using System.Net;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalRestaurantServer : IAsyncDisposable
{
    private readonly LocalDatabaseFactory _databaseFactory;
    private WebApplication? _application;

    public LocalRestaurantServer(LocalDatabaseFactory? databaseFactory = null)
    {
        _databaseFactory = databaseFactory ?? new LocalDatabaseFactory();
    }

    public bool IsRunning => _application is not null;

    public LocalServerDescriptor? Descriptor { get; private set; }

    public async Task StartAsync(
        LocalServerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        options.Validate();

        if (!options.Enabled)
        {
            return;
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);

        var descriptor = LocalServerDescriptor.Create(options);
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(server =>
        {
            server.ListenAnyIP(options.Port);
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(descriptor);
        builder.Services.AddSingleton(_databaseFactory);
        builder.Services.AddSingleton<OperationalSnapshotStore>();
        builder.Services.AddSingleton<WindowsActivationStore>();
        builder.Services.AddSingleton<LocalPairingService>();
        builder.Services.AddSingleton<LocalOrderService>();

        var app = builder.Build();

        app.MapGet("/api/v1/health", (LocalServerOptions serverOptions) =>
            Results.Ok(new
            {
                status = "ok",
                service = "BusinessOS Restaurant Desktop",
                tenant_id = serverOptions.TenantId,
                mode = "local",
                api_version = "v1",
                server_time = DateTimeOffset.UtcNow,
            }));

        app.MapGet("/api/v1/local/info", (LocalServerDescriptor local) =>
            Results.Ok(new
            {
                tenant_id = local.TenantId,
                port = local.Port,
                base_urls = local.BaseUrls,
                machine_name = Environment.MachineName,
            }));

        app.MapGet("/api/v1/local/catalog", async (
            OperationalSnapshotStore store,
            CancellationToken token) =>
        {
            var catalog = await store.LoadCatalogAsync(token);
            var branches = catalog.Branches.ToDictionary(value => value.Id, StringComparer.Ordinal);
            var linksByItem = catalog.MenuItemModifierGroups
                .GroupBy(value => value.MenuItemId)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var optionsByGroup = catalog.ModifierOptions
                .GroupBy(value => value.ModifierGroupId)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            var menu = catalog.Categories.Select(category => new
            {
                id = category.Id,
                name = category.Name,
                sort_order = category.SortOrder,
                items = catalog.Items
                    .Where(item => item.MenuCategoryId == category.Id)
                    .Select(item => new
                    {
                        id = item.Id,
                        menu_category_id = item.MenuCategoryId,
                        sku = item.Sku,
                        name = item.Name,
                        description = item.Description,
                        price = item.Price,
                        currency = item.Currency,
                        sort_order = item.SortOrder,
                        modifier_groups = linksByItem.TryGetValue(item.Id, out var itemLinks)
                            ? itemLinks
                                .Where(link => catalog.ModifierGroups.ContainsKey(link.ModifierGroupId))
                                .Select(link =>
                                {
                                    var group = catalog.ModifierGroups[link.ModifierGroupId];
                                    return new
                                    {
                                        id = group.Id,
                                        name = group.Name,
                                        min_selections = group.MinSelections,
                                        max_selections = group.MaxSelections,
                                        sort_order = link.SortOrder,
                                        options = optionsByGroup.TryGetValue(group.Id, out var groupOptions)
                                            ? groupOptions.Select(option => new
                                            {
                                                id = option.Id,
                                                name = option.Name,
                                                price_delta = option.PriceDelta,
                                                sort_order = option.SortOrder,
                                            }).ToArray()
                                            : [],
                                    };
                                })
                                .ToArray()
                            : [],
                    })
                    .ToArray(),
            }).ToArray();

            var tables = catalog.Tables.Select(table =>
            {
                var area = catalog.Areas[table.DiningAreaId];
                var branch = branches[area.BranchId];

                return new
                {
                    id = table.Id,
                    code = table.Code,
                    name = table.Name,
                    capacity = table.Capacity,
                    status = table.Status,
                    is_active = table.IsActive,
                    area = new { id = area.Id, name = area.Name },
                    branch = new { id = branch.Id, name = branch.Name },
                };
            }).ToArray();

            return Results.Ok(new
            {
                data = new
                {
                    tenant_id = catalog.State?.TenantId,
                    cursor = catalog.State?.Cursor ?? 0,
                    refreshed_at = catalog.State?.RefreshedAtUtc,
                    branches = catalog.Branches.Select(value => new
                    {
                        id = value.Id,
                        code = value.Code,
                        name = value.Name,
                        is_active = value.IsActive,
                    }).ToArray(),
                    menu,
                    tables,
                },
            });
        });

        app.MapPost("/api/v1/local/admin/pairing-code", async (
            HttpContext context,
            LocalPairingCodeRequest request,
            LocalPairingService pairing) =>
        {
            var remote = context.Connection.RemoteIpAddress;

            if (remote is null || !IPAddress.IsLoopback(remote))
            {
                return Results.Forbid();
            }

            try
            {
                var code = await pairing.CreatePairingCodeAsync(
                    request.StaffUserId,
                    context.RequestAborted);

                return Results.Ok(new
                {
                    data = new
                    {
                        pairing_code = code.Code,
                        staff_user_id = code.StaffUserId,
                        staff_name = code.StaffName,
                        expires_at = code.ExpiresAtUtc,
                    },
                });
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or UnauthorizedAccessException)
            {
                return Results.UnprocessableEntity(new
                {
                    code = "pairing_unavailable",
                    message = exception.Message,
                });
            }
        });

        app.MapPost("/api/v1/local/pair", async (
            HttpContext context,
            LocalPairRequest request,
            LocalPairingService pairing) =>
        {
            try
            {
                var paired = await pairing.PairAsync(request, context.RequestAborted);

                return Results.Ok(new
                {
                    device = new
                    {
                        id = paired.Device.Id,
                        uid = paired.Device.DeviceUid,
                        name = paired.Device.DeviceName,
                        platform = "android",
                        staff_user_id = paired.Device.StaffUserId,
                    },
                    device_secret = paired.DeviceSecret,
                    access_token = paired.AccessToken,
                    token_type = paired.TokenType,
                    tenant_id = paired.TenantId,
                    public_key = paired.PublicKey,
                    lease = paired.Lease,
                });
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or UnauthorizedAccessException)
            {
                return Results.UnprocessableEntity(new
                {
                    code = "pairing_failed",
                    message = exception.Message,
                });
            }
        });

        app.MapGet("/api/v1/license/public-key", async (
            HttpContext context,
            WindowsActivationStore activations) =>
        {
            var activation = await activations.LoadAsync(context.RequestAborted);

            if (activation is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                algorithm = "Ed25519",
                key_id = activation.PublicKeyId,
                schema_version = 1,
                public_key = activation.PublicKey,
            });
        });

        app.MapPost("/api/v1/license/lease", async (
            HttpContext context,
            LocalPairingService pairing) =>
        {
            try
            {
                var auth = await pairing.AuthenticateDeviceAsync(
                    context.Request,
                    context.RequestAborted);

                return Results.Ok(new
                {
                    lease = auth.HostActivation.Lease,
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = exception.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        app.MapGet("/api/v1/sync/bootstrap", async (
            HttpContext context,
            LocalPairingService pairing,
            LocalOrderService orders) =>
        {
            try
            {
                var auth = await pairing.AuthenticateAsync(
                    context.Request,
                    context.RequestAborted);
                var data = await orders.BootstrapAsync(auth, context.RequestAborted);
                return Results.Ok(new { data });
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = exception.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        app.MapPost("/api/v1/sync/push", async (
            HttpContext context,
            LocalSyncPushRequest request,
            LocalPairingService pairing,
            LocalOrderService orders) =>
        {
            try
            {
                var auth = await pairing.AuthenticateAsync(
                    context.Request,
                    context.RequestAborted);
                var data = await orders.PushAsync(auth, request, context.RequestAborted);
                return Results.Ok(new { data });
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = exception.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        app.MapGet("/api/v1/sync/pull", async (
            HttpContext context,
            LocalPairingService pairing,
            LocalOrderService orders,
            long cursor = 0,
            int limit = 100) =>
        {
            try
            {
                var auth = await pairing.AuthenticateAsync(
                    context.Request,
                    context.RequestAborted);
                var data = await orders.PullAsync(
                    auth,
                    cursor,
                    limit,
                    context.RequestAborted);
                return Results.Ok(new { data });
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = exception.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        app.MapFallback(() => Results.Json(
            new
            {
                code = "local_endpoint_not_ready",
                message = "This local restaurant endpoint is not available in the installed desktop batch yet.",
            },
            statusCode: StatusCodes.Status404NotFound));

        try
        {
            await app.StartAsync(cancellationToken);
            _application = app;
            Descriptor = descriptor;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null)
        {
            return;
        }

        var application = _application;
        _application = null;
        Descriptor = null;

        await application.StopAsync(cancellationToken);
        await application.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}

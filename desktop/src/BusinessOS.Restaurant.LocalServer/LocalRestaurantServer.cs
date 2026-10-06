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

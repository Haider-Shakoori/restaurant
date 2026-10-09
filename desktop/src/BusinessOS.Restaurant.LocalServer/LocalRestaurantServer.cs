using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        builder.Services.AddSingleton(new ConnectionSettingsStore());
        builder.Services.AddSingleton(new WindowsActivationStore());
        builder.Services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        });
        builder.Services.AddSingleton<LocalCloudProxy>();
        builder.Services.AddSingleton<LocalTerminalManagementService>();
        builder.Services.AddSingleton<LocalReportingService>();
        builder.Services.AddSingleton<LocalTerminalAuthenticator>();
        builder.Services.AddSingleton<LocalRestaurantSettingsService>();
        builder.Services.AddSingleton<LocalKitchenService>();
        builder.Services.AddSingleton<LocalInventoryService>();
        builder.Services.AddSingleton<LocalCashierService>();
        builder.Services.AddSingleton<LocalOperationsControlService>();
        builder.Services.AddSingleton<LocalSyncService>();

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
            HttpRequest request,
            LocalServerOptions serverOptions,
            LocalTerminalAuthenticator authenticator,
            LocalSyncService sync,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                serverOptions,
                allowCloudPairing: true,
                token);

            if (principal is null)
            {
                return Results.Json(
                    new
                    {
                        code = "unauthenticated",
                        message = "This Android terminal has not been validated for local restaurant access.",
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var data = await sync.BootstrapAsync(principal, token);
            return Results.Ok(new { data });
        });

        // Menu images are referenced by the local catalog and fetched by paired
        // waiter devices. Authenticate before reading any file from disk.
        app.MapGet("/menu-images/{fileName}", async (
            string fileName,
            HttpRequest request,
            LocalServerOptions serverOptions,
            LocalTerminalAuthenticator authenticator,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request, serverOptions, allowCloudPairing: true, token);
            if (principal is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName != Path.GetFileName(fileName) ||
                fileName.Length > 100)
                return Results.BadRequest();

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => null,
            };
            if (contentType is null)
                return Results.NotFound();

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BusinessOS", "Restaurant", "menu-images");
            var imagePath = Path.Combine(directory, fileName);
            if (!File.Exists(imagePath))
                return Results.NotFound();

            return Results.File(imagePath, contentType, enableRangeProcessing: true);
        });

        app.MapLocalControlPlane();
        app.MapLocalDiagnostics();
        app.MapLocalSync();
        app.MapLocalKitchen();
        app.MapLocalCashier();
        app.MapLocalOperationsControl();
        app.MapLocalInventory();
        app.MapLocalReports();

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

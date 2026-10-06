using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalRestaurantServer : IAsyncDisposable
{
    private WebApplication? _application;

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

        var descriptor = LocalServerDescriptor.Create(options);
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(server =>
        {
            server.ListenAnyIP(options.Port);
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(descriptor);

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

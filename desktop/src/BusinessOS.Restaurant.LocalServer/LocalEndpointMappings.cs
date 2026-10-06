using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BusinessOS.Restaurant.LocalServer;

public static class LocalEndpointMappings
{
    public static void MapLocalControlPlane(this WebApplication app)
    {
        app.MapGet("/api/v1/license/public-key", (
            HttpRequest request,
            LocalCloudProxy proxy,
            CancellationToken token) =>
            proxy.ForwardAsync(request, "api/v1/license/public-key", token));

        app.MapPost("/api/v1/license/activate", (
            HttpRequest request,
            LocalCloudProxy proxy,
            CancellationToken token) =>
            proxy.ForwardAsync(request, "api/v1/license/activate", token));

        app.MapPost("/api/v1/auth/login", (
            HttpRequest request,
            LocalCloudProxy proxy,
            CancellationToken token) =>
            proxy.ForwardAsync(request, "api/v1/auth/login", token));

        app.MapPost("/api/v1/license/lease", (
            HttpRequest request,
            LocalCloudProxy proxy,
            CancellationToken token) =>
            proxy.ForwardAsync(request, "api/v1/license/lease", token));
    }

    public static void MapLocalSync(this WebApplication app)
    {
        app.MapGet("/api/v1/sync/bootstrap", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalSyncService sync,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
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

        app.MapPost("/api/v1/sync/push", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalSyncService sync,
            LocalSyncPushRequest body,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
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

            var data = await sync.PushAsync(principal, body, token);
            return Results.Ok(new { data });
        });

        app.MapGet("/api/v1/sync/pull", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalSyncService sync,
            long? cursor,
            int? limit,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
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

            var data = await sync.PullAsync(
                principal,
                Math.Max(0, cursor ?? 0),
                limit ?? 100,
                token);

            return Results.Ok(new { data });
        });
    }
}

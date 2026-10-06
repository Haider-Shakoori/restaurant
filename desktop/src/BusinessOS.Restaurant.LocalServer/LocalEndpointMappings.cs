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
    public static void MapLocalKitchen(this WebApplication app)
    {
        app.MapGet("/api/v1/kitchen/tickets", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalKitchenService kitchen,
            string? station_id,
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
                        message = "This kitchen terminal has not been validated for local restaurant access.",
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (principal.UserRole is not ("owner" or "manager" or "kitchen"))
            {
                return Results.Json(
                    new { code = "forbidden", message = "This user cannot operate the kitchen display." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var data = await kitchen.ActiveTicketsAsync(station_id, token);
            return Results.Ok(new { data });
        });

        app.MapPost("/api/v1/kitchen/tickets/{ticketId}/start", async (
            string ticketId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalKitchenService kitchen,
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
                    new { code = "unauthenticated", message = "Kitchen terminal authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var data = await kitchen.StartAsync(ticketId, principal, token);
                return Results.Ok(new { data });
            }
            catch (LocalSyncConflictException conflict)
            {
                return Results.Json(
                    new { code = conflict.Code, message = conflict.Message },
                    statusCode: conflict.Status == "rejected"
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status409Conflict);
            }
        });

        app.MapPost("/api/v1/kitchen/tickets/{ticketId}/ready", async (
            string ticketId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalKitchenService kitchen,
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
                    new { code = "unauthenticated", message = "Kitchen terminal authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var data = await kitchen.ReadyAsync(ticketId, principal, token);
                return Results.Ok(new { data });
            }
            catch (LocalSyncConflictException conflict)
            {
                return Results.Json(
                    new { code = conflict.Code, message = conflict.Message },
                    statusCode: conflict.Status == "rejected"
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status409Conflict);
            }
        });

        app.MapPut("/api/v1/kitchen/stations/{stationId}/printer", async (
            string stationId,
            LocalPrinterBindingRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalKitchenService kitchen,
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
                    new { code = "unauthenticated", message = "Restaurant management authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (principal.UserRole is not ("owner" or "manager"))
            {
                return Results.Json(
                    new { code = "forbidden", message = "Only an owner or manager can configure kitchen printers." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                await kitchen.ConfigurePrinterAsync(
                    stationId,
                    body.PrinterName,
                    body.Copies,
                    body.Enabled,
                    token);
                return Results.NoContent();
            }
            catch (LocalSyncConflictException conflict)
            {
                return Results.Json(
                    new { code = conflict.Code, message = conflict.Message },
                    statusCode: StatusCodes.Status409Conflict);
            }
        });
    }

    public static void MapLocalCashier(this WebApplication app)
    {
        app.MapGet("/api/v1/pos/bills", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (principal.UserRole is not ("owner" or "admin" or "manager" or "cashier"))
            {
                return Results.Json(new { code = "forbidden", message = "This user cannot operate the cashier POS." }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new { data = await cashier.OpenBillsAsync(token) });
        });

        app.MapPost("/api/v1/cashier/sessions", async (
            LocalOpenCashierSessionRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.OpenSessionAsync(body.BranchId, body.OpeningCash, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/cashier/sessions/{sessionId}/close", async (
            string sessionId,
            LocalCloseCashierSessionRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.CloseSessionAsync(sessionId, body.DeclaredCash, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/orders/{orderId}/serve", async (
            string orderId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Restaurant authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.ServeOrderAsync(orderId, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/orders/{orderId}/bill", async (
            string orderId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.CreateBillAsync(orderId, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/pos/bills/{billId}/discount", async (
            string billId,
            LocalDiscountRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.ApplyDiscountAsync(billId, body.Type, body.Value, body.Reason, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/pos/bills/{billId}/splits", async (
            string billId,
            LocalBillSplitRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var parts = body.Parts.Select(value => new LocalBillSplitPart(value.Label, value.Amount)).ToArray();
                return Results.Ok(new { data = await cashier.CreateSplitsAsync(billId, parts, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/pos/bills/{billId}/payments", async (
            string billId,
            LocalPaymentBody body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var payment = new LocalPaymentRequest(
                    body.CashierSessionId,
                    body.Amount,
                    body.Method,
                    body.ClientPaymentId,
                    body.Reference,
                    body.BillSplitId);

                return Results.Ok(new { data = await cashier.AddPaymentAsync(billId, payment, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/orders/{orderId}/transfer", async (
            string orderId,
            LocalTransferOrderRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.TransferOrderAsync(orderId, body.TargetTableId, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/orders/{targetOrderId}/merge", async (
            string targetOrderId,
            LocalMergeOrdersRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await cashier.MergeDraftOrdersAsync(targetOrderId, body.SourceOrderId, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPut("/api/v1/pos/receipt-printer", async (
            LocalReceiptPrinterRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                await cashier.ConfigureReceiptPrinterAsync(body.PrinterName, body.Copies, body.Enabled, principal, token);
                return Results.NoContent();
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/pos/bills/{billId}/receipt", async (
            string billId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalCashierService cashier,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                await cashier.QueueReceiptAsync(billId, principal, token);
                return Results.Accepted();
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });
    }

    private static IResult CashierConflict(LocalSyncConflictException conflict) =>
        Results.Json(
            new { code = conflict.Code, message = conflict.Message },
            statusCode: conflict.Status == "rejected"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status409Conflict);

}

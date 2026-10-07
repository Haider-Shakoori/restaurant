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

    public static void MapLocalDiagnostics(this WebApplication app)
    {
        app.MapPost("/api/v1/local/heartbeat", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalTerminalManagementService terminals,
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
                        message = "This terminal is not allowed to use the local restaurant host.",
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var diagnostics = await terminals.GetDiagnosticsAsync(
                options.TenantId,
                cancellationToken: token);

            return Results.Ok(new
            {
                data = new
                {
                    terminal_id = principal.DeviceId,
                    network_mode = diagnostics.NetworkMode,
                    local_operations_allowed = diagnostics.LocalOperationsAllowed,
                    sync_enabled = diagnostics.SyncEnabled,
                    offline_valid_until = diagnostics.OfflineValidUntil,
                    server_time = DateTimeOffset.UtcNow,
                },
            });
        });

        app.MapGet("/api/v1/local/diagnostics", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalTerminalManagementService terminals,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
                allowCloudPairing: false,
                token);

            if (principal is null)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = "Manager authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!IsTerminalManager(principal))
            {
                return Results.Json(
                    new { code = "forbidden", message = "Only an owner or manager can view LAN diagnostics." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                data = await terminals.GetDiagnosticsAsync(
                    options.TenantId,
                    cancellationToken: token),
            });
        });

        app.MapGet("/api/v1/local/terminals", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalTerminalManagementService terminals,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
                allowCloudPairing: false,
                token);

            if (principal is null)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = "Manager authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!IsTerminalManager(principal))
            {
                return Results.Json(
                    new { code = "forbidden", message = "Only an owner or manager can manage LAN terminals." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                data = await terminals.GetTerminalsAsync(cancellationToken: token),
            });
        });

        app.MapPut("/api/v1/local/terminals/{deviceId}/enabled", async (
            string deviceId,
            LocalTerminalEnabledRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalTerminalManagementService terminals,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
                allowCloudPairing: false,
                token);

            if (principal is null)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = "Manager authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!IsTerminalManager(principal))
            {
                return Results.Json(
                    new { code = "forbidden", message = "Only an owner or manager can manage LAN terminals." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.Equals(principal.DeviceId, deviceId, StringComparison.Ordinal) && !body.Enabled)
            {
                return Results.Json(
                    new { code = "self_disable_blocked", message = "The current management terminal cannot disable itself." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var updated = await terminals.SetEnabledAsync(
                deviceId,
                body.Enabled,
                principal.UserId,
                token);

            return updated
                ? Results.Ok(new { data = new { device_id = deviceId, enabled = body.Enabled } })
                : Results.Json(
                    new { code = "terminal_not_found", message = "The terminal does not exist." },
                    statusCode: StatusCodes.Status404NotFound);
        });

        app.MapDelete("/api/v1/local/terminals/{deviceId}", async (
            string deviceId,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalTerminalManagementService terminals,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(
                request,
                options,
                allowCloudPairing: false,
                token);

            if (principal is null)
            {
                return Results.Json(
                    new { code = "unauthenticated", message = "Manager authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!IsTerminalManager(principal))
            {
                return Results.Json(
                    new { code = "forbidden", message = "Only an owner or manager can unpair LAN terminals." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.Equals(principal.DeviceId, deviceId, StringComparison.Ordinal))
            {
                return Results.Json(
                    new { code = "self_unpair_blocked", message = "The current management terminal cannot unpair itself." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            return await terminals.UnpairAsync(deviceId, token)
                ? Results.NoContent()
                : Results.Json(
                    new { code = "terminal_not_found", message = "The terminal does not exist." },
                    statusCode: StatusCodes.Status404NotFound);
        });
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

            if (principal.UserRole is not ("owner" or "manager" or "kitchen" or "expo"))
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

        app.MapPost("/api/v1/kitchen/items/{itemId}/start", async (
            string itemId,
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
                var data = await kitchen.StartItemAsync(itemId, principal, token);
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

        app.MapPost("/api/v1/kitchen/items/{itemId}/ready", async (
            string itemId,
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
                var data = await kitchen.ReadyItemAsync(itemId, principal, token);
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

        app.MapPost("/api/v1/kitchen/items/{itemId}/expo-pass", async (
            string itemId,
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
                    new { code = "unauthenticated", message = "Expo authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var data = await kitchen.PassExpoItemAsync(itemId, principal, token);
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

        app.MapPost("/api/v1/kitchen/items/{itemId}/refire", async (
            string itemId,
            LocalKitchenRefireRequest body,
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
                    new { code = "unauthenticated", message = "Kitchen authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var data = await kitchen.RefireItemAsync(
                    itemId,
                    body.ClientRefireId,
                    body.Reason,
                    principal,
                    token);
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

        app.MapGet("/api/v1/restaurant/settings", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalRestaurantSettingsService settings,
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
                    new { code = "unauthenticated", message = "Restaurant authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var data = await settings.GetAsync(token);
            return Results.Ok(new { data = LocalRestaurantSettingsService.ToPayload(data) });
        });

        app.MapPut("/api/v1/restaurant/settings", async (
            RestaurantWorkflowSettingsUpdate body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalRestaurantSettingsService settings,
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

            try
            {
                var updated = await settings.UpdateAsync(body, principal, token);
                return Results.Ok(new { data = LocalRestaurantSettingsService.ToPayload(updated) });
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

    public static void MapLocalOperationsControl(this WebApplication app)
    {
        app.MapPost("/api/v1/shifts", async (
            LocalStartShiftRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Staff authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await operations.StartShiftAsync(body.BranchId, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/shifts/{shiftId}/close", async (
            string shiftId,
            LocalEndShiftRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Staff authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await operations.EndShiftAsync(
                        shiftId,
                        body.BreakMinutes,
                        body.Note,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/shifts/active", async (
            string? branch_id,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await operations.ActiveShiftsAsync(branch_id, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/daily-closings", async (
            string? branch_id,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier or management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await operations.ListClosingsAsync(branch_id, principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/daily-closings/finalize", async (
            LocalFinalizeDailyClosingRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Cashier or management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await operations.FinalizeDailyClosingAsync(
                        body.BranchId,
                        body.BusinessDate,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/daily-closings/{closingId}/reopen", async (
            string closingId,
            LocalReopenDailyClosingRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await operations.ReopenDailyClosingAsync(
                        closingId,
                        body.Reason,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/audit", async (
            long? cursor,
            int? limit,
            string? category,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalOperationsControlService operations,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await operations.AuditAsync(
                        Math.Max(0, cursor ?? 0),
                        limit ?? 100,
                        category,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });
    }

    public static void MapLocalInventory(this WebApplication app)
    {
        app.MapGet("/api/v1/inventory/items", async (
            string? branch_id,
            bool? low_stock,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.ItemsAsync(
                        branch_id,
                        low_stock ?? false,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/inventory/items", async (
            LocalInventoryItemRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.CreateItemAsync(
                        body.Sku,
                        body.Name,
                        body.BaseUnit,
                        body.PurchaseUnit,
                        body.PurchaseToBaseFactor,
                        body.ReorderLevel,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/inventory/items/{itemId}/adjustments", async (
            string itemId,
            LocalInventoryAdjustmentRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.AdjustAsync(
                        body.BranchId,
                        itemId,
                        body.QuantityDelta,
                        body.ClientAdjustmentId,
                        body.Reason,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/inventory/movements", async (
            string? branch_id,
            string? inventory_item_id,
            int? limit,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.MovementsAsync(
                        branch_id,
                        inventory_item_id,
                        limit ?? 100,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/suppliers", async (
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new { data = await inventory.SuppliersAsync(principal, token) });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/suppliers", async (
            LocalSupplierRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.CreateSupplierAsync(
                        body.Code,
                        body.Name,
                        body.Phone,
                        body.Email,
                        body.Address,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/recipes", async (
            string? branch_id,
            string? menu_item_id,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.RecipesAsync(
                        branch_id,
                        menu_item_id,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/menu/items/{menuItemId}/recipes", async (
            string menuItemId,
            LocalRecipeRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var components = body.Items
                    .Select(value => new LocalRecipeComponentRequest(
                        value.InventoryItemId,
                        value.QuantityBase))
                    .ToArray();

                return Results.Ok(new
                {
                    data = await inventory.CreateRecipeVersionAsync(
                        body.BranchId,
                        menuItemId,
                        body.Name,
                        components,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapGet("/api/v1/purchasing/orders", async (
            string? branch_id,
            string? status,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(new
                {
                    data = await inventory.PurchaseOrdersAsync(
                        branch_id,
                        status,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/purchasing/orders", async (
            LocalPurchaseOrderRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var lines = body.Lines
                    .Select(value => new LocalPurchaseOrderLineRequest(
                        value.InventoryItemId,
                        value.PurchaseQuantity,
                        value.UnitCost))
                    .ToArray();

                return Results.Ok(new
                {
                    data = await inventory.CreatePurchaseOrderAsync(
                        body.BranchId,
                        body.SupplierId,
                        lines,
                        body.Notes,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });

        app.MapPost("/api/v1/purchasing/orders/{purchaseOrderId}/receive", async (
            string purchaseOrderId,
            LocalGoodsReceiptRequest body,
            HttpRequest request,
            LocalServerOptions options,
            LocalTerminalAuthenticator authenticator,
            LocalInventoryService inventory,
            CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null)
            {
                return Results.Json(new { code = "unauthenticated", message = "Inventory authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var lines = body.Lines
                    .Select(value => new LocalReceivePurchaseOrderLineRequest(
                        value.PurchaseOrderLineId,
                        value.PurchaseQuantity))
                    .ToArray();

                return Results.Ok(new
                {
                    data = await inventory.ReceivePurchaseOrderAsync(
                        purchaseOrderId,
                        lines,
                        body.ClientReceiptId,
                        body.Notes,
                        principal,
                        token),
                });
            }
            catch (LocalSyncConflictException conflict)
            {
                return CashierConflict(conflict);
            }
        });
    }

    public static void MapLocalReports(this WebApplication app)
    {
        app.MapGet("/api/v1/reports/summary", async (string branch_id, DateOnly from, DateOnly to, HttpRequest request, LocalServerOptions options, LocalTerminalAuthenticator authenticator, LocalReportingService reports, CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null) return Results.Json(new { code = "unauthenticated", message = "Management authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            if (!CanViewReports(principal)) return Results.Json(new { code = "forbidden", message = "This role cannot view financial reports." }, statusCode: StatusCodes.Status403Forbidden);
            if (to < from) return Results.BadRequest(new { code = "invalid_period", message = "The report end date must be on or after the start date." });
            return Results.Ok(new { data = await reports.SummaryAsync(branch_id, from, to, token) });
        });

        app.MapGet("/api/v1/reports/payments", async (string branch_id, DateOnly from, DateOnly to, HttpRequest request, LocalServerOptions options, LocalTerminalAuthenticator authenticator, LocalReportingService reports, CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null) return Results.Json(new { code = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            if (!CanViewReports(principal)) return Results.Json(new { code = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { data = await reports.PaymentsAsync(branch_id, from, to, token) });
        });

        app.MapGet("/api/v1/reports/top-items", async (string branch_id, DateOnly from, DateOnly to, int? limit, HttpRequest request, LocalServerOptions options, LocalTerminalAuthenticator authenticator, LocalReportingService reports, CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null) return Results.Json(new { code = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            if (!CanViewReports(principal)) return Results.Json(new { code = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { data = await reports.TopItemsAsync(branch_id, from, to, limit ?? 10, token) });
        });

        app.MapGet("/api/v1/reports/closings", async (string branch_id, DateOnly from, DateOnly to, HttpRequest request, LocalServerOptions options, LocalTerminalAuthenticator authenticator, LocalReportingService reports, CancellationToken token) =>
        {
            var principal = await authenticator.AuthenticateAsync(request, options, true, token);
            if (principal is null) return Results.Json(new { code = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            if (!CanViewReports(principal)) return Results.Json(new { code = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { data = await reports.ClosingsAsync(branch_id, from, to, token) });
        });
    }

    private static bool CanViewReports(LocalTerminalPrincipal principal) =>
        principal.UserRole.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
        principal.UserRole.Equals("manager", StringComparison.OrdinalIgnoreCase) ||
        principal.UserRole.Equals("accountant", StringComparison.OrdinalIgnoreCase) ||
        principal.UserRole.Equals("auditor", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalManager(LocalTerminalPrincipal principal) =>
        principal.UserRole.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
        principal.UserRole.Equals("manager", StringComparison.OrdinalIgnoreCase);

    private static IResult CashierConflict(LocalSyncConflictException conflict) =>
        Results.Json(
            new { code = conflict.Code, message = conflict.Message },
            statusCode: conflict.Status == "rejected"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status409Conflict);

}

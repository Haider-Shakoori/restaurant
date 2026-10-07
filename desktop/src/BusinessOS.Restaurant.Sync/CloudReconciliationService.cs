using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Sync;

public sealed record CloudReconciliationRunResult(
    int Pushed,
    int Accepted,
    int Conflicts,
    int Pulled,
    long Cursor);

public sealed class CloudReconciliationService
{
    private static readonly string[] PendingStatuses = ["pending", "retry"];
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly CloudReconciliationClient _client;

    public CloudReconciliationService(
        LocalDatabaseFactory databaseFactory,
        CloudReconciliationClient client)
    {
        _databaseFactory = databaseFactory;
        _client = client;
    }

    public async Task<CloudReconciliationRunResult> RunOnceAsync(
        ActivationState activation,
        AuthSession session,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                activation.Snapshot.TenantId,
                session.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The signed-in user does not belong to the activated tenant.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);

        var pushed = 0;
        var accepted = 0;
        var conflicts = 0;
        var pulled = 0;

        await using (var db = _databaseFactory.Create())
        {
            var state = await StateAsync(db, cancellationToken);
            var pending = (await db.CloudOutbox
                    .Where(value => PendingStatuses.Contains(value.Status))
                    .ToArrayAsync(cancellationToken))
                .OrderBy(value => value.OccurredAtUtc)
                .Take(50)
                .ToArray();

            if (pending.Length > 0)
            {
                var mutations = pending.Select(value => new CloudMutationEnvelope(
                    value.Id,
                    value.Operation,
                    value.EntityType,
                    value.LocalEntityId,
                    value.ActorPublicId,
                    JsonSerializer.Deserialize<JsonElement>(value.PayloadJson),
                    value.OccurredAtUtc)).ToArray();

                foreach (var mutation in pending)
                {
                    mutation.Attempts += 1;
                    mutation.LastAttemptAtUtc = DateTimeOffset.UtcNow;
                    mutation.Status = "sending";
                }

                await db.SaveChangesAsync(cancellationToken);
                pushed = pending.Length;

                CloudPushData response;

                try
                {
                    response = await _client.PushAsync(
                        activation,
                        session,
                        mutations,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is HttpRequestException ||
                    exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    foreach (var mutation in pending)
                    {
                        mutation.Status = "retry";
                        mutation.ErrorCode = "cloud_unreachable";
                        mutation.ErrorMessage = exception.Message;
                    }

                    state.LastPushAtUtc = DateTimeOffset.UtcNow;
                    state.LastError = exception.Message;
                    await db.SaveChangesAsync(cancellationToken);

                    return new CloudReconciliationRunResult(
                        pushed,
                        accepted,
                        conflicts,
                        pulled,
                        state.PullCursor);
                }

                foreach (var result in response.Results)
                {
                    var mutation = pending.SingleOrDefault(value => value.Id == result.MutationId);
                    if (mutation is null)
                    {
                        continue;
                    }

                    if (string.Equals(result.Status, "accepted", StringComparison.Ordinal))
                    {
                        mutation.Status = "synced";
                        mutation.CloudEntityId = result.EntityId;
                        mutation.ErrorCode = null;
                        mutation.ErrorMessage = null;
                        mutation.SyncedAtUtc = DateTimeOffset.UtcNow;
                        accepted++;

                        if (!string.IsNullOrWhiteSpace(result.EntityId))
                        {
                            UpsertLink(
                                db,
                                result.EntityType ?? mutation.EntityType,
                                mutation.LocalEntityId,
                                result.EntityId!);
                        }

                        continue;
                    }

                    mutation.Status = string.Equals(result.Status, "rejected", StringComparison.Ordinal)
                        ? "rejected"
                        : "conflict";
                    mutation.ErrorCode = result.Code;
                    mutation.ErrorMessage = result.Message;
                    conflicts++;

                    AddConflict(
                        db,
                        mutation,
                        result.Code ?? "cloud_conflict",
                        result.Message ?? "The cloud rejected this local mutation.",
                        cloudPayload: null);
                }

                state.LastPushAtUtc = DateTimeOffset.UtcNow;
                state.LastSuccessAtUtc = DateTimeOffset.UtcNow;
                state.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        for (var page = 0; page < 5; page++)
        {
            long cursor;

            await using (var db = _databaseFactory.Create())
            {
                cursor = (await StateAsync(db, cancellationToken)).PullCursor;
            }

            CloudPullData pageData;

            try
            {
                pageData = await _client.PullAsync(
                    activation,
                    session,
                    cursor,
                    100,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is HttpRequestException ||
                exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await using var db = _databaseFactory.Create();
                var state = await StateAsync(db, cancellationToken);
                state.LastPullAtUtc = DateTimeOffset.UtcNow;
                state.LastError = exception.Message;
                await db.SaveChangesAsync(cancellationToken);
                break;
            }

            await using (var db = _databaseFactory.Create())
            {
                var state = await StateAsync(db, cancellationToken);

                foreach (var change in pageData.Changes)
                {
                    await ApplyChangeAsync(db, change, cancellationToken);
                    pulled++;
                }

                state.PullCursor = Math.Max(state.PullCursor, pageData.Cursor);
                state.LastPullAtUtc = DateTimeOffset.UtcNow;
                state.LastSuccessAtUtc = DateTimeOffset.UtcNow;
                state.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (!pageData.HasMore)
            {
                break;
            }
        }

        await using (var db = _databaseFactory.Create())
        {
            var state = await StateAsync(db, cancellationToken);
            return new CloudReconciliationRunResult(
                pushed,
                accepted,
                conflicts,
                pulled,
                state.PullCursor);
        }
    }

    private async Task ApplyChangeAsync(
        RestaurantDbContext db,
        CloudPullChange change,
        CancellationToken cancellationToken)
    {
        if (change.Payload is null)
        {
            return;
        }

        var localId = string.Equals(change.EntityType, "restaurant_settings", StringComparison.Ordinal)
            ? "workflow"
            : await ResolveLocalIdAsync(db, change, cancellationToken);

        if (localId is not null)
        {
            var hasPending = await db.CloudOutbox.AnyAsync(
                value =>
                    value.EntityType == change.EntityType &&
                    value.LocalEntityId == localId &&
                    PendingStatuses.Contains(value.Status),
                cancellationToken);

            if (hasPending)
            {
                var mutation = (await db.CloudOutbox
                        .Where(value =>
                            value.EntityType == change.EntityType &&
                            value.LocalEntityId == localId &&
                            PendingStatuses.Contains(value.Status))
                        .ToArrayAsync(cancellationToken))
                    .OrderBy(value => value.OccurredAtUtc)
                    .First();

                AddConflict(
                    db,
                    mutation,
                    "cloud_changed_while_local_pending",
                    "The cloud changed this record while a local mutation is still pending.",
                    change.Payload.Value.GetRawText());
                return;
            }
        }

        if (string.Equals(change.Operation, "delete", StringComparison.Ordinal))
        {
            await ApplyCatalogDeleteAsync(db, change, cancellationToken);
            return;
        }

        var payload = change.Payload.Value;

        switch (change.EntityType)
        {
            case "order":
                await ApplyOrderAsync(db, change.EntityId, payload, localId, cancellationToken);
                break;
            case "bill":
                await ApplyBillAsync(db, change.EntityId, payload, localId, cancellationToken);
                break;
            case "cashier_session":
                await ApplyCashierSessionAsync(db, change.EntityId, payload, localId, cancellationToken);
                break;
            case "inventory_item":
                await ApplyInventoryItemAsync(db, change.EntityId, payload, localId, cancellationToken);
                break;
            case "inventory_balance":
                await ApplyInventoryBalanceAsync(db, payload, cancellationToken);
                break;
            case "supplier":
                await ApplySupplierAsync(db, change.EntityId, payload, localId, cancellationToken);
                break;
            case "purchase_order":
                await ApplyPurchaseOrderAsync(db, payload, localId, cancellationToken);
                break;
            case "daily_closing":
                await ApplyDailyClosingAsync(db, payload, localId, cancellationToken);
                break;
            case "menu_category":
                await ApplyMenuCategoryAsync(db, change.EntityId, payload, cancellationToken);
                break;
            case "menu_item":
                await ApplyMenuItemAsync(db, change.EntityId, payload, cancellationToken);
                break;
            case "dining_table":
                await ApplyDiningTableAsync(db, change.EntityId, payload, cancellationToken);
                break;
            case "restaurant_settings":
                await ApplyRestaurantSettingsAsync(db, payload, cancellationToken);
                break;
        }
    }

    private static async Task ApplyCatalogDeleteAsync(
        RestaurantDbContext db,
        CloudPullChange change,
        CancellationToken cancellationToken)
    {
        switch (change.EntityType)
        {
            case "menu_item":
            {
                var item = await db.MenuItems.SingleOrDefaultAsync(
                    value => value.Id == change.EntityId,
                    cancellationToken);
                if (item is not null)
                {
                    item.IsAvailable = false;
                    AddLanChange(db, "menu_item", item.Id, "delete", null);
                }
                break;
            }
            case "menu_category":
            {
                var category = await db.MenuCategories.SingleOrDefaultAsync(
                    value => value.Id == change.EntityId,
                    cancellationToken);
                if (category is not null)
                {
                    category.IsActive = false;
                    AddLanChange(db, "menu_category", category.Id, "delete", null);
                }
                break;
            }
            case "dining_table":
            {
                var table = await db.DiningTables.SingleOrDefaultAsync(
                    value => value.Id == change.EntityId,
                    cancellationToken);
                if (table is not null)
                {
                    table.IsActive = false;
                    AddLanChange(db, "dining_table", table.Id, "delete", null);
                }
                break;
            }
        }
    }

    private static async Task ApplyMenuCategoryAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var category = await db.MenuCategories.SingleOrDefaultAsync(
            value => value.Id == cloudId,
            cancellationToken);

        if (category is null)
        {
            category = new LocalMenuCategory
            {
                Id = cloudId,
                Name = String(payload, "name") ?? "Menu",
            };
            db.MenuCategories.Add(category);
        }

        category.Name = String(payload, "name") ?? category.Name;
        category.SortOrder = Int(payload, "sort_order", category.SortOrder);
        category.IsActive = Bool(payload, "is_active", true);

        AddLanChange(db, "menu_category", category.Id, "upsert", new
        {
            id = category.Id,
            name = category.Name,
            sort_order = category.SortOrder,
            is_active = category.IsActive,
        });
    }

    private static async Task ApplyMenuItemAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var item = await db.MenuItems.SingleOrDefaultAsync(
            value => value.Id == cloudId,
            cancellationToken);

        if (item is null)
        {
            item = new LocalMenuItem
            {
                Id = cloudId,
                Name = String(payload, "name") ?? "Menu item",
            };
            db.MenuItems.Add(item);
        }

        item.MenuCategoryId = String(payload, "menu_category_id") ?? item.MenuCategoryId;
        item.Sku = String(payload, "sku");
        item.Name = String(payload, "name") ?? item.Name;
        item.Description = String(payload, "description");
        item.ImageUrl = String(payload, "image_url");
        item.Price = Decimal(payload, "price", item.Price);
        item.Currency = String(payload, "currency") ?? "AFN";
        item.SortOrder = Int(payload, "sort_order", item.SortOrder);
        item.IsAvailable = Bool(payload, "is_available", true);

        AddLanChange(db, "menu_item", item.Id, "upsert", new
        {
            id = item.Id,
            menu_category_id = item.MenuCategoryId,
            sku = item.Sku,
            name = item.Name,
            description = item.Description,
            image_url = item.ImageUrl,
            price = item.Price.ToString("0.00"),
            currency = item.Currency,
            sort_order = item.SortOrder,
            is_available = item.IsAvailable,
        });
    }

    private static async Task ApplyDiningTableAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var areaId = String(payload, "dining_area_id");
        string? areaName = null;
        string? branchId = null;
        string? branchName = null;

        if (payload.TryGetProperty("dining_area", out var areaPayload) &&
            areaPayload.ValueKind == JsonValueKind.Object)
        {
            areaId = String(areaPayload, "id") ?? areaId;
            areaName = String(areaPayload, "name");

            if (areaPayload.TryGetProperty("branch", out var branchPayload) &&
                branchPayload.ValueKind == JsonValueKind.Object)
            {
                branchId = String(branchPayload, "id");
                branchName = String(branchPayload, "name");
            }
        }

        if (string.IsNullOrWhiteSpace(areaId))
        {
            return;
        }

        var area = await db.DiningAreas.SingleOrDefaultAsync(
            value => value.Id == areaId,
            cancellationToken);

        branchId ??= area?.BranchId;

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            var branch = await db.Branches.SingleOrDefaultAsync(
                value => value.Id == branchId,
                cancellationToken);
            if (branch is null)
            {
                branch = new LocalBranch
                {
                    Id = branchId,
                    Code = branchId,
                    Name = branchName ?? branchId,
                    IsActive = true,
                };
                db.Branches.Add(branch);
            }
            else
            {
                branch.Name = branchName ?? branch.Name;
                branch.IsActive = true;
            }
        }

        if (area is null)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return;
            }

            area = new LocalDiningArea
            {
                Id = areaId,
                BranchId = branchId,
                Name = areaName ?? "Dining area",
                IsActive = true,
            };
            db.DiningAreas.Add(area);
        }
        else
        {
            area.Name = areaName ?? area.Name;
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                area.BranchId = branchId;
            }
            area.IsActive = true;
        }

        var table = await db.DiningTables.SingleOrDefaultAsync(
            value => value.Id == cloudId,
            cancellationToken);
        if (table is null)
        {
            table = new LocalDiningTable
            {
                Id = cloudId,
                DiningAreaId = area.Id,
                Code = String(payload, "code") ?? cloudId,
                Name = String(payload, "name") ?? "Table",
                Status = String(payload, "status") ?? "available",
            };
            db.DiningTables.Add(table);
        }

        table.DiningAreaId = area.Id;
        table.Code = String(payload, "code") ?? table.Code;
        table.Name = String(payload, "name") ?? table.Name;
        table.Capacity = Int(payload, "capacity", table.Capacity == 0 ? 4 : table.Capacity);
        table.Status = String(payload, "status") ?? table.Status;
        table.IsActive = Bool(payload, "is_active", true);

        AddLanChange(db, "dining_table", table.Id, "upsert", new
        {
            id = table.Id,
            code = table.Code,
            name = table.Name,
            capacity = table.Capacity,
            status = table.Status,
            is_active = table.IsActive,
            area = new { id = area.Id, name = area.Name },
            branch = new { id = area.BranchId, name = branchName ?? area.BranchId },
        });
    }

    private static void AddLanChange(
        RestaurantDbContext db,
        string entityType,
        string entityId,
        string operation,
        object? data)
    {
        db.Changes.Add(new LocalChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            OwnerUserId = null,
            DataJson = data is null ? null : JsonSerializer.Serialize(data),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private async Task ApplyOrderAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        var clientOrderId = String(payload, "client_order_id");

        var order = localId is null
            ? null
            : await db.Orders.SingleOrDefaultAsync(value => value.Id == localId, cancellationToken);

        order ??= !string.IsNullOrWhiteSpace(clientOrderId)
            ? await db.Orders.SingleOrDefaultAsync(
                value => value.ClientOrderId == clientOrderId,
                cancellationToken)
            : null;

        if (order is null)
        {
            var tableId = String(payload, "dining_table_id");
            if (string.IsNullOrWhiteSpace(tableId) ||
                !await db.DiningTables.AnyAsync(value => value.Id == tableId, cancellationToken))
            {
                return;
            }

            var waiterId = Long(payload, "waiter_id");
            var staff = await db.StaffUsers.SingleOrDefaultAsync(
                value => value.Id == waiterId,
                cancellationToken);

            if (staff is null)
            {
                return;
            }

            order = new LocalOrder
            {
                Id = Guid.CreateVersion7().ToString("N"),
                ClientOrderId = clientOrderId ?? $"cloud-{cloudId}",
                DiningTableId = tableId,
                WaiterId = staff.Id,
                WaiterPublicId = staff.PublicId,
                WaiterName = staff.Name,
                Status = String(payload, "status") ?? "draft",
                GuestCount = Int(payload, "guest_count", 1),
                Notes = String(payload, "notes"),
                Subtotal = Decimal(payload, "subtotal"),
                Total = Decimal(payload, "total"),
                OpenedAt = Date(payload, "opened_at"),
                SubmittedAt = Date(payload, "submitted_at"),
                ServedAt = Date(payload, "served_at"),
                ClosedAt = Date(payload, "closed_at"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Orders.Add(order);
        }
        else
        {
            order.Status = String(payload, "status") ?? order.Status;
            order.GuestCount = Int(payload, "guest_count", order.GuestCount);
            order.Notes = String(payload, "notes");
            order.Subtotal = Decimal(payload, "subtotal", order.Subtotal);
            order.Total = Decimal(payload, "total", order.Total);
            order.OpenedAt = Date(payload, "opened_at") ?? order.OpenedAt;
            order.SubmittedAt = Date(payload, "submitted_at");
            order.ServedAt = Date(payload, "served_at");
            order.ClosedAt = Date(payload, "closed_at");
            order.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        UpsertLink(db, "order", order.Id, cloudId);
        await db.SaveChangesAsync(cancellationToken);

        var table = await db.DiningTables.SingleOrDefaultAsync(
            value => value.Id == order.DiningTableId,
            cancellationToken);
        if (table is not null)
        {
            table.Status = order.Status is "closed" or "cancelled"
                ? "available"
                : "occupied";
        }

        await SynchronizeCloudOrderItemsAsync(db, order, payload, cancellationToken);
        await SynchronizeCloudKitchenTicketsAsync(db, order, payload, cancellationToken);
    }

    private static async Task SynchronizeCloudOrderItemsAsync(
        RestaurantDbContext db,
        LocalOrder order,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var itemPayload in items.EnumerateArray())
        {
            var clientLineId = String(itemPayload, "client_line_id");
            var cloudLineId = String(itemPayload, "id");
            var menuItemId = String(itemPayload, "menu_item_id");

            if (string.IsNullOrWhiteSpace(clientLineId) ||
                string.IsNullOrWhiteSpace(menuItemId) ||
                !await db.MenuItems.AnyAsync(value => value.Id == menuItemId, cancellationToken))
            {
                continue;
            }

            var item = await db.OrderItems.SingleOrDefaultAsync(
                value => value.OrderId == order.Id && value.ClientLineId == clientLineId,
                cancellationToken);

            if (item is null)
            {
                item = new LocalOrderItem
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    OrderId = order.Id,
                    MenuItemId = menuItemId,
                    ClientLineId = clientLineId,
                    ItemName = String(itemPayload, "item_name") ?? "Menu item",
                    UnitPrice = Decimal(itemPayload, "unit_price"),
                    Quantity = Int(itemPayload, "quantity", 1),
                    LineTotal = Decimal(itemPayload, "line_total"),
                    Notes = String(itemPayload, "notes"),
                    Status = String(itemPayload, "status") ?? "queued",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                db.OrderItems.Add(item);
            }
            else
            {
                item.MenuItemId = menuItemId;
                item.ItemName = String(itemPayload, "item_name") ?? item.ItemName;
                item.UnitPrice = Decimal(itemPayload, "unit_price", item.UnitPrice);
                item.Quantity = Int(itemPayload, "quantity", item.Quantity);
                item.LineTotal = Decimal(itemPayload, "line_total", item.LineTotal);
                item.Notes = String(itemPayload, "notes");
                item.Status = String(itemPayload, "status") ?? item.Status;
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(cloudLineId))
            {
                UpsertLink(db, "order_item", item.Id, cloudLineId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SynchronizeCloudKitchenTicketsAsync(
        RestaurantDbContext db,
        LocalOrder order,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("kitchen_tickets", out var tickets) ||
            tickets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var ticketPayload in tickets.EnumerateArray())
        {
            var ticketNumber = String(ticketPayload, "ticket_number");
            var cloudTicketId = String(ticketPayload, "id");
            var stationId = String(ticketPayload, "kitchen_station_id");

            if (string.IsNullOrWhiteSpace(stationId) &&
                ticketPayload.TryGetProperty("station", out var stationPayload) &&
                stationPayload.ValueKind == JsonValueKind.Object)
            {
                stationId = String(stationPayload, "id");
            }

            if (string.IsNullOrWhiteSpace(ticketNumber) ||
                string.IsNullOrWhiteSpace(stationId))
            {
                continue;
            }

            var station = await db.KitchenStations.SingleOrDefaultAsync(
                value => value.Id == stationId,
                cancellationToken);

            if (station is null &&
                ticketPayload.TryGetProperty("station", out var embeddedStation) &&
                embeddedStation.ValueKind == JsonValueKind.Object)
            {
                var table = await db.DiningTables.SingleAsync(
                    value => value.Id == order.DiningTableId,
                    cancellationToken);
                var area = await db.DiningAreas.SingleAsync(
                    value => value.Id == table.DiningAreaId,
                    cancellationToken);

                station = new LocalKitchenStation
                {
                    Id = stationId,
                    BranchId = String(embeddedStation, "branch_id") ?? area.BranchId,
                    Code = String(embeddedStation, "code") ?? $"CLOUD-{stationId[..Math.Min(8, stationId.Length)]}",
                    Name = String(embeddedStation, "name") ?? "Cloud Kitchen",
                    SortOrder = Int(embeddedStation, "sort_order", 999),
                    IsActive = Bool(embeddedStation, "is_active", true),
                };
                db.KitchenStations.Add(station);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (station is null)
            {
                continue;
            }

            var ticket = await db.KitchenTickets.SingleOrDefaultAsync(
                value => value.OrderId == order.Id && value.TicketNumber == ticketNumber,
                cancellationToken);

            var isNew = ticket is null;
            if (ticket is null)
            {
                ticket = new LocalKitchenTicket
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    OrderId = order.Id,
                    KitchenStationId = station.Id,
                    SubmittedByUserId = Long(ticketPayload, "submitted_by_user_id") is var submitted && submitted > 0
                        ? submitted
                        : order.WaiterId,
                    TicketNumber = ticketNumber,
                    Status = String(ticketPayload, "status") ?? "queued",
                    QueuedAt = Date(ticketPayload, "queued_at") ?? DateTimeOffset.UtcNow,
                    StartedAt = Date(ticketPayload, "started_at"),
                    ReadyAt = Date(ticketPayload, "ready_at"),
                    CompletedAt = Date(ticketPayload, "completed_at"),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                db.KitchenTickets.Add(ticket);
            }
            else
            {
                ticket.KitchenStationId = station.Id;
                ticket.Status = String(ticketPayload, "status") ?? ticket.Status;
                ticket.StartedAt = Date(ticketPayload, "started_at");
                ticket.ReadyAt = Date(ticketPayload, "ready_at");
                ticket.CompletedAt = Date(ticketPayload, "completed_at");
                ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(cloudTicketId))
            {
                UpsertLink(db, "kitchen_ticket", ticket.Id, cloudTicketId);
            }

            await db.SaveChangesAsync(cancellationToken);
            await SynchronizeCloudKitchenTicketItemsAsync(
                db,
                ticket,
                ticketPayload,
                cancellationToken);

            if (isNew)
            {
                await QueueImportedKotPrintAsync(db, order, ticket, station, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SynchronizeCloudKitchenTicketItemsAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        JsonElement ticketPayload,
        CancellationToken cancellationToken)
    {
        if (!ticketPayload.TryGetProperty("items", out var ticketItems) ||
            ticketItems.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var itemPayload in ticketItems.EnumerateArray())
        {
            var cloudOrderItemId = String(itemPayload, "order_item_id");
            if (string.IsNullOrWhiteSpace(cloudOrderItemId))
            {
                continue;
            }

            var orderItemId = await db.CloudEntityLinks
                .Where(value =>
                    value.EntityType == "order_item" &&
                    value.CloudEntityId == cloudOrderItemId)
                .Select(value => value.LocalEntityId)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(orderItemId))
            {
                continue;
            }

            var existing = await db.KitchenTicketItems.SingleOrDefaultAsync(
                value => value.KitchenTicketId == ticket.Id && value.OrderItemId == orderItemId,
                cancellationToken);

            if (existing is null)
            {
                db.KitchenTicketItems.Add(new LocalKitchenTicketItem
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    KitchenTicketId = ticket.Id,
                    OrderItemId = orderItemId,
                    ItemName = String(itemPayload, "item_name") ?? "Menu item",
                    Quantity = Int(itemPayload, "quantity", 1),
                    Notes = String(itemPayload, "notes"),
                    Status = String(itemPayload, "status") ?? ticket.Status,
                });
            }
            else
            {
                existing.ItemName = String(itemPayload, "item_name") ?? existing.ItemName;
                existing.Quantity = Int(itemPayload, "quantity", existing.Quantity);
                existing.Notes = String(itemPayload, "notes");
                existing.Status = String(itemPayload, "status") ?? existing.Status;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task QueueImportedKotPrintAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalKitchenTicket ticket,
        LocalKitchenStation station,
        CancellationToken cancellationToken)
    {
        if (ticket.Status is "cancelled" or "completed" ||
            await db.PrintJobs.AnyAsync(value => value.KitchenTicketId == ticket.Id, cancellationToken))
        {
            return;
        }

        var binding = await db.KitchenPrinterBindings.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.KitchenStationId == station.Id && value.IsEnabled,
                cancellationToken);

        if (binding is null)
        {
            return;
        }

        var table = await db.DiningTables.AsNoTracking()
            .SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var items = await db.KitchenTicketItems.AsNoTracking()
            .Where(value => value.KitchenTicketId == ticket.Id)
            .ToArrayAsync(cancellationToken);

        var text = new StringBuilder();
        text.AppendLine("BUSINESSOS RESTAURANT");
        text.AppendLine($"KOT: {ticket.TicketNumber}");
        text.AppendLine($"STATION: {station.Name}");
        text.AppendLine($"TABLE: {table.Name} ({table.Code})");
        text.AppendLine($"WAITER: {order.WaiterName}");
        text.AppendLine($"TIME: {ticket.QueuedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine("--------------------------------");
        foreach (var item in items)
        {
            text.AppendLine($"{item.Quantity} x {item.ItemName}");
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                text.AppendLine($"  NOTE: {item.Notes}");
            }
        }
        text.AppendLine("--------------------------------");
        text.AppendLine("SOURCE: CLOUD FALLBACK");

        db.PrintJobs.Add(new LocalPrintJob
        {
            Id = Guid.CreateVersion7().ToString("N"),
            KitchenTicketId = ticket.Id,
            PrinterName = binding.PrinterName,
            DocumentName = ticket.TicketNumber,
            PayloadText = text.ToString(),
            Copies = Math.Clamp(binding.Copies, 1, 5),
            Status = "pending",
            Attempts = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private async Task ApplyBillAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        if (localId is null)
        {
            return;
        }

        var bill = await db.Bills.SingleOrDefaultAsync(value => value.Id == localId, cancellationToken);
        if (bill is null)
        {
            return;
        }

        bill.Status = String(payload, "status") ?? bill.Status;
        bill.DiscountType = String(payload, "discount_type");
        bill.DiscountValue = NullableDecimal(payload, "discount_value");
        bill.DiscountAmount = Decimal(payload, "discount_amount", bill.DiscountAmount);
        bill.Total = Decimal(payload, "total", bill.Total);
        bill.PaidAmount = Decimal(payload, "paid_amount", bill.PaidAmount);
        bill.BalanceDue = Decimal(payload, "balance_due", bill.BalanceDue);
        bill.PaidAt = Date(payload, "paid_at");
        UpsertLink(db, "bill", bill.Id, cloudId);
    }

    private async Task ApplyCashierSessionAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        if (localId is null)
        {
            return;
        }

        var session = await db.CashierSessions.SingleOrDefaultAsync(
            value => value.Id == localId,
            cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Status = String(payload, "status") ?? session.Status;
        session.ExpectedCash = NullableDecimal(payload, "expected_cash");
        session.DeclaredCash = NullableDecimal(payload, "declared_cash");
        session.CashVariance = NullableDecimal(payload, "cash_variance");
        session.ClosedAt = Date(payload, "closed_at");
        UpsertLink(db, "cashier_session", session.Id, cloudId);
    }

    private async Task ApplyInventoryItemAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        LocalInventoryItem? item = null;

        if (localId is not null)
        {
            item = await db.InventoryItems.SingleOrDefaultAsync(value => value.Id == localId, cancellationToken);
        }

        var sku = String(payload, "sku");
        if (item is null && !string.IsNullOrWhiteSpace(sku))
        {
            item = await db.InventoryItems.SingleOrDefaultAsync(value => value.Sku == sku, cancellationToken);
        }

        if (item is null)
        {
            if (string.IsNullOrWhiteSpace(sku))
            {
                return;
            }

            item = new LocalInventoryItem
            {
                Id = Guid.CreateVersion7().ToString("N"),
                Sku = sku,
                Name = String(payload, "name") ?? sku,
                BaseUnit = String(payload, "base_unit") ?? "unit",
                PurchaseUnit = String(payload, "purchase_unit"),
                PurchaseToBaseFactor = Decimal(payload, "purchase_to_base_factor", 1m),
                ReorderLevel = Decimal(payload, "reorder_level"),
                IsActive = Bool(payload, "is_active", true),
            };
            db.InventoryItems.Add(item);
        }
        else
        {
            item.Name = String(payload, "name") ?? item.Name;
            item.BaseUnit = String(payload, "base_unit") ?? item.BaseUnit;
            item.PurchaseUnit = String(payload, "purchase_unit");
            item.PurchaseToBaseFactor = Decimal(payload, "purchase_to_base_factor", item.PurchaseToBaseFactor);
            item.ReorderLevel = Decimal(payload, "reorder_level", item.ReorderLevel);
            item.IsActive = Bool(payload, "is_active", item.IsActive);
        }

        UpsertLink(db, "inventory_item", item.Id, cloudId);
    }

    private async Task ApplyInventoryBalanceAsync(
        RestaurantDbContext db,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var cloudItemId = String(payload, "inventory_item_id");
        var branchId = String(payload, "branch_id");

        if (string.IsNullOrWhiteSpace(cloudItemId) || string.IsNullOrWhiteSpace(branchId))
        {
            return;
        }

        var link = await db.CloudEntityLinks.SingleOrDefaultAsync(
            value => value.EntityType == "inventory_item" && value.CloudEntityId == cloudItemId,
            cancellationToken);

        if (link is null)
        {
            return;
        }

        var balance = await db.InventoryBalances.SingleOrDefaultAsync(
            value => value.BranchId == branchId && value.InventoryItemId == link.LocalEntityId,
            cancellationToken);

        if (balance is null)
        {
            balance = new LocalInventoryBalance
            {
                Id = Guid.CreateVersion7().ToString("N"),
                BranchId = branchId,
                InventoryItemId = link.LocalEntityId,
                Quantity = Decimal(payload, "quantity"),
            };
            db.InventoryBalances.Add(balance);
        }
        else
        {
            balance.Quantity = Decimal(payload, "quantity", balance.Quantity);
        }
    }

    private async Task ApplySupplierAsync(
        RestaurantDbContext db,
        string cloudId,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        LocalSupplier? supplier = null;

        if (localId is not null)
        {
            supplier = await db.Suppliers.SingleOrDefaultAsync(value => value.Id == localId, cancellationToken);
        }

        var code = String(payload, "code");
        if (supplier is null && !string.IsNullOrWhiteSpace(code))
        {
            supplier = await db.Suppliers.SingleOrDefaultAsync(value => value.Code == code, cancellationToken);
        }

        if (supplier is null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            supplier = new LocalSupplier
            {
                Id = Guid.CreateVersion7().ToString("N"),
                Code = code,
                Name = String(payload, "name") ?? code,
                Phone = String(payload, "phone"),
                Email = String(payload, "email"),
                Address = String(payload, "address"),
                IsActive = Bool(payload, "is_active", true),
            };
            db.Suppliers.Add(supplier);
        }
        else
        {
            supplier.Name = String(payload, "name") ?? supplier.Name;
            supplier.Phone = String(payload, "phone");
            supplier.Email = String(payload, "email");
            supplier.Address = String(payload, "address");
            supplier.IsActive = Bool(payload, "is_active", supplier.IsActive);
        }

        UpsertLink(db, "supplier", supplier.Id, cloudId);
    }

    private async Task ApplyPurchaseOrderAsync(
        RestaurantDbContext db,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        if (localId is null)
        {
            return;
        }

        var po = await db.PurchaseOrders.SingleOrDefaultAsync(value => value.Id == localId, cancellationToken);
        if (po is null)
        {
            return;
        }

        po.Status = String(payload, "status") ?? po.Status;
        po.EstimatedTotal = Decimal(payload, "estimated_total", po.EstimatedTotal);
        po.CompletedAt = Date(payload, "completed_at");
    }

    private async Task ApplyDailyClosingAsync(
        RestaurantDbContext db,
        JsonElement payload,
        string? localId,
        CancellationToken cancellationToken)
    {
        if (localId is null)
        {
            return;
        }

        var closing = await db.DailyClosings.SingleOrDefaultAsync(
            value => value.Id == localId,
            cancellationToken);
        if (closing is null)
        {
            return;
        }

        closing.Status = String(payload, "status") ?? closing.Status;
        closing.FinalizedAt = Date(payload, "finalized_at");
        closing.ReopenedAt = Date(payload, "reopened_at");
    }

    private static async Task ApplyRestaurantSettingsAsync(
        RestaurantDbContext db,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kitchen_queue_enabled"] = Bool(payload, "kitchen_queue_enabled", true) ? "true" : "false",
            ["preparing_stage_enabled"] = Bool(payload, "preparing_stage_enabled", true) ? "true" : "false",
            ["expo_enabled"] = Bool(payload, "expo_enabled", false) ? "true" : "false",
            ["courses_enabled"] = Bool(payload, "courses_enabled", false) ? "true" : "false",
            ["kot_sound_enabled"] = Bool(payload, "kot_sound_enabled", true) ? "true" : "false",
            ["kitchen_warning_minutes"] = Math.Clamp(Int(payload, "kitchen_warning_minutes", 10), 1, 240).ToString(),
            ["kitchen_late_minutes"] = Math.Clamp(Int(payload, "kitchen_late_minutes", 20), 1, 480).ToString(),
            ["require_manager_approval_post_kot_void"] =
                Bool(
                    payload,
                    "require_manager_approval_post_kot_void",
                    Bool(payload, "require_manager_approval_for_post_kot_void", false))
                    ? "true"
                    : "false",
            ["negative_stock_policy"] = NormalizeNegativeStockPolicy(
                String(payload, "negative_stock_policy") ?? "block"),
        };

        var warning = int.Parse(defaults["kitchen_warning_minutes"]);
        var late = int.Parse(defaults["kitchen_late_minutes"]);
        if (late < warning)
        {
            defaults["kitchen_late_minutes"] = warning.ToString();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var pair in defaults)
        {
            var row = await db.RestaurantSettings
                .SingleOrDefaultAsync(value => value.Key == pair.Key, cancellationToken);

            if (row is null)
            {
                db.RestaurantSettings.Add(new LocalRestaurantSetting
                {
                    Key = pair.Key,
                    Value = pair.Value,
                    Source = "cloud",
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                row.Value = pair.Value;
                row.Source = "cloud";
                row.UpdatedAtUtc = now;
            }
        }

        AddLanChange(db, "restaurant_settings", "workflow", "upsert", new
        {
            kitchen_queue_enabled = defaults["kitchen_queue_enabled"] == "true",
            preparing_stage_enabled = defaults["preparing_stage_enabled"] == "true",
            expo_enabled = defaults["expo_enabled"] == "true",
            courses_enabled = defaults["courses_enabled"] == "true",
            kot_sound_enabled = defaults["kot_sound_enabled"] == "true",
            kitchen_warning_minutes = int.Parse(defaults["kitchen_warning_minutes"]),
            kitchen_late_minutes = int.Parse(defaults["kitchen_late_minutes"]),
            require_manager_approval_post_kot_void =
                defaults["require_manager_approval_post_kot_void"] == "true",
            negative_stock_policy = defaults["negative_stock_policy"],
        });
    }

    private async Task<string?> ResolveLocalIdAsync(
        RestaurantDbContext db,
        CloudPullChange change,
        CancellationToken cancellationToken)
    {
        var fromServer = change.LocalLinks.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromServer))
        {
            UpsertLink(db, change.EntityType, fromServer, change.EntityId);
            return fromServer;
        }

        return await db.CloudEntityLinks
            .Where(value =>
                value.EntityType == change.EntityType &&
                value.CloudEntityId == change.EntityId)
            .Select(value => value.LocalEntityId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<LocalCloudSyncState> StateAsync(
        RestaurantDbContext db,
        CancellationToken cancellationToken)
    {
        var state = await db.CloudSyncStates.SingleOrDefaultAsync(value => value.Id == 1, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new LocalCloudSyncState
        {
            Id = 1,
            PullCursor = 0,
        };
        db.CloudSyncStates.Add(state);
        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static void UpsertLink(
        RestaurantDbContext db,
        string entityType,
        string localEntityId,
        string cloudEntityId)
    {
        var link = db.CloudEntityLinks.Local.FirstOrDefault(
            value => value.EntityType == entityType && value.LocalEntityId == localEntityId);

        if (link is null)
        {
            link = db.CloudEntityLinks.SingleOrDefault(
                value => value.EntityType == entityType && value.LocalEntityId == localEntityId);
        }

        if (link is null)
        {
            db.CloudEntityLinks.Add(new LocalCloudEntityLink
            {
                EntityType = entityType,
                LocalEntityId = localEntityId,
                CloudEntityId = cloudEntityId,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        link.CloudEntityId = cloudEntityId;
        link.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void AddConflict(
        RestaurantDbContext db,
        LocalCloudOutboxMutation mutation,
        string code,
        string message,
        string? cloudPayload)
    {
        if (db.CloudConflicts.Local.Any(value => value.MutationId == mutation.Id) ||
            db.CloudConflicts.Any(value => value.MutationId == mutation.Id))
        {
            return;
        }

        db.CloudConflicts.Add(new LocalCloudConflict
        {
            Id = Guid.CreateVersion7().ToString("N"),
            MutationId = mutation.Id,
            Operation = mutation.Operation,
            EntityType = mutation.EntityType,
            LocalEntityId = mutation.LocalEntityId,
            Code = code,
            Message = message,
            LocalPayloadJson = mutation.PayloadJson,
            CloudPayloadJson = cloudPayload,
            Status = "open",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static string NormalizeNegativeStockPolicy(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "block" or "warn" or "allow"
            ? normalized
            : "block";
    }

    private static string? String(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static decimal Decimal(JsonElement element, string name, decimal fallback = 0m)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(property.ToString(), out var parsed) ? parsed : fallback;
    }

    private static decimal? NullableDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return Decimal(element, name);
    }

    private static long Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        if (property.TryGetInt64(out var value))
        {
            return value;
        }

        return long.TryParse(property.ToString(), out value) ? value : 0;
    }

    private static int Int(JsonElement element, string name, int fallback)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return fallback;
        }

        if (property.TryGetInt32(out var value))
        {
            return value;
        }

        return int.TryParse(property.ToString(), out value) ? value : fallback;
    }

    private static bool Bool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(property.ToString(), out var parsed) ? parsed : fallback,
        };
    }

    private static DateTimeOffset? Date(JsonElement element, string name)
    {
        var value = String(element, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}

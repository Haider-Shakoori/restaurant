using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalRecipeComponentRequest(string InventoryItemId, decimal QuantityBase);
public sealed record LocalPurchaseOrderLineRequest(string InventoryItemId, decimal PurchaseQuantity, decimal UnitCost);
public sealed record LocalReceivePurchaseOrderLineRequest(string PurchaseOrderLineId, decimal PurchaseQuantity);

public sealed class LocalInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;

    public LocalInventoryService(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<object> CreateItemAsync(
        string sku,
        string name,
        string baseUnit,
        string? purchaseUnit,
        decimal purchaseToBaseFactor,
        decimal reorderLevel,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        if (string.IsNullOrWhiteSpace(sku) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(baseUnit))
        {
            throw new LocalSyncConflictException("invalid_payload", "SKU, name and base unit are required.");
        }

        if (purchaseToBaseFactor <= 0 || reorderLevel < 0)
        {
            throw new LocalSyncConflictException(
                "invalid_payload",
                "Purchase conversion factor must be positive and reorder level cannot be negative.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var normalizedSku = sku.Trim().ToUpperInvariant();

        if (await db.InventoryItems.AnyAsync(value => value.Sku == normalizedSku, cancellationToken))
        {
            throw new LocalSyncConflictException("inventory_item_conflict", "An inventory item already uses this SKU.");
        }

        var item = new LocalInventoryItem
        {
            Id = Guid.CreateVersion7().ToString("N"),
            Sku = normalizedSku,
            Name = name.Trim(),
            BaseUnit = baseUnit.Trim().ToLowerInvariant(),
            PurchaseUnit = string.IsNullOrWhiteSpace(purchaseUnit)
                ? null
                : purchaseUnit.Trim().ToLowerInvariant(),
            PurchaseToBaseFactor = Factor(purchaseToBaseFactor),
            ReorderLevel = Quantity(reorderLevel),
            IsActive = true,
        };

        db.InventoryItems.Add(item);
        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "inventory.item.create",
            "inventory_item",
            item.Id,
            new
            {
                sku = item.Sku,
                name = item.Name,
                base_unit = item.BaseUnit,
                purchase_unit = item.PurchaseUnit,
                purchase_to_base_factor = item.PurchaseToBaseFactor,
                reorder_level = item.ReorderLevel,
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "inventory.item_created",
            null,
            "inventory_item",
            item.Id,
            new
            {
                item_id = item.Id,
                sku = item.Sku,
                item.Name,
                item.BaseUnit,
                purchase_unit = item.PurchaseUnit,
                purchase_to_base_factor = item.PurchaseToBaseFactor,
                reorder_level = item.ReorderLevel,
            });

        await db.SaveChangesAsync(cancellationToken);
        return ItemSnapshot(item, null, null);
    }

    public async Task<object[]> ItemsAsync(
        string? branchId,
        bool lowStockOnly,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var items = await db.InventoryItems
            .Where(value => value.IsActive)
            .OrderBy(value => value.Name)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        Dictionary<string, LocalInventoryBalance> balances = [];
        Dictionary<string, decimal> reserved = [];

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            balances = await db.InventoryBalances
                .Where(value => value.BranchId == branchId)
                .AsNoTracking()
                .ToDictionaryAsync(value => value.InventoryItemId, StringComparer.Ordinal, cancellationToken);

            var reservationRows = await (
                from line in db.InventoryReservationLines.AsNoTracking()
                join reservation in db.InventoryReservations.AsNoTracking()
                    on line.InventoryReservationId equals reservation.Id
                where reservation.BranchId == branchId && reservation.Status == "reserved"
                select new { line.InventoryItemId, line.QuantityBase })
                .ToArrayAsync(cancellationToken);

            reserved = reservationRows
                .GroupBy(value => value.InventoryItemId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => Quantity(group.Sum(value => value.QuantityBase)),
                    StringComparer.Ordinal);
        }

        return items
            .Select(item =>
            {
                balances.TryGetValue(item.Id, out var balance);
                reserved.TryGetValue(item.Id, out var reservedQuantity);
                var quantity = balance?.Quantity ?? 0m;
                var available = Quantity(quantity - reservedQuantity);
                return new
                {
                    item,
                    quantity,
                    reserved = reservedQuantity,
                    available,
                    low = !string.IsNullOrWhiteSpace(branchId) &&
                          available <= Quantity(item.ReorderLevel),
                };
            })
            .Where(value => !lowStockOnly || value.low)
            .Select(value => ItemSnapshot(
                value.item,
                string.IsNullOrWhiteSpace(branchId) ? null : value.quantity,
                string.IsNullOrWhiteSpace(branchId) ? null : value.reserved))
            .ToArray();
    }

    public async Task<object> CreateSupplierAsync(
        string code,
        string name,
        string? phone,
        string? email,
        string? address,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new LocalSyncConflictException("invalid_payload", "Supplier code and name are required.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (await db.Suppliers.AnyAsync(value => value.Code == normalizedCode, cancellationToken))
        {
            throw new LocalSyncConflictException("supplier_conflict", "A supplier already uses this code.");
        }

        var supplier = new LocalSupplier
        {
            Id = Guid.CreateVersion7().ToString("N"),
            Code = normalizedCode,
            Name = name.Trim(),
            Phone = EmptyToNull(phone),
            Email = EmptyToNull(email),
            Address = EmptyToNull(address),
            IsActive = true,
        };

        db.Suppliers.Add(supplier);
        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "supplier.create",
            "supplier",
            supplier.Id,
            new
            {
                code = supplier.Code,
                name = supplier.Name,
                phone = supplier.Phone,
                email = supplier.Email,
                address = supplier.Address,
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "purchasing",
            "supplier.created",
            null,
            "supplier",
            supplier.Id,
            new { supplier_id = supplier.Id, supplier.Code, supplier.Name });

        await db.SaveChangesAsync(cancellationToken);
        return SupplierSnapshot(supplier);
    }

    public async Task<object[]> SuppliersAsync(
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        return (await db.Suppliers
                .Where(value => value.IsActive)
                .OrderBy(value => value.Name)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken))
            .Select(SupplierSnapshot)
            .ToArray();
    }

    public async Task<object> CreateRecipeVersionAsync(
        string branchId,
        string menuItemId,
        string? name,
        IReadOnlyList<LocalRecipeComponentRequest> components,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        if (components.Count == 0)
        {
            throw new LocalSyncConflictException("invalid_payload", "A recipe requires at least one inventory component.");
        }

        if (components.GroupBy(value => value.InventoryItemId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new LocalSyncConflictException("invalid_payload", "The same inventory item cannot appear twice in one recipe.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!await db.Branches.AnyAsync(value => value.Id == branchId && value.IsActive, cancellationToken))
        {
            throw new LocalSyncConflictException("dependency_missing", "The selected branch is inactive or unavailable.");
        }

        var menuItem = await db.MenuItems.SingleOrDefaultAsync(
            value => value.Id == menuItemId && value.IsAvailable,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "The selected menu item is unavailable.");

        var inventoryIds = components.Select(value => value.InventoryItemId).ToArray();
        var inventoryItems = await db.InventoryItems
            .Where(value => inventoryIds.Contains(value.Id) && value.IsActive)
            .ToArrayAsync(cancellationToken);

        if (inventoryItems.Length != inventoryIds.Length)
        {
            throw new LocalSyncConflictException("dependency_missing", "One or more recipe inventory items are missing or inactive.");
        }

        if (components.Any(value => Quantity(value.QuantityBase) <= 0))
        {
            throw new LocalSyncConflictException("invalid_payload", "Recipe quantities must be greater than zero.");
        }

        var existing = await db.Recipes
            .Where(value => value.BranchId == branchId && value.MenuItemId == menuItemId)
            .ToArrayAsync(cancellationToken);

        foreach (var recipe in existing.Where(value => value.IsActive))
        {
            recipe.IsActive = false;
        }

        var version = existing.Length == 0 ? 1 : existing.Max(value => value.Version) + 1;
        var recipeEntity = new LocalRecipe
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branchId,
            MenuItemId = menuItemId,
            Name = string.IsNullOrWhiteSpace(name) ? $"{menuItem.Name} Recipe" : name.Trim(),
            Version = version,
            IsActive = true,
        };

        db.Recipes.Add(recipeEntity);

        foreach (var component in components)
        {
            db.RecipeItems.Add(new LocalRecipeItem
            {
                Id = Guid.CreateVersion7().ToString("N"),
                RecipeId = recipeEntity.Id,
                InventoryItemId = component.InventoryItemId,
                QuantityBase = Quantity(component.QuantityBase),
            });
        }

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "recipe.version.create",
            "recipe",
            recipeEntity.Id,
            new
            {
                branch_id = branchId,
                menu_item_id = menuItemId,
                name = recipeEntity.Name,
                version = recipeEntity.Version,
                items = components.Select(value => new
                {
                    local_inventory_item_id = value.InventoryItemId,
                    quantity_base = Quantity(value.QuantityBase),
                }).ToArray(),
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "recipe.version_created",
            branchId,
            "recipe",
            recipeEntity.Id,
            new
            {
                recipe_id = recipeEntity.Id,
                menu_item_id = menuItemId,
                recipeEntity.Version,
                component_count = components.Count,
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await RecipeSnapshotAsync(db, recipeEntity, cancellationToken);
    }

    public async Task<object[]> RecipesAsync(
        string? branchId,
        string? menuItemId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.Recipes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(value => value.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(menuItemId))
        {
            query = query.Where(value => value.MenuItemId == menuItemId);
        }

        var recipes = await query.ToArrayAsync(cancellationToken);
        var result = new List<object>();

        foreach (var recipe in recipes
                     .OrderByDescending(value => value.IsActive)
                     .ThenByDescending(value => value.Version)
                     .Take(200))
        {
            result.Add(await RecipeSnapshotAsync(db, recipe, cancellationToken));
        }

        return result.ToArray();
    }

    public async Task<object> AdjustAsync(
        string branchId,
        string inventoryItemId,
        decimal quantityDelta,
        string clientAdjustmentId,
        string reason,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        var delta = Quantity(quantityDelta);
        if (delta == 0m)
        {
            throw new LocalSyncConflictException("invalid_payload", "Stock movement quantity cannot be zero.");
        }

        if (string.IsNullOrWhiteSpace(clientAdjustmentId) || string.IsNullOrWhiteSpace(reason))
        {
            throw new LocalSyncConflictException("invalid_payload", "Adjustment ID and reason are required.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var item = await RequireInventoryItemAsync(db, inventoryItemId, cancellationToken);
        await RequireBranchAsync(db, branchId, cancellationToken);

        var key = $"adjustment:{branchId}:{clientAdjustmentId.Trim()}";
        var existing = await db.StockMovements.SingleOrDefaultAsync(value => value.IdempotencyKey == key, cancellationToken);

        if (existing is not null)
        {
            return await MovementSnapshotAsync(db, existing, cancellationToken);
        }

        var movement = await RecordMovementAsync(
            db,
            branchId,
            item,
            actor,
            "adjustment",
            delta,
            "manual_adjustment",
            clientAdjustmentId.Trim(),
            key,
            null,
            null,
            reason.Trim(),
            cancellationToken);

        var valuation = await GetOrCreateValuationAsync(db, branchId, item.Id, cancellationToken);
        var valueDelta = Money(valuation.AverageUnitCost * delta);
        valuation.Quantity = Quantity(valuation.Quantity + delta);
        valuation.Value = Money(valuation.Value + valueDelta);
        valuation.AverageUnitCost = valuation.Quantity == 0m
            ? valuation.AverageUnitCost
            : Factor(valuation.Value / valuation.Quantity);

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "inventory.adjust",
            "stock_movement",
            movement.Id,
            new
            {
                branch_id = branchId,
                local_inventory_item_id = item.Id,
                quantity_delta = delta,
                client_adjustment_id = clientAdjustmentId.Trim(),
                reason = reason.Trim(),
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "inventory.adjusted",
            branchId,
            "stock_movement",
            movement.Id,
            new
            {
                movement_id = movement.Id,
                inventory_item_id = item.Id,
                quantity_delta = delta,
                value_delta = valueDelta,
                reason = reason.Trim(),
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MovementSnapshotAsync(db, movement, cancellationToken);
    }

    public async Task<object[]> MovementsAsync(
        string? branchId,
        string? inventoryItemId,
        int limit,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.StockMovements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(value => value.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(inventoryItemId))
        {
            query = query.Where(value => value.InventoryItemId == inventoryItemId);
        }

        var movements = await query.ToArrayAsync(cancellationToken);
        var result = new List<object>();

        foreach (var movement in movements
                     .OrderByDescending(value => value.OccurredAt)
                     .Take(Math.Clamp(limit, 1, 250)))
        {
            result.Add(await MovementSnapshotAsync(db, movement, cancellationToken));
        }

        return result.ToArray();
    }

    public async Task<LocalInventoryReservation?> ReserveKitchenItemAsync(
        RestaurantDbContext db,
        LocalKitchenTicketItem kitchenItem,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        var existing = db.InventoryReservations.Local.FirstOrDefault(
            value => value.KitchenTicketItemId == kitchenItem.Id)
            ?? await db.InventoryReservations.SingleOrDefaultAsync(
                value => value.KitchenTicketItemId == kitchenItem.Id,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var orderItem = await db.OrderItems.SingleAsync(
            value => value.Id == kitchenItem.OrderItemId,
            cancellationToken);
        var ticket = await db.KitchenTickets.SingleAsync(
            value => value.Id == kitchenItem.KitchenTicketId,
            cancellationToken);
        var order = await db.Orders.SingleAsync(
            value => value.Id == ticket.OrderId,
            cancellationToken);
        var branchId = order.BranchId;
        if (string.IsNullOrWhiteSpace(branchId))
        {
            if (string.IsNullOrWhiteSpace(order.DiningTableId))
            {
                throw new LocalSyncConflictException(
                    "dependency_missing",
                    "Order branch context is unavailable for inventory reservation.");
            }

            var table = await db.DiningTables.SingleAsync(
                value => value.Id == order.DiningTableId,
                cancellationToken);
            var area = await db.DiningAreas.SingleAsync(
                value => value.Id == table.DiningAreaId,
                cancellationToken);
            branchId = area.BranchId;
            order.BranchId = branchId;
        }

        var recipe = await db.Recipes
            .Where(value =>
                value.BranchId == branchId &&
                value.MenuItemId == orderItem.MenuItemId &&
                value.IsActive)
            .OrderByDescending(value => value.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (recipe is null)
        {
            return null;
        }

        var components = await db.RecipeItems
            .Where(value => value.RecipeId == recipe.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        if (components.Length == 0)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var reservation = new LocalInventoryReservation
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            KitchenTicketItemId = kitchenItem.Id,
            BranchId = branchId,
            CreatedByUserId = actor.UserId,
            Status = "reserved",
            ReservedAt = now,
        };
        db.InventoryReservations.Add(reservation);

        foreach (var component in components)
        {
            db.InventoryReservationLines.Add(new LocalInventoryReservationLine
            {
                Id = Guid.CreateVersion7().ToString("N"),
                InventoryReservationId = reservation.Id,
                RecipeId = recipe.Id,
                InventoryItemId = component.InventoryItemId,
                QuantityBase = Quantity(component.QuantityBase * kitchenItem.Quantity),
            });
        }

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "inventory.production_reserved",
            branchId,
            "inventory_reservation",
            reservation.Id,
            new
            {
                order_id = order.Id,
                order_item_id = orderItem.Id,
                kitchen_ticket_item_id = kitchenItem.Id,
                quantity = kitchenItem.Quantity,
            });

        await db.SaveChangesAsync(cancellationToken);
        return reservation;
    }

    public async Task<LocalInventoryConsumption?> CommitKitchenItemAsync(
        RestaurantDbContext db,
        LocalKitchenTicketItem kitchenItem,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        var existingConsumption = db.InventoryConsumptions.Local.FirstOrDefault(
            value => value.ProductionKey == kitchenItem.Id)
            ?? await db.InventoryConsumptions.SingleOrDefaultAsync(
                value => value.ProductionKey == kitchenItem.Id,
                cancellationToken);

        if (existingConsumption is not null)
        {
            return existingConsumption;
        }

        var reservation = await ReserveKitchenItemAsync(db, kitchenItem, actor, cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        if (reservation.Status == "released")
        {
            throw new LocalSyncConflictException(
                "inventory_reservation_released",
                "This production reservation was already released.");
        }

        var lines = await db.InventoryReservationLines
            .Where(value => value.InventoryReservationId == reservation.Id)
            .ToArrayAsync(cancellationToken);
        if (lines.Length == 0)
        {
            return null;
        }

        var consumption = new LocalInventoryConsumption
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = reservation.OrderId,
            BranchId = reservation.BranchId,
            ProductionKey = kitchenItem.Id,
            OrderItemId = reservation.OrderItemId,
            KitchenTicketItemId = kitchenItem.Id,
            InventoryReservationId = reservation.Id,
            ConsumedByUserId = actor.UserId,
            ConsumedAt = DateTimeOffset.UtcNow,
        };
        db.InventoryConsumptions.Add(consumption);

        decimal totalCost = 0m;
        foreach (var line in lines)
        {
            var item = await RequireInventoryItemAsync(db, line.InventoryItemId, cancellationToken);
            var quantity = Quantity(line.QuantityBase);
            var valuation = await GetOrCreateValuationAsync(
                db,
                reservation.BranchId,
                item.Id,
                cancellationToken);
            var cost = Money(valuation.AverageUnitCost * quantity);
            totalCost = Money(totalCost + cost);

            valuation.Quantity = Quantity(valuation.Quantity - quantity);
            valuation.Value = Money(valuation.Value - cost);

            var movement = await RecordMovementAsync(
                db,
                reservation.BranchId,
                item,
                actor,
                "consumption",
                -quantity,
                "kitchen_production",
                kitchenItem.Id,
                $"production-consumption:{kitchenItem.Id}:{item.Id}",
                reservation.OrderItemId,
                null,
                "Recipe consumption committed for kitchen production.",
                cancellationToken);

            db.InventoryConsumptionLines.Add(new LocalInventoryConsumptionLine
            {
                Id = Guid.CreateVersion7().ToString("N"),
                InventoryConsumptionId = consumption.Id,
                OrderItemId = reservation.OrderItemId,
                RecipeId = line.RecipeId,
                InventoryItemId = item.Id,
                StockMovementId = movement.Id,
                QuantityBase = quantity,
            });
        }

        reservation.Status = "committed";
        reservation.CommittedAt = DateTimeOffset.UtcNow;

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "inventory.production_consumed",
            reservation.BranchId,
            "inventory_consumption",
            consumption.Id,
            new
            {
                consumption_id = consumption.Id,
                order_id = reservation.OrderId,
                order_item_id = reservation.OrderItemId,
                kitchen_ticket_item_id = kitchenItem.Id,
                inventory_reservation_id = reservation.Id,
                estimated_cost = totalCost,
            });

        await db.SaveChangesAsync(cancellationToken);
        return consumption;
    }

    public async Task ReleaseKitchenItemReservationAsync(
        RestaurantDbContext db,
        LocalKitchenTicketItem kitchenItem,
        LocalTerminalPrincipal actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var reservation = db.InventoryReservations.Local.FirstOrDefault(
            value => value.KitchenTicketItemId == kitchenItem.Id)
            ?? await db.InventoryReservations.SingleOrDefaultAsync(
                value => value.KitchenTicketItemId == kitchenItem.Id,
                cancellationToken);

        if (reservation is null || reservation.Status == "released")
        {
            return;
        }

        if (reservation.Status == "committed")
        {
            throw new LocalSyncConflictException(
                "inventory_already_consumed",
                "Committed production inventory cannot be returned by releasing a reservation.");
        }

        reservation.Status = "released";
        reservation.ReleasedAt = DateTimeOffset.UtcNow;
        reservation.ReleaseReason = EmptyToNull(reason);

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "inventory",
            "inventory.reservation_released",
            reservation.BranchId,
            "inventory_reservation",
            reservation.Id,
            new
            {
                order_id = reservation.OrderId,
                order_item_id = reservation.OrderItemId,
                kitchen_ticket_item_id = kitchenItem.Id,
                reason,
            });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ConsumeOrderAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        if (order.Status is not ("ready" or "served"))
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Inventory can only be finalized for a ready or served order.");
        }

        var ticketIds = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var productionItems = await db.KitchenTicketItems
            .Where(value =>
                ticketIds.Contains(value.KitchenTicketId) &&
                (value.Status == "ready" || value.Status == "completed"))
            .ToArrayAsync(cancellationToken);

        foreach (var kitchenItem in productionItems)
        {
            await CommitKitchenItemAsync(db, kitchenItem, actor, cancellationToken);
        }
    }

    public async Task<object> CreatePurchaseOrderAsync(
        string branchId,
        string supplierId,
        IReadOnlyList<LocalPurchaseOrderLineRequest> lines,
        string? notes,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        if (lines.Count == 0)
        {
            throw new LocalSyncConflictException("invalid_payload", "A purchase order requires at least one line.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await RequireBranchAsync(db, branchId, cancellationToken);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(
            value => value.Id == supplierId && value.IsActive,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "The selected supplier is inactive or unavailable.");

        var po = new LocalPurchaseOrder
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branchId,
            SupplierId = supplier.Id,
            OrderedByUserId = actor.UserId,
            PoNumber = $"PO-{Guid.CreateVersion7():N}".ToUpperInvariant(),
            Status = "ordered",
            EstimatedTotal = 0m,
            Notes = EmptyToNull(notes),
            OrderedAt = DateTimeOffset.UtcNow,
        };
        db.PurchaseOrders.Add(po);

        decimal total = 0m;

        foreach (var line in lines)
        {
            var item = await RequireInventoryItemAsync(db, line.InventoryItemId, cancellationToken);
            var purchaseQuantity = Quantity(line.PurchaseQuantity);
            var unitCost = Money(line.UnitCost);

            if (purchaseQuantity <= 0 || unitCost < 0)
            {
                throw new LocalSyncConflictException(
                    "invalid_payload",
                    "Purchase quantities must be positive and unit cost cannot be negative.");
            }

            var factor = Factor(item.PurchaseToBaseFactor);
            var baseQuantity = Quantity(purchaseQuantity * factor);
            var lineTotal = Money(unitCost * purchaseQuantity);
            total = Money(total + lineTotal);

            db.PurchaseOrderLines.Add(new LocalPurchaseOrderLine
            {
                Id = Guid.CreateVersion7().ToString("N"),
                PurchaseOrderId = po.Id,
                InventoryItemId = item.Id,
                ItemName = item.Name,
                PurchaseUnit = item.PurchaseUnit ?? item.BaseUnit,
                ConversionFactor = factor,
                OrderedPurchaseQuantity = purchaseQuantity,
                OrderedBaseQuantity = baseQuantity,
                ReceivedBaseQuantity = 0m,
                UnitCost = unitCost,
                LineTotal = lineTotal,
            });
        }

        po.EstimatedTotal = total;

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "purchase_order.create",
            "purchase_order",
            po.Id,
            new
            {
                branch_id = po.BranchId,
                local_supplier_id = po.SupplierId,
                notes = po.Notes,
                lines = db.PurchaseOrderLines.Local
                    .Where(value => value.PurchaseOrderId == po.Id)
                    .Select(value => new
                    {
                        local_line_id = value.Id,
                        local_inventory_item_id = value.InventoryItemId,
                        purchase_quantity = value.OrderedPurchaseQuantity,
                        unit_cost = value.UnitCost,
                    }).ToArray(),
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "purchasing",
            "purchase_order.created",
            branchId,
            "purchase_order",
            po.Id,
            new
            {
                purchase_order_id = po.Id,
                po.PoNumber,
                supplier_id = supplier.Id,
                line_count = lines.Count,
                estimated_total = po.EstimatedTotal,
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await PurchaseOrderSnapshotAsync(db, po, cancellationToken);
    }

    public async Task<object[]> PurchaseOrdersAsync(
        string? branchId,
        string? status,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.PurchaseOrders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(value => value.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(value => value.Status == status);
        }

        var orders = await query.ToArrayAsync(cancellationToken);
        var result = new List<object>();

        foreach (var po in orders.OrderByDescending(value => value.OrderedAt).Take(100))
        {
            result.Add(await PurchaseOrderSnapshotAsync(db, po, cancellationToken));
        }

        return result.ToArray();
    }

    public async Task<object> ReceivePurchaseOrderAsync(
        string purchaseOrderId,
        IReadOnlyList<LocalReceivePurchaseOrderLineRequest> lines,
        string? clientReceiptId,
        string? notes,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureInventoryRole(actor);

        if (lines.Count == 0)
        {
            throw new LocalSyncConflictException("invalid_payload", "A goods receipt requires at least one line.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(clientReceiptId))
        {
            var replay = await db.GoodsReceipts.SingleOrDefaultAsync(
                value => value.ClientReceiptId == clientReceiptId,
                cancellationToken);

            if (replay is not null)
            {
                if (replay.PurchaseOrderId != purchaseOrderId)
                {
                    throw new LocalSyncConflictException("receipt_conflict", "This client receipt ID belongs to another purchase order.");
                }

                return await ReceiptSnapshotAsync(db, replay, cancellationToken);
            }
        }

        var po = await db.PurchaseOrders.SingleOrDefaultAsync(
            value => value.Id == purchaseOrderId,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Purchase order does not exist.");

        if (po.Status is not ("ordered" or "partially_received"))
        {
            throw new LocalSyncConflictException(
                "purchase_order_state_conflict",
                "Only ordered or partially received purchase orders can receive stock.");
        }

        var poLines = await db.PurchaseOrderLines
            .Where(value => value.PurchaseOrderId == po.Id)
            .ToArrayAsync(cancellationToken);

        var receipt = new LocalGoodsReceipt
        {
            Id = Guid.CreateVersion7().ToString("N"),
            PurchaseOrderId = po.Id,
            BranchId = po.BranchId,
            SupplierId = po.SupplierId,
            ReceivedByUserId = actor.UserId,
            ReceiptNumber = $"GRN-{Guid.CreateVersion7():N}".ToUpperInvariant(),
            ClientReceiptId = EmptyToNull(clientReceiptId),
            Status = "posted",
            ReceivedAt = DateTimeOffset.UtcNow,
            Notes = EmptyToNull(notes),
        };
        db.GoodsReceipts.Add(receipt);

        decimal receiptTotal = 0m;

        foreach (var requestLine in lines)
        {
            var poLine = poLines.SingleOrDefault(value => value.Id == requestLine.PurchaseOrderLineId)
                ?? throw new LocalSyncConflictException(
                    "invalid_payload",
                    "A receipt line does not belong to this purchase order.");

            var purchaseQuantity = Quantity(requestLine.PurchaseQuantity);
            if (purchaseQuantity <= 0)
            {
                throw new LocalSyncConflictException("invalid_payload", "Received quantities must be greater than zero.");
            }

            var baseQuantity = Quantity(purchaseQuantity * poLine.ConversionFactor);
            var newReceived = Quantity(poLine.ReceivedBaseQuantity + baseQuantity);

            if (newReceived > Quantity(poLine.OrderedBaseQuantity))
            {
                throw new LocalSyncConflictException(
                    "invalid_payload",
                    "Receipt quantity exceeds the remaining purchase order quantity.");
            }

            var lineTotal = Money(poLine.UnitCost * purchaseQuantity);
            receiptTotal = Money(receiptTotal + lineTotal);
            var receiptLine = new LocalGoodsReceiptLine
            {
                Id = Guid.CreateVersion7().ToString("N"),
                GoodsReceiptId = receipt.Id,
                PurchaseOrderLineId = poLine.Id,
                InventoryItemId = poLine.InventoryItemId,
                ReceivedPurchaseQuantity = purchaseQuantity,
                ReceivedBaseQuantity = baseQuantity,
                UnitCost = poLine.UnitCost,
                LineTotal = lineTotal,
            };
            db.GoodsReceiptLines.Add(receiptLine);

            var item = await RequireInventoryItemAsync(db, poLine.InventoryItemId, cancellationToken);
            await RecordMovementAsync(
                db,
                po.BranchId,
                item,
                actor,
                "receipt",
                baseQuantity,
                "goods_receipt",
                receipt.Id,
                $"goods-receipt:{receipt.Id}:{poLine.Id}",
                receiptLine.Id,
                poLine.UnitCost,
                $"Purchase order receipt {po.PoNumber}",
                cancellationToken);

            var valuation = await GetOrCreateValuationAsync(db, po.BranchId, item.Id, cancellationToken);
            valuation.Quantity = Quantity(valuation.Quantity + baseQuantity);
            valuation.Value = Money(valuation.Value + lineTotal);
            valuation.AverageUnitCost = valuation.Quantity == 0m
                ? valuation.AverageUnitCost
                : Factor(valuation.Value / valuation.Quantity);

            poLine.ReceivedBaseQuantity = newReceived;
        }

        var fullyReceived = poLines.All(value =>
            Quantity(value.ReceivedBaseQuantity) >= Quantity(value.OrderedBaseQuantity));

        po.Status = fullyReceived ? "received" : "partially_received";
        po.CompletedAt = fullyReceived ? DateTimeOffset.UtcNow : null;

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "goods_receipt.post",
            "goods_receipt",
            receipt.Id,
            new
            {
                local_purchase_order_id = po.Id,
                client_receipt_id = receipt.ClientReceiptId ?? receipt.Id,
                notes = receipt.Notes,
                lines = db.GoodsReceiptLines.Local
                    .Where(value => value.GoodsReceiptId == receipt.Id)
                    .Select(value => new
                    {
                        local_purchase_order_line_id = value.PurchaseOrderLineId,
                        purchase_quantity = value.ReceivedPurchaseQuantity,
                    }).ToArray(),
            });
        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "purchasing",
            "goods_receipt.posted",
            po.BranchId,
            "goods_receipt",
            receipt.Id,
            new
            {
                goods_receipt_id = receipt.Id,
                purchase_order_id = po.Id,
                receipt.ReceiptNumber,
                total = receiptTotal,
                purchase_order_status = po.Status,
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReceiptSnapshotAsync(db, receipt, cancellationToken);
    }

    private static async Task<LocalStockMovement> RecordMovementAsync(
        RestaurantDbContext db,
        string branchId,
        LocalInventoryItem item,
        LocalTerminalPrincipal actor,
        string movementType,
        decimal quantityDelta,
        string sourceType,
        string sourceId,
        string idempotencyKey,
        string? sourceLineId,
        decimal? unitCost,
        string? notes,
        CancellationToken cancellationToken)
    {
        var existing = db.StockMovements.Local.FirstOrDefault(
            value => value.IdempotencyKey == idempotencyKey)
            ?? await db.StockMovements.SingleOrDefaultAsync(
                value => value.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var delta = Quantity(quantityDelta);
        if (delta == 0m)
        {
            throw new LocalSyncConflictException("invalid_payload", "Stock movement quantity cannot be zero.");
        }

        var balance = db.InventoryBalances.Local.FirstOrDefault(
            value => value.BranchId == branchId && value.InventoryItemId == item.Id)
            ?? await db.InventoryBalances.SingleOrDefaultAsync(
                value => value.BranchId == branchId && value.InventoryItemId == item.Id,
                cancellationToken);

        if (balance is null)
        {
            balance = new LocalInventoryBalance
            {
                Id = Guid.CreateVersion7().ToString("N"),
                BranchId = branchId,
                InventoryItemId = item.Id,
                Quantity = 0m,
            };
            db.InventoryBalances.Add(balance);
        }

        var movement = new LocalStockMovement
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branchId,
            InventoryItemId = item.Id,
            ActorUserId = actor.UserId,
            MovementType = movementType,
            QuantityDelta = delta,
            UnitCost = unitCost.HasValue ? Money(unitCost.Value) : null,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceLineId = sourceLineId,
            IdempotencyKey = idempotencyKey,
            Notes = EmptyToNull(notes),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        db.StockMovements.Add(movement);
        balance.Quantity = Quantity(balance.Quantity + delta);
        return movement;
    }

    private static async Task<LocalInventoryValuation> GetOrCreateValuationAsync(
        RestaurantDbContext db,
        string branchId,
        string inventoryItemId,
        CancellationToken cancellationToken)
    {
        var valuation = db.InventoryValuations.Local.FirstOrDefault(
            value => value.BranchId == branchId && value.InventoryItemId == inventoryItemId)
            ?? await db.InventoryValuations.SingleOrDefaultAsync(
                value => value.BranchId == branchId && value.InventoryItemId == inventoryItemId,
                cancellationToken);

        if (valuation is not null)
        {
            return valuation;
        }

        valuation = new LocalInventoryValuation
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branchId,
            InventoryItemId = inventoryItemId,
            Quantity = 0m,
            Value = 0m,
            AverageUnitCost = 0m,
        };
        db.InventoryValuations.Add(valuation);
        return valuation;
    }

    private static async Task<LocalInventoryItem> RequireInventoryItemAsync(
        RestaurantDbContext db,
        string id,
        CancellationToken cancellationToken) =>
        await db.InventoryItems.SingleOrDefaultAsync(
            value => value.Id == id && value.IsActive,
            cancellationToken)
        ?? throw new LocalSyncConflictException("dependency_missing", "Inventory item is missing or inactive.");

    private static async Task RequireBranchAsync(
        RestaurantDbContext db,
        string id,
        CancellationToken cancellationToken)
    {
        if (!await db.Branches.AnyAsync(value => value.Id == id && value.IsActive, cancellationToken))
        {
            throw new LocalSyncConflictException("dependency_missing", "Branch is missing or inactive.");
        }
    }

    private static object ItemSnapshot(
        LocalInventoryItem item,
        decimal? quantity,
        decimal? reservedQuantity) => new
    {
        id = item.Id,
        sku = item.Sku,
        name = item.Name,
        base_unit = item.BaseUnit,
        purchase_unit = item.PurchaseUnit ?? item.BaseUnit,
        purchase_to_base_factor = item.PurchaseToBaseFactor.ToString("0.######"),
        reorder_level = item.ReorderLevel.ToString("0.0000"),
        quantity = quantity?.ToString("0.0000"),
        on_hand = quantity?.ToString("0.0000"),
        reserved = reservedQuantity?.ToString("0.0000"),
        available = quantity.HasValue
            ? Quantity(quantity.Value - (reservedQuantity ?? 0m)).ToString("0.0000")
            : null,
        low_stock = quantity.HasValue
            ? Quantity(quantity.Value - (reservedQuantity ?? 0m)) <= Quantity(item.ReorderLevel)
            : (bool?)null,
        is_active = item.IsActive,
    };

    private static object SupplierSnapshot(LocalSupplier supplier) => new
    {
        id = supplier.Id,
        code = supplier.Code,
        name = supplier.Name,
        phone = supplier.Phone,
        email = supplier.Email,
        address = supplier.Address,
        is_active = supplier.IsActive,
    };

    private static async Task<object> RecipeSnapshotAsync(
        RestaurantDbContext db,
        LocalRecipe recipe,
        CancellationToken cancellationToken)
    {
        var menuItem = await db.MenuItems.AsNoTracking().SingleAsync(
            value => value.Id == recipe.MenuItemId,
            cancellationToken);
        var components = await db.RecipeItems
            .Where(value => value.RecipeId == recipe.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var ids = components.Select(value => value.InventoryItemId).ToArray();
        var items = await db.InventoryItems
            .Where(value => ids.Contains(value.Id))
            .AsNoTracking()
            .ToDictionaryAsync(value => value.Id, StringComparer.Ordinal, cancellationToken);

        return new
        {
            id = recipe.Id,
            branch_id = recipe.BranchId,
            menu_item = new { id = menuItem.Id, name = menuItem.Name, sku = menuItem.Sku },
            name = recipe.Name,
            version = recipe.Version,
            is_active = recipe.IsActive,
            items = components.Select(value => new
            {
                id = value.Id,
                inventory_item_id = value.InventoryItemId,
                inventory_item_name = items[value.InventoryItemId].Name,
                quantity_base = value.QuantityBase.ToString("0.0000"),
                base_unit = items[value.InventoryItemId].BaseUnit,
            }).ToArray(),
        };
    }

    private static async Task<object> MovementSnapshotAsync(
        RestaurantDbContext db,
        LocalStockMovement movement,
        CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.AsNoTracking().SingleAsync(
            value => value.Id == movement.InventoryItemId,
            cancellationToken);

        return new
        {
            id = movement.Id,
            branch_id = movement.BranchId,
            inventory_item = new
            {
                id = item.Id,
                sku = item.Sku,
                name = item.Name,
                base_unit = item.BaseUnit,
            },
            actor_user_id = movement.ActorUserId,
            movement_type = movement.MovementType,
            quantity_delta = movement.QuantityDelta.ToString("0.0000"),
            unit_cost = movement.UnitCost?.ToString("0.00"),
            source_type = movement.SourceType,
            source_id = movement.SourceId,
            source_line_id = movement.SourceLineId,
            idempotency_key = movement.IdempotencyKey,
            notes = movement.Notes,
            occurred_at = movement.OccurredAt,
        };
    }

    private static async Task<object> PurchaseOrderSnapshotAsync(
        RestaurantDbContext db,
        LocalPurchaseOrder po,
        CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(value => value.Id == po.SupplierId, cancellationToken);
        var lines = await db.PurchaseOrderLines
            .Where(value => value.PurchaseOrderId == po.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = po.Id,
            branch_id = po.BranchId,
            supplier = SupplierSnapshot(supplier),
            ordered_by_user_id = po.OrderedByUserId,
            po_number = po.PoNumber,
            status = po.Status,
            estimated_total = po.EstimatedTotal.ToString("0.00"),
            notes = po.Notes,
            ordered_at = po.OrderedAt,
            completed_at = po.CompletedAt,
            lines = lines.Select(value => new
            {
                id = value.Id,
                inventory_item_id = value.InventoryItemId,
                item_name = value.ItemName,
                purchase_unit = value.PurchaseUnit,
                conversion_factor = value.ConversionFactor.ToString("0.######"),
                ordered_purchase_quantity = value.OrderedPurchaseQuantity.ToString("0.0000"),
                ordered_base_quantity = value.OrderedBaseQuantity.ToString("0.0000"),
                received_base_quantity = value.ReceivedBaseQuantity.ToString("0.0000"),
                unit_cost = value.UnitCost.ToString("0.00"),
                line_total = value.LineTotal.ToString("0.00"),
            }).ToArray(),
        };
    }

    private static async Task<object> ReceiptSnapshotAsync(
        RestaurantDbContext db,
        LocalGoodsReceipt receipt,
        CancellationToken cancellationToken)
    {
        var lines = await db.GoodsReceiptLines
            .Where(value => value.GoodsReceiptId == receipt.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = receipt.Id,
            purchase_order_id = receipt.PurchaseOrderId,
            branch_id = receipt.BranchId,
            supplier_id = receipt.SupplierId,
            received_by_user_id = receipt.ReceivedByUserId,
            receipt_number = receipt.ReceiptNumber,
            client_receipt_id = receipt.ClientReceiptId,
            status = receipt.Status,
            received_at = receipt.ReceivedAt,
            notes = receipt.Notes,
            lines = lines.Select(value => new
            {
                id = value.Id,
                purchase_order_line_id = value.PurchaseOrderLineId,
                inventory_item_id = value.InventoryItemId,
                received_purchase_quantity = value.ReceivedPurchaseQuantity.ToString("0.0000"),
                received_base_quantity = value.ReceivedBaseQuantity.ToString("0.0000"),
                unit_cost = value.UnitCost.ToString("0.00"),
                line_total = value.LineTotal.ToString("0.00"),
            }).ToArray(),
        };
    }

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Factor(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureInventoryRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "inventory"))
        {
            throw new LocalSyncConflictException(
                "forbidden",
                "This user cannot manage restaurant inventory.",
                "rejected");
        }
    }
}

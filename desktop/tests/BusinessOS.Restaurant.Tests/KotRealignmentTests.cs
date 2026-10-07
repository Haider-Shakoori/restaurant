using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class KotRealignmentTests
{
    [Fact]
    public async Task One_order_supports_multiple_idempotent_kot_rounds_without_redispatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);

            await OpenAsync(sync);
            await AddAsync(sync, "ADD-1", "LINE-1", "item-grill", 2);
            var first = await PushOneAsync(sync, Waiter(), "SEND-1", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });
            Assert.Equal("accepted", first.GetProperty("status").GetString());

            await AddAsync(sync, "ADD-2", "LINE-2", "item-general", 1);
            var second = await PushOneAsync(sync, Waiter(), "SEND-2", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });
            var replay = await PushOneAsync(sync, Waiter(), "SEND-2", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            Assert.Equal("accepted", second.GetProperty("status").GetString());
            Assert.Equal(
                second.GetProperty("entity_id").GetString(),
                replay.GetProperty("entity_id").GetString());

            await using var db = factory.Create();
            var rounds = await db.KotRounds.OrderBy(x => x.RoundNumber).ToArrayAsync();
            var orderItems = await db.OrderItems.OrderBy(x => x.CreatedAtUtc).ToArrayAsync();
            var kitchenItems = await db.KitchenTicketItems.ToArrayAsync();

            Assert.Equal(2, rounds.Length);
            Assert.Equal([1, 2], rounds.Select(x => x.RoundNumber).ToArray());
            Assert.Equal(2, kitchenItems.Length);
            Assert.Equal(1, orderItems[0].RoundNumber);
            Assert.Equal(2, orderItems[1].RoundNumber);
            Assert.NotEqual(orderItems[0].KotRoundId, orderItems[1].KotRoundId);
            Assert.Matches("^KOT-[0-9]{4}$", rounds[0].KotNumber);
            Assert.Matches("^KOT-[0-9]{4}$", rounds[1].KotNumber);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, true, "queued")]
    [InlineData(true, false, "queued")]
    [InlineData(false, true, "active")]
    [InlineData(false, false, "active")]
    public async Task Queue_and_preparing_are_independent_workflow_settings(
        bool queueEnabled,
        bool preparingEnabled,
        string expectedInitialStatus)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            var settings = new LocalRestaurantSettingsService(factory);
            await settings.UpdateAsync(
                new RestaurantWorkflowSettingsUpdate(
                    queueEnabled,
                    preparingEnabled,
                    ExpoEnabled: false,
                    CoursesEnabled: false,
                    KotSoundEnabled: true,
                    KitchenWarningMinutes: 10,
                    KitchenLateMinutes: 20,
                    RequireManagerApprovalForPostKotVoid: true),
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            await OpenAsync(sync);
            await AddAsync(sync, "ADD", "LINE-1", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            string ticketId;
            await using (var db = factory.Create())
            {
                var ticket = await db.KitchenTickets.SingleAsync();
                ticketId = ticket.Id;
                Assert.Equal(expectedInitialStatus, ticket.Status);
                var round = await db.KotRounds.SingleAsync();
                Assert.Equal(queueEnabled, round.QueueEnabled);
                Assert.Equal(preparingEnabled, round.PreparingEnabled);
            }

            if (preparingEnabled)
            {
                await Assert.ThrowsAsync<LocalSyncConflictException>(
                    () => kitchen.ReadyAsync(ticketId, Kitchen(), CancellationToken.None));
                await kitchen.StartAsync(ticketId, Kitchen(), CancellationToken.None);
            }
            else
            {
                await Assert.ThrowsAsync<LocalSyncConflictException>(
                    () => kitchen.StartAsync(ticketId, Kitchen(), CancellationToken.None));
            }

            await kitchen.ReadyAsync(ticketId, Kitchen(), CancellationToken.None);

            await using var finalDb = factory.Create();
            Assert.Equal("ready", (await finalDb.KitchenTickets.SingleAsync()).Status);
            Assert.Equal("ready", (await finalDb.Orders.SingleAsync()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Settings_change_affects_new_rounds_without_rewriting_existing_round_history()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var settings = new LocalRestaurantSettingsService(factory);
            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);

            await OpenAsync(sync);
            await AddAsync(sync, "ADD-1", "LINE-1", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND-1", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            await settings.UpdateAsync(
                new RestaurantWorkflowSettingsUpdate(
                    KitchenQueueEnabled: false,
                    PreparingStageEnabled: false,
                    ExpoEnabled: false,
                    CoursesEnabled: false,
                    KotSoundEnabled: true,
                    KitchenWarningMinutes: 10,
                    KitchenLateMinutes: 20,
                    RequireManagerApprovalForPostKotVoid: true),
                Manager(),
                CancellationToken.None);

            await AddAsync(sync, "ADD-2", "LINE-2", "item-general", 1);
            await PushOneAsync(sync, Waiter(), "SEND-2", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            await using var db = factory.Create();
            var rounds = await db.KotRounds.OrderBy(x => x.RoundNumber).ToArrayAsync();

            Assert.True(rounds[0].QueueEnabled);
            Assert.True(rounds[0].PreparingEnabled);
            Assert.False(rounds[1].QueueEnabled);
            Assert.False(rounds[1].PreparingEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Inventory_is_reserved_on_send_and_consumed_once_per_production_item()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);

            var stockItemJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "CHICKEN",
                    "Chicken",
                    "g",
                    "kg",
                    1000m,
                    0m,
                    Manager(),
                    CancellationToken.None));
            var stockItemId = stockItemJson.GetProperty("id").GetString()!;

            await inventory.AdjustAsync(
                "branch-1",
                stockItemId,
                5000m,
                "OPENING",
                "Opening stock",
                Manager(),
                CancellationToken.None);

            await inventory.CreateRecipeVersionAsync(
                "branch-1",
                "item-grill",
                "Grilled Chicken",
                [new LocalRecipeComponentRequest(stockItemId, 250m)],
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory, inventory);
            var sync = new LocalSyncService(factory, catalog, kitchen);

            await OpenAsync(sync);
            await AddAsync(sync, "ADD-1", "LINE-1", "item-grill", 2);
            await PushOneAsync(sync, Waiter(), "SEND-1", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            string firstTicketId;
            await using (var db = factory.Create())
            {
                firstTicketId = (await db.KitchenTickets.SingleAsync()).Id;
                Assert.Equal(5000m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Equal(500m, (await db.InventoryReservationLines.SingleAsync()).QuantityBase);
                Assert.Equal("reserved", (await db.InventoryReservations.SingleAsync()).Status);
            }

            await kitchen.StartAsync(firstTicketId, Kitchen(), CancellationToken.None);
            await kitchen.StartAsync(firstTicketId, Kitchen(), CancellationToken.None);

            await using (var db = factory.Create())
            {
                Assert.Equal(4500m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Single(await db.InventoryConsumptions.ToArrayAsync());
                Assert.Single(await db.StockMovements.Where(x => x.MovementType == "consumption").ToArrayAsync());
                Assert.Equal("committed", (await db.InventoryReservations.SingleAsync()).Status);
            }

            await AddAsync(sync, "ADD-2", "LINE-2", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND-2", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            string secondTicketId;
            await using (var db = factory.Create())
            {
                secondTicketId = (await db.KitchenTickets.OrderByDescending(x => x.RoundNumber).FirstAsync()).Id;
                Assert.Equal(4500m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Equal(2, await db.InventoryReservations.CountAsync());
            }

            await kitchen.StartAsync(secondTicketId, Kitchen(), CancellationToken.None);

            await using var finalDb = factory.Create();
            Assert.Equal(4250m, (await finalDb.InventoryBalances.SingleAsync()).Quantity);
            Assert.Equal(2, await finalDb.InventoryConsumptions.CountAsync());
            Assert.Equal(2, await finalDb.StockMovements.CountAsync(x => x.MovementType == "consumption"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Modifier_groups_enforce_required_and_max_selections_and_price_delta()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            await using (var db = factory.Create())
            {
                db.ModifierGroups.Add(new LocalModifierGroup
                {
                    Id = "group-sauce",
                    Name = "Sauce",
                    MinSelections = 1,
                    MaxSelections = 2,
                    SortOrder = 1,
                    IsActive = true,
                });
                db.ModifierOptions.AddRange(
                    new LocalModifierOption
                    {
                        Id = "opt-hot",
                        ModifierGroupId = "group-sauce",
                        Name = "Hot",
                        PriceDelta = 10m,
                        SortOrder = 1,
                        IsActive = true,
                    },
                    new LocalModifierOption
                    {
                        Id = "opt-garlic",
                        ModifierGroupId = "group-sauce",
                        Name = "Garlic",
                        PriceDelta = 15m,
                        SortOrder = 2,
                        IsActive = true,
                    },
                    new LocalModifierOption
                    {
                        Id = "opt-yogurt",
                        ModifierGroupId = "group-sauce",
                        Name = "Yogurt",
                        PriceDelta = 5m,
                        SortOrder = 3,
                        IsActive = true,
                    });
                db.MenuItemModifierGroups.Add(new LocalMenuItemModifierGroup
                {
                    MenuItemId = "item-grill",
                    ModifierGroupId = "group-sauce",
                    SortOrder = 1,
                });
                await db.SaveChangesAsync();
            }

            var sync = new LocalSyncService(factory, catalog);
            await OpenAsync(sync);

            var missing = await PushOneAsync(sync, Waiter(), "MOD-MISSING", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-MISSING",
                menu_item_id = "item-grill",
                quantity = 1,
            });
            Assert.Equal("conflict", missing.GetProperty("status").GetString());
            Assert.Equal("modifier_selection_required", missing.GetProperty("code").GetString());

            var tooMany = await PushOneAsync(sync, Waiter(), "MOD-MAX", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-MAX",
                menu_item_id = "item-grill",
                quantity = 1,
                modifiers = new[]
                {
                    new { option_id = "opt-hot" },
                    new { option_id = "opt-garlic" },
                    new { option_id = "opt-yogurt" },
                },
            });
            Assert.Equal("conflict", tooMany.GetProperty("status").GetString());
            Assert.Equal("modifier_selection_limit", tooMany.GetProperty("code").GetString());

            var accepted = await PushOneAsync(sync, Waiter(), "MOD-OK", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-OK",
                menu_item_id = "item-grill",
                quantity = 1,
                modifiers = new[]
                {
                    new { option_id = "opt-hot" },
                    new { option_id = "opt-garlic" },
                },
            });
            Assert.Equal("accepted", accepted.GetProperty("status").GetString());

            await using var finalDb = factory.Create();
            var line = await finalDb.OrderItems.SingleAsync();
            Assert.Equal(375m, line.UnitPrice);
            Assert.Contains("Hot", line.ModifiersJson, StringComparison.Ordinal);
            Assert.Contains("Garlic", line.ModifiersJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workflow_settings_persist_across_service_restart_and_enqueue_cloud_sync()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var settings = new LocalRestaurantSettingsService(factory);

            await settings.UpdateAsync(
                new RestaurantWorkflowSettingsUpdate(
                    KitchenQueueEnabled: false,
                    PreparingStageEnabled: true,
                    ExpoEnabled: true,
                    CoursesEnabled: true,
                    KotSoundEnabled: false,
                    KitchenWarningMinutes: 7,
                    KitchenLateMinutes: 15,
                    RequireManagerApprovalForPostKotVoid: false),
                Manager(),
                CancellationToken.None);

            var restartedFactory = new LocalDatabaseFactory(root);
            var restarted = new LocalRestaurantSettingsService(restartedFactory);
            var loaded = await restarted.GetAsync(CancellationToken.None);

            Assert.False(loaded.KitchenQueueEnabled);
            Assert.True(loaded.PreparingStageEnabled);
            Assert.True(loaded.ExpoEnabled);
            Assert.True(loaded.CoursesEnabled);
            Assert.False(loaded.KotSoundEnabled);
            Assert.Equal(7, loaded.KitchenWarningMinutes);
            Assert.Equal(15, loaded.KitchenLateMinutes);
            Assert.False(loaded.RequireManagerApprovalForPostKotVoid);

            await using var db = restartedFactory.Create();
            var outbox = await db.CloudOutbox
                .SingleAsync(x => x.EntityType == "restaurant_settings" && x.LocalEntityId == "workflow");
            Assert.Equal("restaurant.settings.update", outbox.Operation);
            Assert.True(await db.Changes.AnyAsync(x => x.EntityType == "restaurant_settings"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Expo_enabled_gates_ready_until_expo_passes_item()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            var settings = new LocalRestaurantSettingsService(factory);
            await settings.UpdateAsync(
                new RestaurantWorkflowSettingsUpdate(
                    KitchenQueueEnabled: true,
                    PreparingStageEnabled: true,
                    ExpoEnabled: true,
                    CoursesEnabled: false,
                    KotSoundEnabled: false,
                    KitchenWarningMinutes: 10,
                    KitchenLateMinutes: 20,
                    RequireManagerApprovalForPostKotVoid: true),
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            await OpenAsync(sync);
            await AddAsync(sync, "ADD", "LINE-1", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            string ticketId;
            string kitchenItemId;
            await using (var db = factory.Create())
            {
                ticketId = (await db.KitchenTickets.SingleAsync()).Id;
                kitchenItemId = (await db.KitchenTicketItems.SingleAsync()).Id;
            }

            await kitchen.StartAsync(ticketId, Kitchen(), CancellationToken.None);
            await kitchen.ReadyItemAsync(kitchenItemId, Kitchen(), CancellationToken.None);

            await using (var db = factory.Create())
            {
                Assert.Equal("expo", (await db.KitchenTicketItems.SingleAsync()).Status);
                Assert.Equal("expo", (await db.KitchenTickets.SingleAsync()).Status);
                Assert.Equal("expo", (await db.Orders.SingleAsync()).Status);
            }

            await kitchen.PassExpoItemAsync(kitchenItemId, Expo(), CancellationToken.None);

            await using var finalDb = factory.Create();
            Assert.Equal("ready", (await finalDb.KitchenTicketItems.SingleAsync()).Status);
            Assert.Equal("ready", (await finalDb.KitchenTickets.SingleAsync()).Status);
            Assert.Equal("ready", (await finalDb.Orders.SingleAsync()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Post_kot_void_requires_manager_and_releases_unstarted_reservation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);

            var stockJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "CHICKEN",
                    "Chicken",
                    "g",
                    "kg",
                    1000m,
                    0m,
                    Manager(),
                    CancellationToken.None));
            var stockId = stockJson.GetProperty("id").GetString()!;
            await inventory.AdjustAsync(
                "branch-1",
                stockId,
                1000m,
                "OPENING-VOID",
                "Opening stock",
                Manager(),
                CancellationToken.None);
            await inventory.CreateRecipeVersionAsync(
                "branch-1",
                "item-grill",
                "Grilled Chicken",
                [new LocalRecipeComponentRequest(stockId, 250m)],
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory, inventory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            await OpenAsync(sync);
            await AddAsync(sync, "ADD", "LINE-1", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            var rejected = await PushOneAsync(sync, Waiter(), "VOID-WAITER", "order.item.void", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                reason = "Guest changed mind",
            });
            Assert.Equal("rejected", rejected.GetProperty("status").GetString());
            Assert.Equal("manager_approval_required", rejected.GetProperty("code").GetString());

            var approved = await PushOneAsync(sync, Manager(), "VOID-MANAGER", "order.item.void", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                reason = "Manager approved guest cancellation",
            });
            Assert.Equal("accepted", approved.GetProperty("status").GetString());

            await using var db = factory.Create();
            Assert.Equal("voided", (await db.OrderItems.SingleAsync()).Status);
            Assert.Equal("voided", (await db.KitchenTicketItems.SingleAsync()).Status);
            Assert.Equal("released", (await db.InventoryReservations.SingleAsync()).Status);
            Assert.Equal(1000m, (await db.InventoryBalances.SingleAsync()).Quantity);
            Assert.Empty(await db.InventoryConsumptions.ToArrayAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Refire_creates_new_round_and_second_production_without_duplicate_bill_line()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);

            var stockJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "CHICKEN",
                    "Chicken",
                    "g",
                    "kg",
                    1000m,
                    0m,
                    Manager(),
                    CancellationToken.None));
            var stockId = stockJson.GetProperty("id").GetString()!;
            await inventory.AdjustAsync(
                "branch-1",
                stockId,
                1000m,
                "OPENING-REFIRE",
                "Opening stock",
                Manager(),
                CancellationToken.None);
            await inventory.CreateRecipeVersionAsync(
                "branch-1",
                "item-grill",
                "Grilled Chicken",
                [new LocalRecipeComponentRequest(stockId, 250m)],
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory, inventory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            await OpenAsync(sync);
            await AddAsync(sync, "ADD", "LINE-1", "item-grill", 1);
            await PushOneAsync(sync, Waiter(), "SEND", "order.kot.send", new
            {
                client_order_id = "ORDER-1",
            });

            string firstTicketId;
            string firstKitchenItemId;
            await using (var db = factory.Create())
            {
                firstTicketId = (await db.KitchenTickets.SingleAsync()).Id;
                firstKitchenItemId = (await db.KitchenTicketItems.SingleAsync()).Id;
            }

            await kitchen.StartAsync(firstTicketId, Kitchen(), CancellationToken.None);
            await kitchen.ReadyAsync(firstTicketId, Kitchen(), CancellationToken.None);

            await kitchen.RefireItemAsync(
                firstKitchenItemId,
                "REFIRE-1",
                "Dropped plate",
                Kitchen(),
                CancellationToken.None);
            await kitchen.RefireItemAsync(
                firstKitchenItemId,
                "REFIRE-1",
                "Dropped plate",
                Kitchen(),
                CancellationToken.None);

            string secondTicketId;
            await using (var db = factory.Create())
            {
                Assert.Equal(2, await db.KotRounds.CountAsync());
                Assert.Equal(2, await db.KitchenTicketItems.CountAsync());
                Assert.Single(await db.OrderItems.ToArrayAsync());

                var refire = await db.KitchenTicketItems
                    .SingleAsync(x => x.RefireOfKitchenItemId == firstKitchenItemId);
                Assert.Equal("Dropped plate", refire.RefireReason);
                secondTicketId = refire.KitchenTicketId;
            }

            await kitchen.StartAsync(secondTicketId, Kitchen(), CancellationToken.None);

            await using var finalDb = factory.Create();
            Assert.Equal(500m, (await finalDb.InventoryBalances.SingleAsync()).Quantity);
            Assert.Equal(2, await finalDb.InventoryConsumptions.CountAsync());
            Assert.Equal(2, await finalDb.StockMovements.CountAsync(x => x.MovementType == "consumption"));
            Assert.Single(await finalDb.OrderItems.ToArrayAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Course_fire_is_idempotent_and_dispatches_held_items()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var settings = new LocalRestaurantSettingsService(factory);
            await settings.UpdateAsync(
                new RestaurantWorkflowSettingsUpdate(
                    KitchenQueueEnabled: true,
                    PreparingStageEnabled: true,
                    ExpoEnabled: false,
                    CoursesEnabled: true,
                    KotSoundEnabled: false,
                    KitchenWarningMinutes: 10,
                    KitchenLateMinutes: 20,
                    RequireManagerApprovalForPostKotVoid: true),
                Manager(),
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            await OpenAsync(sync);
            await PushOneAsync(sync, Waiter(), "ADD-COURSE", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-COURSE",
                menu_item_id = "item-grill",
                quantity = 1,
                course_number = 2,
                course_name = "Main",
                held = true,
            });

            var first = await PushOneAsync(sync, Waiter(), "FIRE-2", "course.fire", new
            {
                client_order_id = "ORDER-1",
                course_number = 2,
            });
            var replay = await PushOneAsync(sync, Waiter(), "FIRE-2", "course.fire", new
            {
                client_order_id = "ORDER-1",
                course_number = 2,
            });

            Assert.Equal("accepted", first.GetProperty("status").GetString());
            Assert.Equal(first.GetProperty("entity_id").GetString(), replay.GetProperty("entity_id").GetString());

            await using var db = factory.Create();
            Assert.Single(await db.KotRounds.ToArrayAsync());
            Assert.Single(await db.KitchenTicketItems.ToArrayAsync());
            Assert.NotEqual("held", (await db.OrderItems.SingleAsync()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Takeaway_order_uses_branch_context_without_fake_table()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalog);

            var opened = await PushOneAsync(sync, Waiter(), "OPEN-TAKEAWAY", "order.open", new
            {
                client_order_id = "TAKEAWAY-1",
                branch_id = "branch-1",
                service_type = "takeaway",
                service_reference = "Pickup Ali",
                guest_count = 1,
            });

            Assert.Equal("accepted", opened.GetProperty("status").GetString());

            await using var db = factory.Create();
            var order = await db.Orders.SingleAsync();
            Assert.Equal("takeaway", order.ServiceType);
            Assert.Equal("branch-1", order.BranchId);
            Assert.Equal(string.Empty, order.DiningTableId);
            Assert.Equal("Pickup Ali", order.ServiceReference);
            Assert.Equal("available", (await db.DiningTables.SingleAsync(x => x.Id == "table-1")).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Split_moves_only_unsent_lines_and_keeps_source_kot_lineage_unchanged()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalog);
            var cashier = new LocalCashierService(factory);

            await OpenAsync(sync);
            await AddAsync(sync, "ADD-1", "LINE-1", "item-grill", 1);
            await AddAsync(sync, "ADD-2", "LINE-2", "item-general", 1);

            string sourceOrderId;
            string movedItemId;
            await using (var db = factory.Create())
            {
                sourceOrderId = (await db.Orders.SingleAsync()).Id;
                movedItemId = (await db.OrderItems.SingleAsync(x => x.ClientLineId == "LINE-2")).Id;
            }

            await cashier.SplitUnsentItemsAsync(
                sourceOrderId,
                "table-2",
                [movedItemId],
                Manager(),
                CancellationToken.None);

            await using var finalDb = factory.Create();
            var orders = await finalDb.Orders.OrderBy(x => x.CreatedAtUtc).ToArrayAsync();
            Assert.Equal(2, orders.Length);
            Assert.Equal(sourceOrderId, (await finalDb.OrderItems.SingleAsync(x => x.ClientLineId == "LINE-1")).OrderId);
            Assert.NotEqual(sourceOrderId, (await finalDb.OrderItems.SingleAsync(x => x.ItemName == "Salad")).OrderId);
            Assert.Equal("occupied", (await finalDb.DiningTables.SingleAsync(x => x.Id == "table-2")).Status);
            Assert.Empty(await finalDb.KitchenTickets.ToArrayAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task OpenAsync(LocalSyncService sync) =>
        PushOneAsync(sync, Waiter(), "OPEN", "order.open", new
        {
            client_order_id = "ORDER-1",
            dining_table_id = "table-1",
            guest_count = 2,
        });

    private static Task AddAsync(
        LocalSyncService sync,
        string mutationId,
        string clientLineId,
        string menuItemId,
        int quantity) =>
        PushOneAsync(sync, Waiter(), mutationId, "order.item.add", new
        {
            client_order_id = "ORDER-1",
            client_line_id = clientLineId,
            menu_item_id = menuItemId,
            quantity,
        });

    private static async Task<JsonElement> PushOneAsync(
        LocalSyncService sync,
        LocalTerminalPrincipal principal,
        string mutationId,
        string operation,
        object payload)
    {
        var request = new LocalSyncPushRequest(
            "kot-realignment",
            [
                new LocalSyncMutationRequest(
                    mutationId,
                    operation,
                    DateTimeOffset.UtcNow,
                    JsonSerializer.SerializeToElement(payload)),
            ]);

        var response = JsonSerializer.SerializeToElement(
            await sync.PushAsync(principal, request, CancellationToken.None));
        return response.GetProperty("results")[0];
    }

    private static LocalTerminalPrincipal Waiter() =>
        new("waiter-device", 1, "waiter-1", "Waiter One", "waiter", "tenant-1");

    private static LocalTerminalPrincipal Kitchen() =>
        new("kitchen-device", 2, "kitchen-1", "Kitchen One", "kitchen", "tenant-1");

    private static LocalTerminalPrincipal Manager() =>
        new("manager-device", 3, "manager-1", "Manager One", "manager", "tenant-1");

    private static LocalTerminalPrincipal Expo() =>
        new("expo-device", 4, "expo-1", "Expo One", "expo", "tenant-1");

    private static OperationalSnapshot Snapshot() =>
        new(
            1,
            DateTimeOffset.UtcNow,
            0,
            "tenant-1",
            [new BranchSnapshot("branch-1", "MAIN", "Main Branch", true)],
            [
                new StaffSnapshot(1, "waiter-1", "Waiter One", "waiter@example.test", "waiter", true),
                new StaffSnapshot(2, "kitchen-1", "Kitchen One", "kitchen@example.test", "kitchen", true),
                new StaffSnapshot(3, "manager-1", "Manager One", "manager@example.test", "manager", true),
            ],
            [
                new MenuCategorySnapshot(
                    "category-1",
                    "Main",
                    1,
                    [
                        new MenuItemSnapshot(
                            "item-grill",
                            "category-1",
                            "GRILL-1",
                            "Grilled Chicken",
                            null,
                            350m,
                            "AFN",
                            1,
                            []),
                        new MenuItemSnapshot(
                            "item-general",
                            "category-1",
                            "GEN-1",
                            "Salad",
                            null,
                            120m,
                            "AFN",
                            2,
                            []),
                    ]),
            ],
            [
                new DiningTableSnapshot(
                    "table-1",
                    "T-01",
                    "Table 1",
                    4,
                    "available",
                    true,
                    new DiningAreaSummary("area-1", "Main Hall"),
                    new BranchSummary("branch-1", "Main Branch")),
                new DiningTableSnapshot(
                    "table-2",
                    "T-02",
                    "Table 2",
                    4,
                    "available",
                    true,
                    new DiningAreaSummary("area-1", "Main Hall"),
                    new BranchSummary("branch-1", "Main Branch")),
            ],
            new KitchenSnapshot(
                [
                    new KitchenStationSnapshot(
                        "station-grill",
                        "branch-1",
                        "GRILL",
                        "Grill Station",
                        1,
                        true),
                ],
                [
                    new MenuItemKitchenRouteSnapshot(
                        "route-1",
                        "item-grill",
                        "branch-1",
                        "station-grill"),
                ]));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

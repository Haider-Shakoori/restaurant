using BusinessOS.Restaurant.Application.OperationalData;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Persistence;

public sealed class OperationalSnapshotStore
{
    private readonly LocalDatabaseFactory _databaseFactory;

    public OperationalSnapshotStore(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task ApplyAsync(
        OperationalSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Branches.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.DiningAreas.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.DiningTables.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.MenuCategories.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.MenuItems.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsAvailable, false),
            cancellationToken);
        await db.ModifierGroups.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.ModifierOptions.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.StaffUsers.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);
        await db.KitchenStations.ExecuteUpdateAsync(
            setters => setters.SetProperty(value => value.IsActive, false),
            cancellationToken);

        db.MenuItemModifierGroups.RemoveRange(db.MenuItemModifierGroups);
        db.MenuItemKitchenRoutes.RemoveRange(db.MenuItemKitchenRoutes);

        foreach (var branch in snapshot.Branches)
        {
            var entity = await db.Branches.FindAsync([branch.Id], cancellationToken);

            if (entity is null)
            {
                entity = new LocalBranch
                {
                    Id = branch.Id,
                    Code = branch.Code,
                    Name = branch.Name,
                };
                db.Branches.Add(entity);
            }

            entity.Code = branch.Code;
            entity.Name = branch.Name;
            entity.IsActive = branch.IsActive;
        }

        foreach (var staff in snapshot.Staff)
        {
            var entity = await db.StaffUsers.FindAsync([staff.Id], cancellationToken);

            if (entity is null)
            {
                entity = new LocalStaffUser
                {
                    Id = staff.Id,
                    PublicId = staff.PublicId,
                    Name = staff.Name,
                    Email = staff.Email,
                    Role = staff.Role,
                };
                db.StaffUsers.Add(entity);
            }

            entity.PublicId = staff.PublicId;
            entity.Name = staff.Name;
            entity.Email = staff.Email;
            entity.Role = staff.Role;
            entity.IsActive = staff.IsActive;
        }

        foreach (var category in snapshot.Menu)
        {
            var categoryEntity = await db.MenuCategories.FindAsync([category.Id], cancellationToken);

            if (categoryEntity is null)
            {
                categoryEntity = new LocalMenuCategory
                {
                    Id = category.Id,
                    Name = category.Name,
                };
                db.MenuCategories.Add(categoryEntity);
            }

            categoryEntity.Name = category.Name;
            categoryEntity.SortOrder = category.SortOrder;
            categoryEntity.IsActive = true;

            foreach (var item in category.Items)
            {
                var itemEntity = await db.MenuItems.FindAsync([item.Id], cancellationToken);

                if (itemEntity is null)
                {
                    itemEntity = new LocalMenuItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                    };
                    db.MenuItems.Add(itemEntity);
                }

                itemEntity.MenuCategoryId = item.MenuCategoryId;
                itemEntity.Sku = item.Sku;
                itemEntity.Name = item.Name;
                itemEntity.Description = item.Description;
                itemEntity.ImageUrl = item.ImageUrl;
                itemEntity.Price = item.Price;
                itemEntity.Currency = item.Currency;
                itemEntity.SortOrder = item.SortOrder;
                itemEntity.IsAvailable = true;

                foreach (var group in item.ModifierGroups)
                {
                    var groupEntity = await db.ModifierGroups.FindAsync([group.Id], cancellationToken);

                    if (groupEntity is null)
                    {
                        groupEntity = new LocalModifierGroup
                        {
                            Id = group.Id,
                            Name = group.Name,
                        };
                        db.ModifierGroups.Add(groupEntity);
                    }

                    groupEntity.Name = group.Name;
                    groupEntity.MinSelections = group.MinSelections;
                    groupEntity.MaxSelections = group.MaxSelections;
                    groupEntity.SortOrder = group.SortOrder;
                    groupEntity.IsActive = true;

                    var link = new LocalMenuItemModifierGroup
                    {
                        MenuItemId = item.Id,
                        ModifierGroupId = group.Id,
                        SortOrder = group.SortOrder,
                    };
                    db.MenuItemModifierGroups.Add(link);

                    foreach (var option in group.Options)
                    {
                        var optionEntity = await db.ModifierOptions.FindAsync([option.Id], cancellationToken);

                        if (optionEntity is null)
                        {
                            optionEntity = new LocalModifierOption
                            {
                                Id = option.Id,
                                ModifierGroupId = group.Id,
                                Name = option.Name,
                            };
                            db.ModifierOptions.Add(optionEntity);
                        }

                        optionEntity.ModifierGroupId = group.Id;
                        optionEntity.Name = option.Name;
                        optionEntity.PriceDelta = option.PriceDelta;
                        optionEntity.SortOrder = option.SortOrder;
                        optionEntity.IsActive = true;
                    }
                }
            }
        }

        foreach (var station in snapshot.Kitchen?.Stations ?? [])
        {
            var entity = await db.KitchenStations.FindAsync([station.Id], cancellationToken);

            if (entity is null)
            {
                entity = new LocalKitchenStation
                {
                    Id = station.Id,
                    BranchId = station.BranchId,
                    Code = station.Code,
                    Name = station.Name,
                };
                db.KitchenStations.Add(entity);
            }

            entity.BranchId = station.BranchId;
            entity.Code = station.Code;
            entity.Name = station.Name;
            entity.SortOrder = station.SortOrder;
            entity.IsActive = station.IsActive;
        }

        foreach (var route in snapshot.Kitchen?.Routes ?? [])
        {
            db.MenuItemKitchenRoutes.Add(new LocalMenuItemKitchenRoute
            {
                Id = route.Id,
                MenuItemId = route.MenuItemId,
                BranchId = route.BranchId,
                KitchenStationId = route.KitchenStationId,
            });
        }

        // v1 snapshots may not contain standalone areas. When present, they
        // are authoritative even for dining floors with zero tables.
        foreach (var areaSnapshot in snapshot.Areas ?? [])
        {
            var areaEntity = await db.DiningAreas.FindAsync([areaSnapshot.Id], cancellationToken);
            if (areaEntity is null)
            {
                areaEntity = new LocalDiningArea
                {
                    Id = areaSnapshot.Id,
                    BranchId = areaSnapshot.BranchId,
                    Name = areaSnapshot.Name,
                };
                db.DiningAreas.Add(areaEntity);
            }

            areaEntity.BranchId = areaSnapshot.BranchId;
            areaEntity.Name = areaSnapshot.Name;
            areaEntity.SortOrder = areaSnapshot.SortOrder;
            areaEntity.IsActive = areaSnapshot.IsActive;
        }

        foreach (var table in snapshot.Tables)
        {
            var branch = await db.Branches.FindAsync([table.Branch.Id], cancellationToken);

            if (branch is null)
            {
                branch = new LocalBranch
                {
                    Id = table.Branch.Id,
                    Code = table.Branch.Id,
                    Name = table.Branch.Name,
                    IsActive = true,
                };
                db.Branches.Add(branch);
            }

            var area = await db.DiningAreas.FindAsync([table.Area.Id], cancellationToken);

            if (area is null)
            {
                area = new LocalDiningArea
                {
                    Id = table.Area.Id,
                    BranchId = table.Branch.Id,
                    Name = table.Area.Name,
                };
                db.DiningAreas.Add(area);
            }

            area.BranchId = table.Branch.Id;
            area.Name = table.Area.Name;
            // Use the standalone area's sort order where available; legacy
            // table-only snapshots still carry enough information to recover it.
            area.IsActive = true;

            var tableEntity = await db.DiningTables.FindAsync([table.Id], cancellationToken);

            if (tableEntity is null)
            {
                tableEntity = new LocalDiningTable
                {
                    Id = table.Id,
                    DiningAreaId = table.Area.Id,
                    Code = table.Code,
                    Name = table.Name,
                    Status = table.Status,
                };
                db.DiningTables.Add(tableEntity);
            }

            tableEntity.DiningAreaId = table.Area.Id;
            tableEntity.Code = table.Code;
            tableEntity.Name = table.Name;
            tableEntity.Capacity = table.Capacity;
            // A cloud catalog refresh must never free an occupied local table
            // while an offline dine-in order is still open on this Windows host.
            var hasLocalActiveOrder = await db.Orders.AsNoTracking().AnyAsync(
                order => order.DiningTableId == table.Id &&
                         order.ServiceType == "dine_in" &&
                         order.Status != "closed" && order.Status != "cancelled",
                cancellationToken);
            tableEntity.Status = hasLocalActiveOrder ? "occupied" : table.Status;
            tableEntity.IsActive = table.IsActive;
        }

        var state = await db.OperationalStates.FindAsync([1], cancellationToken);

        if (state is null)
        {
            state = new LocalOperationalState
            {
                Id = 1,
                TenantId = snapshot.TenantId,
            };
            db.OperationalStates.Add(state);
        }

        state.TenantId = snapshot.TenantId;
        state.Cursor = snapshot.Cursor;
        state.ServerTime = snapshot.ServerTime;
        state.RefreshedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<LocalCatalogSnapshot> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var branches = await db.Branches
            .Where(value => value.IsActive)
            .OrderBy(value => value.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Filter parent activation as well. This matches the Laravel page,
        // API and cloud bootstrap and avoids missing-area dictionary lookups.
        var tables = await (from table in db.DiningTables.AsNoTracking()
            join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
            join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
            where table.IsActive && area.IsActive && branch.IsActive
            orderby table.Code
            select table).ToListAsync(cancellationToken);

        var areas = await db.DiningAreas
            .Where(value => value.IsActive)
            .AsNoTracking()
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        var categories = await db.MenuCategories
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var items = await db.MenuItems
            .Where(value => value.IsAvailable)
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var groups = await db.ModifierGroups
            .Where(value => value.IsActive)
            .AsNoTracking()
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        var options = await db.ModifierOptions
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var links = await db.MenuItemModifierGroups
            .OrderBy(value => value.SortOrder)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var stations = await db.KitchenStations
            .Where(value => value.IsActive)
            .OrderBy(value => value.BranchId)
            .ThenBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var kitchenRoutes = await db.MenuItemKitchenRoutes
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var state = await db.OperationalStates.AsNoTracking().SingleOrDefaultAsync(value => value.Id == 1, cancellationToken);

        return new LocalCatalogSnapshot(
            branches,
            areas,
            tables,
            categories,
            items,
            groups,
            options,
            links,
            stations,
            kitchenRoutes,
            state);
    }
}

public sealed record LocalCatalogSnapshot(
    IReadOnlyList<LocalBranch> Branches,
    IReadOnlyDictionary<string, LocalDiningArea> Areas,
    IReadOnlyList<LocalDiningTable> Tables,
    IReadOnlyList<LocalMenuCategory> Categories,
    IReadOnlyList<LocalMenuItem> Items,
    IReadOnlyDictionary<string, LocalModifierGroup> ModifierGroups,
    IReadOnlyList<LocalModifierOption> ModifierOptions,
    IReadOnlyList<LocalMenuItemModifierGroup> MenuItemModifierGroups,
    IReadOnlyList<LocalKitchenStation> KitchenStations,
    IReadOnlyList<LocalMenuItemKitchenRoute> KitchenRoutes,
    LocalOperationalState? State);

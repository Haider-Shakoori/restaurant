namespace BusinessOS.Restaurant.Persistence;

public sealed class LocalBranch
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalDiningArea
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalDiningTable
{
    public required string Id { get; set; }
    public required string DiningAreaId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; }
    public required string Status { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalMenuCategory
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalMenuItem
{
    public required string Id { get; set; }
    public string? MenuCategoryId { get; set; }
    public string? Sku { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "AFN";
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class LocalModifierGroup
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalModifierOption
{
    public required string Id { get; set; }
    public required string ModifierGroupId { get; set; }
    public required string Name { get; set; }
    public decimal PriceDelta { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalMenuItemModifierGroup
{
    public required string MenuItemId { get; set; }
    public required string ModifierGroupId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LocalStaffUser
{
    public long Id { get; set; }
    public required string PublicId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalOperationalState
{
    public int Id { get; set; }
    public required string TenantId { get; set; }
    public long Cursor { get; set; }
    public DateTimeOffset ServerTime { get; set; }
    public DateTimeOffset RefreshedAtUtc { get; set; }
}


public sealed class LocalKitchenStation
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalMenuItemKitchenRoute
{
    public required string Id { get; set; }
    public required string MenuItemId { get; set; }
    public required string BranchId { get; set; }
    public required string KitchenStationId { get; set; }
}

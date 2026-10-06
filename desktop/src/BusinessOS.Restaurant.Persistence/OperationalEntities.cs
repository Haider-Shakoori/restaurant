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


public sealed class LocalDevice
{
    public required string Id { get; set; }
    public required string DeviceUid { get; set; }
    public required string DeviceName { get; set; }
    public long StaffUserId { get; set; }
    public required string SecretHash { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset PairedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class LocalSession
{
    public required string Id { get; set; }
    public required string DeviceId { get; set; }
    public long StaffUserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class LocalOrder
{
    public required string Id { get; set; }
    public required string ClientOrderId { get; set; }
    public string? CloudOrderId { get; set; }
    public required string DiningTableId { get; set; }
    public long WaiterId { get; set; }
    public required string Status { get; set; }
    public int GuestCount { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? ServedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public bool CloudSynced { get; set; }
}

public sealed class LocalOrderItem
{
    public required string Id { get; set; }
    public required string ClientLineId { get; set; }
    public string? CloudOrderItemId { get; set; }
    public required string OrderId { get; set; }
    public required string MenuItemId { get; set; }
    public required string ItemName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
    public required string Status { get; set; }
}

public sealed class LocalMutation
{
    public long Id { get; set; }
    public required string DeviceId { get; set; }
    public required string MutationId { get; set; }
    public required string Operation { get; set; }
    public required string RequestHash { get; set; }
    public required string Status { get; set; }
    public required string ResponseJson { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class LocalChange
{
    public long Sequence { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Operation { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

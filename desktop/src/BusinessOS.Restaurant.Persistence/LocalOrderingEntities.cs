namespace BusinessOS.Restaurant.Persistence;

public sealed class LocalPairedTerminal
{
    public required string DeviceId { get; set; }
    public required string TenantId { get; set; }
    public required string DeviceSecretHash { get; set; }
    public required string AccessTokenHash { get; set; }
    public long UserId { get; set; }
    public required string UserPublicId { get; set; }
    public required string UserName { get; set; }
    public required string UserRole { get; set; }
    public DateTimeOffset ValidatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class LocalOrder
{
    public required string Id { get; set; }
    public required string ClientOrderId { get; set; }
    public required string DiningTableId { get; set; }
    public long WaiterId { get; set; }
    public required string WaiterPublicId { get; set; }
    public required string WaiterName { get; set; }
    public required string Status { get; set; }
    public int GuestCount { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ServedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalOrderItem
{
    public required string Id { get; set; }
    public required string OrderId { get; set; }
    public required string MenuItemId { get; set; }
    public required string ClientLineId { get; set; }
    public required string ItemName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalMutation
{
    public required string DeviceId { get; set; }
    public required string MutationId { get; set; }
    public long UserId { get; set; }
    public required string Operation { get; set; }
    public required string RequestHash { get; set; }
    public required string Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public required string ResponseJson { get; set; }
    public DateTimeOffset? ClientOccurredAt { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class LocalChange
{
    public long Sequence { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Operation { get; set; }
    public long? OwnerUserId { get; set; }
    public string? DataJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

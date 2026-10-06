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


public sealed class LocalKitchenTicket
{
    public required string Id { get; set; }
    public required string OrderId { get; set; }
    public required string KitchenStationId { get; set; }
    public long SubmittedByUserId { get; set; }
    public required string TicketNumber { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalKitchenTicketItem
{
    public required string Id { get; set; }
    public required string KitchenTicketId { get; set; }
    public required string OrderItemId { get; set; }
    public required string ItemName { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public required string Status { get; set; }
}

public sealed class LocalKitchenPrinterBinding
{
    public required string KitchenStationId { get; set; }
    public required string PrinterName { get; set; }
    public int Copies { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalPrintJob
{
    public required string Id { get; set; }
    public required string KitchenTicketId { get; set; }
    public required string PrinterName { get; set; }
    public required string DocumentName { get; set; }
    public required string PayloadText { get; set; }
    public int Copies { get; set; } = 1;
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PrintedAtUtc { get; set; }
}


public sealed class LocalCashierSession
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public long CashierUserId { get; set; }
    public required string CashierName { get; set; }
    public required string Status { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? DeclaredCash { get; set; }
    public decimal? CashVariance { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class LocalBill
{
    public required string Id { get; set; }
    public required string OrderId { get; set; }
    public required string BranchId { get; set; }
    public long CreatedByUserId { get; set; }
    public required string BillNumber { get; set; }
    public required string Status { get; set; }
    public decimal Subtotal { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

public sealed class LocalBillLine
{
    public required string Id { get; set; }
    public required string BillId { get; set; }
    public required string OrderItemId { get; set; }
    public required string ItemName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class LocalBillSplit
{
    public required string Id { get; set; }
    public required string BillId { get; set; }
    public int SplitNumber { get; set; }
    public required string Label { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public required string Status { get; set; }
}

public sealed class LocalTenantPayment
{
    public required string Id { get; set; }
    public required string BillId { get; set; }
    public string? BillSplitId { get; set; }
    public required string CashierSessionId { get; set; }
    public long ReceivedByUserId { get; set; }
    public string? ClientPaymentId { get; set; }
    public required string Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class LocalReceiptPrinterSetting
{
    public int Id { get; set; }
    public required string PrinterName { get; set; }
    public int Copies { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalReceiptPrintJob
{
    public required string Id { get; set; }
    public required string BillId { get; set; }
    public required string PrinterName { get; set; }
    public required string DocumentName { get; set; }
    public required string PayloadText { get; set; }
    public int Copies { get; set; } = 1;
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PrintedAtUtc { get; set; }
}

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


public sealed class LocalDailyClosing
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Status { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
}

public sealed class LocalDailyClosingSnapshot
{
    public required string Id { get; set; }
    public required string DailyClosingId { get; set; }
    public int Version { get; set; }
    public long FinalizedByUserId { get; set; }
    public int BillCount { get; set; }
    public int PaymentCount { get; set; }
    public int CashierSessionCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal NetSales { get; set; }
    public decimal PaymentsTotal { get; set; }
    public decimal CashPayments { get; set; }
    public decimal CardPayments { get; set; }
    public decimal BankPayments { get; set; }
    public decimal MobileMoneyPayments { get; set; }
    public decimal OtherPayments { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal DeclaredCash { get; set; }
    public decimal CashVariance { get; set; }
    public DateTimeOffset FinalizedAt { get; set; }
}

public sealed class LocalWaiterShift
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public long UserId { get; set; }
    public required string UserPublicId { get; set; }
    public required string UserName { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int BreakMinutes { get; set; }
    public string? ClosingNote { get; set; }
}

public sealed class LocalAuditEvent
{
    public long Sequence { get; set; }
    public required string EventId { get; set; }
    public required string Category { get; set; }
    public required string EventType { get; set; }
    public long ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public required string ActorRole { get; set; }
    public string? BranchId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}


public sealed class LocalSupplier
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalInventoryItem
{
    public required string Id { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public required string BaseUnit { get; set; }
    public string? PurchaseUnit { get; set; }
    public decimal PurchaseToBaseFactor { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalInventoryBalance
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class LocalInventoryValuation
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal AverageUnitCost { get; set; }
}

public sealed class LocalStockMovement
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string InventoryItemId { get; set; }
    public long ActorUserId { get; set; }
    public required string MovementType { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public required string SourceType { get; set; }
    public required string SourceId { get; set; }
    public string? SourceLineId { get; set; }
    public required string IdempotencyKey { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class LocalRecipe
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string MenuItemId { get; set; }
    public required string Name { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LocalRecipeItem
{
    public required string Id { get; set; }
    public required string RecipeId { get; set; }
    public required string InventoryItemId { get; set; }
    public decimal QuantityBase { get; set; }
}

public sealed class LocalInventoryConsumption
{
    public required string Id { get; set; }
    public required string OrderId { get; set; }
    public required string BranchId { get; set; }
    public long ConsumedByUserId { get; set; }
    public DateTimeOffset ConsumedAt { get; set; }
}

public sealed class LocalInventoryConsumptionLine
{
    public required string Id { get; set; }
    public required string InventoryConsumptionId { get; set; }
    public required string OrderItemId { get; set; }
    public required string RecipeId { get; set; }
    public required string InventoryItemId { get; set; }
    public required string StockMovementId { get; set; }
    public decimal QuantityBase { get; set; }
}

public sealed class LocalPurchaseOrder
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public required string SupplierId { get; set; }
    public long OrderedByUserId { get; set; }
    public required string PoNumber { get; set; }
    public required string Status { get; set; }
    public decimal EstimatedTotal { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class LocalPurchaseOrderLine
{
    public required string Id { get; set; }
    public required string PurchaseOrderId { get; set; }
    public required string InventoryItemId { get; set; }
    public required string ItemName { get; set; }
    public required string PurchaseUnit { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal OrderedPurchaseQuantity { get; set; }
    public decimal OrderedBaseQuantity { get; set; }
    public decimal ReceivedBaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class LocalGoodsReceipt
{
    public required string Id { get; set; }
    public required string PurchaseOrderId { get; set; }
    public required string BranchId { get; set; }
    public required string SupplierId { get; set; }
    public long ReceivedByUserId { get; set; }
    public required string ReceiptNumber { get; set; }
    public string? ClientReceiptId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class LocalGoodsReceiptLine
{
    public required string Id { get; set; }
    public required string GoodsReceiptId { get; set; }
    public required string PurchaseOrderLineId { get; set; }
    public required string InventoryItemId { get; set; }
    public decimal ReceivedPurchaseQuantity { get; set; }
    public decimal ReceivedBaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}



public sealed class LocalExpense
{
    public required string Id { get; set; }
    public required string BranchId { get; set; }
    public long RecordedByUserId { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AFN";
    public required string PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}


public sealed class LocalCloudOutboxMutation
{
    public required string Id { get; set; }
    public required string Operation { get; set; }
    public required string EntityType { get; set; }
    public required string LocalEntityId { get; set; }
    public long ActorUserId { get; set; }
    public required string ActorPublicId { get; set; }
    public required string PayloadJson { get; set; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public string? CloudEntityId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? SyncedAtUtc { get; set; }
}

public sealed class LocalCloudEntityLink
{
    public required string EntityType { get; set; }
    public required string LocalEntityId { get; set; }
    public required string CloudEntityId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalCloudSyncState
{
    public int Id { get; set; }
    public long PullCursor { get; set; }
    public DateTimeOffset? LastPushAtUtc { get; set; }
    public DateTimeOffset? LastPullAtUtc { get; set; }
    public DateTimeOffset? LastSuccessAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class LocalCloudConflict
{
    public required string Id { get; set; }
    public required string MutationId { get; set; }
    public required string Operation { get; set; }
    public required string EntityType { get; set; }
    public required string LocalEntityId { get; set; }
    public required string Code { get; set; }
    public required string Message { get; set; }
    public string? LocalPayloadJson { get; set; }
    public string? CloudPayloadJson { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

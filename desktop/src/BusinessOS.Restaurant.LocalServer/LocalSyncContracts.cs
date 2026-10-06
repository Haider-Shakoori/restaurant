using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalSyncPushRequest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("mutations")] IReadOnlyList<LocalSyncMutationRequest> Mutations);

public sealed record LocalSyncMutationRequest(
    [property: JsonPropertyName("mutation_id")] string MutationId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset? OccurredAt,
    [property: JsonPropertyName("payload")] JsonElement Payload);


public sealed record LocalPrinterBindingRequest(
    [property: JsonPropertyName("printer_name")] string PrinterName,
    [property: JsonPropertyName("copies")] int Copies = 1,
    [property: JsonPropertyName("enabled")] bool Enabled = true);


public sealed record LocalOpenCashierSessionRequest(
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("opening_cash")] decimal OpeningCash);

public sealed record LocalCloseCashierSessionRequest(
    [property: JsonPropertyName("declared_cash")] decimal DeclaredCash);

public sealed record LocalDiscountRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record LocalPaymentBody(
    [property: JsonPropertyName("cashier_session_id")] string CashierSessionId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("client_payment_id")] string? ClientPaymentId,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("bill_split_id")] string? BillSplitId);

public sealed record LocalBillSplitRequest(
    [property: JsonPropertyName("parts")] IReadOnlyList<LocalBillSplitBodyPart> Parts);

public sealed record LocalBillSplitBodyPart(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("amount")] decimal Amount);

public sealed record LocalTransferOrderRequest(
    [property: JsonPropertyName("target_table_id")] string TargetTableId);

public sealed record LocalMergeOrdersRequest(
    [property: JsonPropertyName("source_order_id")] string SourceOrderId);

public sealed record LocalReceiptPrinterRequest(
    [property: JsonPropertyName("printer_name")] string PrinterName,
    [property: JsonPropertyName("copies")] int Copies = 1,
    [property: JsonPropertyName("enabled")] bool Enabled = true);


public sealed record LocalStartShiftRequest(
    [property: JsonPropertyName("branch_id")] string BranchId);

public sealed record LocalEndShiftRequest(
    [property: JsonPropertyName("break_minutes")] int BreakMinutes,
    [property: JsonPropertyName("note")] string? Note);

public sealed record LocalFinalizeDailyClosingRequest(
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("business_date")] DateOnly BusinessDate);

public sealed record LocalReopenDailyClosingRequest(
    [property: JsonPropertyName("reason")] string Reason);

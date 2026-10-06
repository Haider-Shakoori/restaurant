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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Sync;

public sealed record CloudMutationEnvelope(
    [property: JsonPropertyName("mutation_id")] string MutationId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("local_entity_id")] string LocalEntityId,
    [property: JsonPropertyName("actor_public_id")] string ActorPublicId,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record CloudMutationResult(
    [property: JsonPropertyName("mutation_id")] string MutationId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("entity_type")] string? EntityType,
    [property: JsonPropertyName("entity_id")] string? EntityId,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message);

public sealed record CloudPushData(
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("results")] IReadOnlyList<CloudMutationResult> Results,
    [property: JsonPropertyName("pull_cursor")] long PullCursor);

public sealed record CloudPushEnvelope([property: JsonPropertyName("data")] CloudPushData Data);

public sealed record CloudPullChange(
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset? OccurredAt,
    [property: JsonPropertyName("local_links")] IReadOnlyList<string> LocalLinks);

public sealed record CloudPullData(
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("changes")] IReadOnlyList<CloudPullChange> Changes);

public sealed record CloudPullEnvelope([property: JsonPropertyName("data")] CloudPullData Data);

public sealed class CloudReconciliationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public CloudReconciliationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CloudPushData> PushAsync(
        ActivationState activation,
        AuthSession session,
        IReadOnlyList<CloudMutationEnvelope> mutations,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            activation,
            session,
            HttpMethod.Post,
            "api/v1/desktop/reconcile/push");
        request.Content = JsonContent.Create(new { mutations }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<CloudPushEnvelope>(
            JsonOptions,
            cancellationToken);

        return envelope?.Data
            ?? throw new InvalidOperationException("Cloud reconciliation push returned an empty response.");
    }

    public async Task<CloudPullData> PullAsync(
        ActivationState activation,
        AuthSession session,
        long cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            activation,
            session,
            HttpMethod.Get,
            $"api/v1/desktop/reconcile/pull?cursor={Math.Max(0, cursor)}&limit={Math.Clamp(limit, 1, 250)}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<CloudPullEnvelope>(
            JsonOptions,
            cancellationToken);

        return envelope?.Data
            ?? throw new InvalidOperationException("Cloud reconciliation pull returned an empty response.");
    }

    private static HttpRequestMessage CreateRequest(
        ActivationState activation,
        AuthSession session,
        HttpMethod method,
        string relativePath)
    {
        var baseUri = new Uri(activation.TenantBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Device-Id", activation.DeviceId);
        request.Headers.TryAddWithoutValidation("X-Device-Secret", activation.DeviceSecret);
        request.Headers.TryAddWithoutValidation("X-App-Version", "1.0.0");
        return request;
    }
}

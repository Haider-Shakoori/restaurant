using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Sync;

public sealed class CloudOperationalDataClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public CloudOperationalDataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationalSnapshot> FetchBootstrapAsync(
        ActivationState activation,
        AuthSession session,
        CancellationToken cancellationToken = default)
    {
        var baseUri = new Uri(activation.TenantBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "api/v1/sync/bootstrap"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Device-Id", activation.DeviceId);
        request.Headers.TryAddWithoutValidation("X-Device-Secret", activation.DeviceSecret);
        request.Headers.TryAddWithoutValidation("X-App-Version", "1.0.0");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<OperationalBootstrapEnvelope>(
            JsonOptions,
            cancellationToken);

        return envelope?.Data
            ?? throw new InvalidOperationException("The restaurant bootstrap response was empty.");
    }
}

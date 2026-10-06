using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BusinessOS.Restaurant.Authentication;

public sealed class TenantAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public TenantAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResponse> LoginAsync(
        Uri tenantBaseUri,
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(tenantBaseUri, "api/v1/auth/login"))
        {
            Content = JsonContent.Create(new LoginRequest(email.Trim(), password, deviceName), options: JsonOptions),
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, cancellationToken)
            ?? throw new AuthenticationApiException("The authentication server returned an empty response.", true);
    }

    public async Task LogoutAsync(
        Uri tenantBaseUri,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(tenantBaseUri, "api/v1/auth/logout"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }
    }

    private static async Task<AuthenticationApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var message = $"Authentication request failed with status {(int)response.StatusCode}.";

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(messageElement.GetString()))
            {
                message = messageElement.GetString()!;
            }
            else if (root.TryGetProperty("errors", out var errorsElement) &&
                     errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var first = property.Value.EnumerateArray()
                        .FirstOrDefault(item => item.ValueKind == JsonValueKind.String);

                    if (first.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(first.GetString()))
                    {
                        message = first.GetString()!;
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        var retryable =
            response.StatusCode == HttpStatusCode.RequestTimeout ||
            (int)response.StatusCode == 429 ||
            (int)response.StatusCode >= 500;

        return new AuthenticationApiException(message, retryable, response.StatusCode);
    }

    private static Uri BuildUri(Uri tenantBaseUri, string relativePath)
    {
        var normalized = tenantBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? tenantBaseUri
            : new Uri(tenantBaseUri.AbsoluteUri + "/", UriKind.Absolute);

        return new Uri(normalized, relativePath);
    }
}

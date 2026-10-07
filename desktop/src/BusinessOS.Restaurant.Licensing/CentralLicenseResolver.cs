using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.Licensing;

public sealed record CentralLicenseResolution(
    [property: JsonPropertyName("tenant_base_url")] string TenantBaseUrl);

public sealed class CentralLicenseResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public CentralLicenseResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CentralLicenseResolution> ResolveAsync(
        Uri centralBaseUri,
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(centralBaseUri, "api/v1/desktop/license/resolve");
        using var response = await _httpClient.PostAsJsonAsync(
            uri,
            new { license_key = licenseKey.Trim() },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateException(response.StatusCode, body);
        }

        return await response.Content.ReadFromJsonAsync<CentralLicenseResolution>(
            JsonOptions,
            cancellationToken)
            ?? throw new LicenseApiException(
                "The BusinessOS licensing server returned an empty response.",
                true,
                response.StatusCode);
    }

    private static Uri BuildUri(Uri baseUri, string relativePath)
    {
        var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalized, relativePath);
    }

    private static LicenseApiException CreateException(HttpStatusCode statusCode, string body)
    {
        string? message = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            if (message is null &&
                root.TryGetProperty("errors", out var errorsElement) &&
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

                    if (first.ValueKind == JsonValueKind.String)
                    {
                        message = first.GetString();
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return new LicenseApiException(
            message ?? $"Licensing request failed with status {(int)statusCode}.",
            statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500,
            statusCode);
    }
}

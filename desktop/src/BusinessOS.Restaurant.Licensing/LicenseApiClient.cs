using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BusinessOS.Restaurant.Licensing;

public sealed class LicenseApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public LicenseApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<DesktopLicenseResolveResponse> ResolveDesktopLicenseAsync(
        Uri platformBaseUri,
        string licenseKey,
        CancellationToken cancellationToken = default) =>
        PostAsync<DesktopLicenseResolveRequest, DesktopLicenseResolveResponse>(
            BuildUri(platformBaseUri, "api/v1/desktop/license/resolve"),
            new DesktopLicenseResolveRequest(licenseKey.Trim()),
            null,
            cancellationToken);

    public Task<LicensePublicKeyResponse> GetPublicKeyAsync(
        Uri tenantBaseUri,
        CancellationToken cancellationToken = default) =>
        GetAsync<LicensePublicKeyResponse>(
            BuildUri(tenantBaseUri, "api/v1/license/public-key"),
            cancellationToken);

    public Task<LicenseActivationResponse> ActivateAsync(
        Uri tenantBaseUri,
        LicenseActivationRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<LicenseActivationRequest, LicenseActivationResponse>(
            BuildUri(tenantBaseUri, "api/v1/license/activate"),
            request,
            null,
            cancellationToken);

    public Task<LeaseRefreshResponse> RefreshLeaseAsync(
        Uri tenantBaseUri,
        string deviceId,
        string deviceSecret,
        string? appVersion,
        CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Device-Id"] = deviceId,
            ["X-Device-Secret"] = deviceSecret,
        };

        return PostAsync<LeaseRefreshRequest, LeaseRefreshResponse>(
            BuildUri(tenantBaseUri, "api/v1/license/lease"),
            new LeaseRefreshRequest(appVersion),
            headers,
            cancellationToken);
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        Uri uri,
        TRequest body,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw CreateException(response.StatusCode, body);
        }
        catch (LicenseApiException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LicenseApiException(
                "The restaurant licensing server did not respond in time.",
                true,
                null,
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LicenseApiException(
                "The restaurant licensing server could not be reached.",
                true,
                exception.StatusCode,
                exception);
        }
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new LicenseApiException(
                    "The restaurant licensing server returned an empty response.",
                    true,
                    response.StatusCode);
        }
        catch (JsonException exception)
        {
            throw new LicenseApiException(
                "The restaurant licensing server returned an invalid response.",
                true,
                response.StatusCode,
                exception);
        }
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

                    message = property.Value.EnumerateArray()
                        .FirstOrDefault(item => item.ValueKind == JsonValueKind.String)
                        .GetString();

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        message ??= $"Licensing request failed with status {(int)statusCode}.";
        var retryable =
            statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500;

        return new LicenseApiException(message, retryable, statusCode);
    }

    private static Uri BuildUri(Uri tenantBaseUri, string relativePath)
    {
        if (!tenantBaseUri.IsAbsoluteUri ||
            (tenantBaseUri.Scheme != Uri.UriSchemeHttps && tenantBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("A valid HTTP or HTTPS tenant base URL is required.", nameof(tenantBaseUri));
        }

        var normalized = tenantBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? tenantBaseUri
            : new Uri(tenantBaseUri.AbsoluteUri + "/", UriKind.Absolute);

        return new Uri(normalized, relativePath);
    }
}

using System.Net.Http.Headers;
using BusinessOS.Restaurant.Licensing;
using Microsoft.AspNetCore.Http;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalCloudProxy
{
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly HttpClient _httpClient;

    public LocalCloudProxy(
        ConnectionSettingsStore settingsStore,
        HttpClient httpClient)
    {
        _settingsStore = settingsStore;
        _httpClient = httpClient;
    }

    public async Task<IResult> ForwardAsync(
        HttpRequest source,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        if (settings is null)
        {
            return Results.Json(
                new
                {
                    code = "cloud_not_configured",
                    message = "The desktop cloud connection is not configured.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var baseUri = new Uri(settings.TenantBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var target = new Uri(baseUri, relativePath.TrimStart('/'));
        using var request = new HttpRequestMessage(new HttpMethod(source.Method), target);

        if (source.ContentLength is > 0)
        {
            request.Content = new StreamContent(source.Body);

            if (!string.IsNullOrWhiteSpace(source.ContentType))
            {
                request.Content.Headers.ContentType =
                    MediaTypeHeaderValue.Parse(source.ContentType);
            }
        }

        CopyHeader(source, request, "Authorization");
        CopyHeader(source, request, "X-Device-Id");
        CopyHeader(source, request, "X-Device-Secret");
        CopyHeader(source, request, "X-App-Version");

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString()
                ?? "application/json";

            return Results.Content(
                body,
                contentType,
                statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.Json(
                new
                {
                    code = "cloud_unreachable",
                    message = "The cloud restaurant server cannot currently be reached.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Json(
                new
                {
                    code = "cloud_timeout",
                    message = "The cloud restaurant server did not respond in time.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static void CopyHeader(
        HttpRequest source,
        HttpRequestMessage destination,
        string name)
    {
        if (source.Headers.TryGetValue(name, out var value))
        {
            destination.Headers.TryAddWithoutValidation(name, value.ToArray());
        }
    }
}

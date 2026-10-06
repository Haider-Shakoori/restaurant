using System.Net;

namespace BusinessOS.Restaurant.Licensing;

public sealed class LicenseApiException : Exception
{
    public LicenseApiException(
        string message,
        bool isRetryable,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
    }

    public bool IsRetryable { get; }

    public HttpStatusCode? StatusCode { get; }
}

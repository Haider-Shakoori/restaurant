using System.Net;

namespace BusinessOS.Restaurant.Authentication;

public sealed class AuthenticationApiException : Exception
{
    public AuthenticationApiException(
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

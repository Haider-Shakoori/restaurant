namespace BusinessOS.Restaurant.Authentication;

public sealed class AuthenticationService
{
    private readonly TenantAuthClient _client;
    private readonly WindowsSessionStore _store;

    public AuthenticationService(TenantAuthClient client, WindowsSessionStore store)
    {
        _client = client;
        _store = store;
    }

    public async Task<AuthSession> LoginAsync(
        Uri tenantBaseUri,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.LoginAsync(
            tenantBaseUri,
            email,
            password,
            Environment.MachineName,
            cancellationToken);

        var session = new AuthSession(
            tenantBaseUri.AbsoluteUri.TrimEnd('/'),
            response.AccessToken,
            response.User,
            response.TenantId,
            DateTimeOffset.UtcNow);

        await _store.SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task LogoutAsync(
        AuthSession? session,
        CancellationToken cancellationToken = default)
    {
        if (session is not null)
        {
            try
            {
                await _client.LogoutAsync(
                    new Uri(session.TenantBaseUrl, UriKind.Absolute),
                    session.AccessToken,
                    cancellationToken);
            }
            catch (AuthenticationApiException exception) when (exception.IsRetryable)
            {
                // Local logout must still succeed when the restaurant is temporarily offline.
            }
        }

        await _store.ClearAsync();
    }
}

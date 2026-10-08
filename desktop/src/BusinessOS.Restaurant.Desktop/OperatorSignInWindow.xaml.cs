using System.Net.Http;
using System.Windows;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop.Appearance;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop;

public partial class OperatorSignInWindow : Window
{
    private readonly string _licensedTenantId;
    private readonly WindowsSessionStore _sessions = new();
    private readonly ConnectionSettingsStore _connections = new();
    private bool _busy;

    public AuthSession? SignedInSession { get; private set; }

    public OperatorSignInWindow(string licensedTenantId, string? tenantBaseUrl)
    {
        _licensedTenantId = licensedTenantId;
        InitializeComponent();
        TenantUrlBox.Text = tenantBaseUrl ?? string.Empty;
        RestaurantBackdrop.Visibility = ThemeManager.Current == AppearanceTheme.Glass
            ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => (string.IsNullOrWhiteSpace(TenantUrlBox.Text)
            ? TenantUrlBox : EmailBox).Focus();
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (!Uri.TryCreate(TenantUrlBox.Text.Trim(), UriKind.Absolute, out var tenantUri) ||
            (tenantUri.Scheme != Uri.UriSchemeHttps &&
             !(tenantUri.Scheme == Uri.UriSchemeHttp && tenantUri.IsLoopback)))
        {
            StatusText.Text = "Enter a valid HTTPS restaurant tenant URL.";
            return;
        }

        var email = EmailBox.Text.Trim();
        var password = PasswordInput.Password;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "Enter the operator email and password.";
            return;
        }

        _busy = true;
        SignInButton.IsEnabled = false;
        StatusText.Text = "Verifying operator and restaurant license…";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var response = await new TenantAuthClient(http)
                .LoginAsync(tenantUri, email, password, Environment.MachineName);

            // A valid cloud user from another restaurant must never inherit this
            // computer's local license, stock, tables or offline access.
            if (!string.Equals(response.TenantId, _licensedTenantId, StringComparison.Ordinal))
                throw new InvalidOperationException("This account belongs to a different licensed restaurant.");

            if (string.IsNullOrEmpty(RestaurantWorkspaceRoutes.DefaultRoute(response.User.Role)))
                throw new InvalidOperationException("This operator's role is not enabled for Restaurant Desktop.");

            var normalizedUrl = tenantUri.AbsoluteUri.TrimEnd('/');
            var settings = await _connections.LoadAsync();
            await _connections.SaveAsync(settings is null
                ? new ConnectionSettings(normalizedUrl)
                : settings with { TenantBaseUrl = normalizedUrl });

            var session = new AuthSession(normalizedUrl, response.AccessToken,
                response.User, response.TenantId, DateTimeOffset.UtcNow);
            await _sessions.SaveAsync(session);
            SignedInSession = session;
            PasswordInput.Clear();
            DialogResult = true;
        }
        catch (AuthenticationApiException exception)
        {
            StatusText.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "Could not reach the restaurant server. A first sign-in needs internet.";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "Sign-in timed out. Check connectivity and try again.";
        }
        catch (Exception exception)
        {
            App.LogRecoverableException("operator-sign-in", exception);
            StatusText.Text = "Sign-in was not completed. " + exception.Message;
        }
        finally
        {
            PasswordInput.Clear();
            _busy = false;
            SignInButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_busy) DialogResult = false;
    }
}

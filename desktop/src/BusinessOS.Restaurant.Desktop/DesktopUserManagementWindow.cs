using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BusinessOS.Restaurant.Authentication;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Online management of cloud-owned restaurant identities. Never manufactures local
/// login credentials and never stores entered passwords in SQLite or diagnostics.
/// </summary>
internal sealed class DesktopUserManagementWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] AllowedRoles =
        ["owner", "admin", "manager", "cashier", "waiter", "kitchen", "inventory"];

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
    private readonly AuthSession _session;
    private readonly Uri _endpoint;
    private readonly DataGrid _users = new() { IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 210 };
    private readonly TextBox _name = new() { Height = 34 };
    private readonly TextBox _email = new() { Height = 34 };
    private readonly TextBox _phone = new() { Height = 34 };
    private readonly ComboBox _role = new() { Height = 34, ItemsSource = AllowedRoles, SelectedIndex = 3 };
    private readonly CheckBox _active = new() { Content = "Account active", IsChecked = true, Margin = new Thickness(0, 10, 0, 6) };
    private readonly PasswordBox _password = new() { Height = 34 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Button _save = new() { Content = "Create account", MinWidth = 170, Height = 36 };
    private DesktopStaffAccount? _selected;

    public DesktopUserManagementWindow(AuthSession session)
    {
        if (session.User.Role.Trim().ToLowerInvariant() is not ("owner" or "admin"))
            throw new UnauthorizedAccessException("Only owners and administrators can manage restaurant users.");

        if (!Uri.TryCreate(session.TenantBaseUrl, UriKind.Absolute, out var tenantUri) ||
            tenantUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A secure HTTPS tenant endpoint is required to manage users.");

        _session = session;
        _endpoint = new Uri(tenantUri.GetLeftPart(UriPartial.Authority) + "/api/v1/desktop/users");
        Title = "Restaurant · Users & Roles";
        Width = 860;
        Height = 730;
        MinWidth = 610;
        MinHeight = 530;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _users.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(DesktopStaffAccount.Name)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridTextColumn { Header = "Email / login", Binding = new Binding(nameof(DesktopStaffAccount.Email)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Binding(nameof(DesktopStaffAccount.Role)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridCheckBoxColumn { Header = "Active", Binding = new Binding(nameof(DesktopStaffAccount.IsActive)), Width = 75 });
        _users.SelectionChanged += (_, _) =>
        {
            if (_users.SelectedItem is DesktopStaffAccount user)
                SelectUser(user);
        };

        var root = new DockPanel { Margin = new Thickness(20) };
        var actionBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var add = new Button { Content = "+ New user", Height = 36, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => ClearForm();
        var reload = new Button { Content = "Refresh from server", Height = 36, MinWidth = 155 };
        reload.Click += async (_, _) => await RefreshAsync();
        actionBar.Children.Add(add);
        actionBar.Children.Add(reload);
        DockPanel.SetDock(actionBar, Dock.Top);
        root.Children.Add(actionBar);

        var info = new TextBlock
        {
            Text = "Cloud-managed staff accounts. Creation, password resets and role changes require Internet. " +
                   "Existing valid offline operator sessions remain subject to the restaurant offline policy.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Restaurant staff", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(_users);
        content.Children.Add(new TextBlock { Text = "Account details", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
        AddField(content, "Full name", _name);
        AddField(content, "Email / login", _email);
        AddField(content, "Phone (optional)", _phone);
        AddField(content, "Restaurant role", _role);
        content.Children.Add(_active);
        AddField(content, "Password (required for new users; leave blank on edit to keep unchanged)", _password);
        _save.Click += async (_, _) => await SaveAsync();
        content.Children.Add(_save);
        content.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _http.Dispose();
    }

    private static void AddField(Panel panel, string label, FrameworkElement input)
    {
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(input);
    }

    private void ClearForm()
    {
        _selected = null;
        _users.SelectedItem = null;
        _name.Clear();
        _email.Clear();
        _phone.Clear();
        _password.Clear();
        _role.SelectedIndex = 3;
        _active.IsChecked = true;
        _active.IsEnabled = false; // New accounts are always active.
        _save.Content = "Create account";
        _status.Text = "";
    }

    private void SelectUser(DesktopStaffAccount user)
    {
        _selected = user;
        _name.Text = user.Name;
        _email.Text = user.Email;
        _phone.Text = user.Phone ?? "";
        _role.SelectedItem = user.Role;
        _active.IsEnabled = true;
        _active.IsChecked = user.IsActive;
        _password.Clear();
        _save.Content = "Save account changes";
        _status.Text = "";
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var request = NewRequest(HttpMethod.Get, _endpoint);
            using var response = await _http.SendAsync(request);
            await CheckResponseAsync(response);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            _users.ItemsSource = document.RootElement.GetProperty("data")
                .Deserialize<DesktopStaffAccount[]>(JsonOptions) ?? [];
            _status.Text = "Staff list refreshed from tenant server.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not load tenant users: " + ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        _save.IsEnabled = false;
        try
        {
            var password = _password.Password;
            if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_email.Text) ||
                _role.SelectedItem is not string role)
                throw new InvalidOperationException("Enter a name, email and role.");
            if (_selected is null && password.Length < 8)
                throw new InvalidOperationException("New accounts require a password of at least eight characters.");
            if (password.Length is > 0 and < 8)
                throw new InvalidOperationException("New passwords must contain at least eight characters.");

            var payload = new Dictionary<string, object?>
            {
                ["name"] = _name.Text.Trim(),
                ["email"] = _email.Text.Trim(),
                ["phone"] = string.IsNullOrWhiteSpace(_phone.Text) ? null : _phone.Text.Trim(),
                ["role"] = role,
            };
            if (_selected is not null) payload["is_active"] = _active.IsChecked == true;
            if (password.Length > 0) payload["password"] = password;

            var uri = _selected is null ? _endpoint : new Uri(_endpoint.AbsoluteUri.TrimEnd('/') + "/" + _selected.Id);
            using var request = NewRequest(_selected is null ? HttpMethod.Post : HttpMethod.Patch, uri);
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.SendAsync(request);
            await CheckResponseAsync(response);
            _password.Clear();
            ClearForm();
            await RefreshAsync();
            _status.Text = "Account saved. Role/password changes may revoke that user's previous server sessions.";
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Restaurant account saved in tenant server.");
        }
        catch (Exception ex)
        {
            _password.Clear();
            _status.Text = "Account not saved: " + ex.Message;
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            _save.IsEnabled = true;
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = $"Server rejected the request (HTTP {(int)response.StatusCode}).";
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.TryGetProperty("message", out var serverMessage) &&
                serverMessage.ValueKind == JsonValueKind.String)
                message = serverMessage.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }

    private sealed record DesktopStaffAccount(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("is_active")] bool IsActive);
}

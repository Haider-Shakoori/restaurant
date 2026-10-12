using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using BusinessOS.Restaurant.Authentication;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Online management of cloud-owned restaurant identities, roles and
/// permissions. Never manufactures local login credentials and never stores
/// entered passwords in SQLite or diagnostics. Role and permission changes are
/// enforced by the tenant authorization layer; this window only surfaces them.
/// </summary>
internal sealed class DesktopUserManagementWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
    private readonly AuthSession _session;
    private readonly Uri _authority;
    private readonly bool _isOwner;

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 2) };

    // Users tab
    private readonly DataGrid _users = new() { IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 220 };
    private readonly TextBox _name = new() { Height = 34 };
    private readonly TextBox _email = new() { Height = 34 };
    private readonly TextBox _phone = new() { Height = 34 };
    private readonly ComboBox _role = new() { Height = 34 };
    private readonly CheckBox _active = new() { Content = "Account active", Margin = new Thickness(0, 10, 0, 6) };
    private readonly PasswordBox _password = new() { Height = 34 };
    private readonly Button _saveUser = new() { Content = "Create account", MinWidth = 170, Height = 36 };
    private DesktopStaffAccount? _selectedUser;

    // Roles tab
    private readonly TextBox _roleSearch = new() { Height = 32, MinWidth = 200 };
    private readonly ComboBox _roleFilter = new() { Height = 32, MinWidth = 120 };
    private readonly DataGrid _rolesGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 240 };
    private readonly TextBox _roleName = new() { Height = 34 };
    private readonly TextBox _roleDescription = new() { Height = 34 };
    private readonly TextBlock _roleInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _saveRole = new() { Content = "Create role", MinWidth = 150, Height = 36 };
    private readonly Button _newRole = new() { Content = "+ New role", MinWidth = 120, Height = 34 };
    private readonly Button _deleteRole = new() { Content = "Delete role", MinWidth = 120, Height = 34 };
    private RoleDto? _selectedRole;
    private List<RoleDto> _allRoles = [];
    private bool _suppressRoleEvents;

    // Permissions tab
    private readonly ComboBox _permissionRole = new() { Height = 34, MinWidth = 220 };
    private readonly StackPanel _matrix = new();
    private readonly TextBlock _permissionSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) };
    private readonly TextBlock _permissionHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
    private readonly Button _savePermissions = new() { Content = "Save permissions", MinWidth = 170, Height = 38 };
    private readonly HashSet<string> _draftPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _permissionCheckBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CheckBox>> _groupPermissionBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _groupCountLabels = new(StringComparer.Ordinal);
    private PermissionGroupDto[] _catalogGroups = [];
    private string[] _protectedPermissions = [];
    private bool _syncingPermissionCheckBoxes;

    public bool SavedChanges { get; private set; }

    public DesktopUserManagementWindow(AuthSession session)
    {
        var operatorRole = session.User.Role.Trim().ToLowerInvariant();
        if (operatorRole is not ("owner" or "admin"))
            throw new UnauthorizedAccessException("Only owners and administrators can manage restaurant users and roles.");

        if (!Uri.TryCreate(session.TenantBaseUrl, UriKind.Absolute, out var tenantUri) ||
            tenantUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A secure HTTPS tenant endpoint is required to manage users and roles.");

        _session = session;
        _isOwner = operatorRole == "owner";
        _authority = new Uri(tenantUri.GetLeftPart(UriPartial.Authority));
        Title = "Restaurant · Users & Roles";
        Width = 1140;
        Height = 790;
        MinWidth = 780;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tabs = new TabControl
        {
            Margin = new Thickness(0, 6, 0, 0),
            Items =
            {
                Tab("Users", BuildUsersContent(), "♙"),
                Tab("Roles", BuildRolesContent(), "♜"),
                Tab("Permissions", BuildPermissionsContent(), "☑"),
            },
        };

        var header = new StackPanel { Margin = new Thickness(6, 2, 6, 0) };
        header.Children.Add(new TextBlock { Text = "Users, Roles & Permissions", FontSize = 22, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "Cloud-managed staff accounts and tenant roles. Membership, role changes and password resets require the tenant server. " +
                   "Existing valid offline operator sessions remain subject to the restaurant offline policy.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4),
        });

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(tabs);
        Content = root;

        Loaded += async (_, _) => await RefreshAllAsync();
        Closed += (_, _) => _http.Dispose();
    }

    private static TabItem Tab(string title, FrameworkElement content, string icon)
    {
        var tab = new TabItem { Header = $"{icon}  {title}", Content = content };
        tab.SetResourceReference(FrameworkElement.StyleProperty, "RestaurantSettingsTabItemStyle");
        return tab;
    }

    // ---------------------------------------------------------------- Users

    private FrameworkElement BuildUsersContent()
    {
        _users.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(DesktopStaffAccount.Name)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridTextColumn { Header = "Email / login", Binding = new Binding(nameof(DesktopStaffAccount.Email)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Binding(nameof(DesktopStaffAccount.RoleDisplay)), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
        _users.Columns.Add(new DataGridCheckBoxColumn { Header = "Active", Binding = new Binding(nameof(DesktopStaffAccount.IsActive)), Width = 75 });
        _users.SelectionChanged += (_, _) =>
        {
            if (_users.SelectedItem is DesktopStaffAccount user)
                SelectUser(user);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        var list = new DockPanel { Margin = new Thickness(6, 12, 18, 6) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var add = new Button { Content = "+ New user", Height = 34, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => ClearUserForm();
        var reload = new Button { Content = "Refresh from server", Height = 34, MinWidth = 155 };
        reload.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        reload.Click += async (_, _) => await GuardAsync(reload, "Users refreshed from tenant server.", RefreshUsersAsync);
        AutomationProperties.SetName(add, "New user");
        actions.Children.Add(add);
        actions.Children.Add(reload);
        DockPanel.SetDock(actions, Dock.Top);
        list.Children.Add(actions);
        list.Children.Add(_users);
        grid.Children.Add(list);

        var form = new StackPanel { Margin = new Thickness(12, 12, 6, 12) };
        form.Children.Add(SectionHeading("Account details"));
        AddField(form, "Full name", _name);
        AddField(form, "Email / login", _email);
        AddField(form, "Phone (optional)", _phone);
        AddField(form, "Restaurant role", _role);
        form.Children.Add(new TextBlock
        {
            Text = "Roles come from the tenant role catalog. Assign a custom role after creating it in the Roles tab.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        form.Children.Add(_active);
        AddField(form, "Password (required for new users; leave blank on edit to keep unchanged)", _password);
        _saveUser.Click += async (_, _) => await SaveUserAsync();
        form.Children.Add(_saveUser);
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        return grid;
    }

    private void ClearUserForm()
    {
        _selectedUser = null;
        _users.SelectedItem = null;
        _name.Clear();
        _email.Clear();
        _phone.Clear();
        _password.Clear();
        _active.IsChecked = true;
        _active.IsEnabled = false; // New accounts are always active.
        _saveUser.Content = "Create account";
        SetStatus("");
    }

    private void SelectUser(DesktopStaffAccount user)
    {
        _selectedUser = user;
        _name.Text = user.Name;
        _email.Text = user.Email;
        _phone.Text = user.Phone ?? "";
        _role.SelectedItem = (_role.ItemsSource as IEnumerable<RoleOption>)?.FirstOrDefault(x => x.Key == user.Role);
        _active.IsEnabled = true;
        _active.IsChecked = user.IsActive;
        _password.Clear();
        _saveUser.Content = "Save account changes";
    }

    private async Task RefreshUsersAsync()
    {
        using var request = NewRequest(HttpMethod.Get, Endpoint("users"));
        using var response = await _http.SendAsync(request);
        var root = await ReadAsync(response);
        _users.ItemsSource = root.GetProperty("data").Deserialize<DesktopStaffAccount[]>(JsonOptions) ?? [];
    }

    private async Task SaveUserAsync()
    {
        _saveUser.IsEnabled = false;
        try
        {
            var password = _password.Password;
            if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_email.Text) ||
                _role.SelectedItem is not RoleOption role)
                throw new InvalidOperationException("Enter a name, email and role.");
            if (_selectedUser is null && password.Length < 8)
                throw new InvalidOperationException("New accounts require a password of at least eight characters.");
            if (password.Length is > 0 and < 8)
                throw new InvalidOperationException("New passwords must contain at least eight characters.");

            var payload = new Dictionary<string, object?>
            {
                ["name"] = _name.Text.Trim(),
                ["email"] = _email.Text.Trim(),
                ["phone"] = string.IsNullOrWhiteSpace(_phone.Text) ? null : _phone.Text.Trim(),
                ["role"] = role.Key,
            };
            if (_selectedUser is not null) payload["is_active"] = _active.IsChecked == true;
            if (password.Length > 0) payload["password"] = password;

            var endpoint = _selectedUser is null ? Endpoint("users") : Endpoint("users/" + _selectedUser.Id);
            using var request = NewRequest(_selectedUser is null ? HttpMethod.Post : HttpMethod.Patch, endpoint);
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.SendAsync(request);
            await ReadAsync(response);

            SavedChanges = true;
            _password.Clear();
            ClearUserForm();
            await RefreshUsersAsync();
            SetStatus("Account saved. Role/password changes may revoke that user's previous server sessions.");
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Restaurant account saved in tenant server.");
        }
        catch (Exception ex)
        {
            _password.Clear();
            SetStatus("Account not saved: " + ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            _saveUser.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- Roles

    private FrameworkElement BuildRolesContent()
    {
        _rolesGrid.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Binding(nameof(RoleDto.Name)), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
        _rolesGrid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Binding(nameof(RoleDto.Key)), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
        _rolesGrid.Columns.Add(new DataGridTextColumn { Header = "Users", Binding = new Binding(nameof(RoleDto.UserCount)), Width = 70 });
        _rolesGrid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(RoleDto.TypeLabel)), Width = 90 });
        _rolesGrid.Columns.Add(new DataGridTextColumn { Header = "Permissions", Binding = new Binding(nameof(RoleDto.PermissionCount)), Width = 100 });
        _rolesGrid.SelectionChanged += (_, _) =>
        {
            if (_suppressRoleEvents) return;
            if (_rolesGrid.SelectedItem is RoleDto role)
                SelectRole(role);
        };

        _roleFilter.ItemsSource = new[] { "All roles", "System", "Custom" };
        _roleFilter.SelectedIndex = 0;
        _roleFilter.SelectionChanged += (_, _) => ApplyRoleFilter();
        _roleSearch.TextChanged += (_, _) => ApplyRoleFilter();
        AutomationProperties.SetName(_roleSearch, "Search roles");
        _roleSearch.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");

        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _roleSearch.Width = 230;
        _roleSearch.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(_roleSearch);
        tools.Children.Add(_roleFilter);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        var list = new DockPanel { Margin = new Thickness(6, 12, 18, 6) };
        DockPanel.SetDock(tools, Dock.Top);
        list.Children.Add(tools);
        list.Children.Add(_rolesGrid);
        grid.Children.Add(list);

        var editor = new StackPanel { Margin = new Thickness(12, 12, 6, 12) };
        editor.Children.Add(SectionHeading("Role details"));
        AddField(editor, "Role name", _roleName);
        AddField(editor, "Description (optional)", _roleDescription);
        _roleInfo.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        editor.Children.Add(_roleInfo);
        _saveRole.Click += async (_, _) => await SaveRoleAsync();
        _newRole.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        _newRole.Click += (_, _) => ClearRoleEditor();
        _newRole.Margin = new Thickness(0, 0, 8, 8);
        _deleteRole.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        _deleteRole.Click += async (_, _) => await DeleteRoleAsync();
        var roleActions = new WrapPanel();
        roleActions.Children.Add(_saveRole);
        roleActions.Children.Add(_newRole);
        roleActions.Children.Add(_deleteRole);
        editor.Children.Add(roleActions);
        editor.Children.Add(new TextBlock
        {
            Text = "System roles cannot be deleted and the Super Admin role is fully protected. " +
                   "A role can only be deleted after every assigned user is moved to another role.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
        var scroller = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        return grid;
    }

    private void ClearRoleEditor()
    {
        _suppressRoleEvents = true;
        _rolesGrid.SelectedItem = null;
        _suppressRoleEvents = false;
        _selectedRole = null;
        _roleName.Clear();
        _roleDescription.Clear();
        _saveRole.Content = "Create role";
        _draftPermissions.Clear();
        SyncPermissionCheckBoxes();
        ApplyRoleEditorState();
    }

    private void SelectRole(RoleDto role)
    {
        _selectedRole = role;
        _roleName.Text = role.Name;
        _roleDescription.Text = role.Description ?? "";
        _saveRole.Content = "Save role";
        _syncingPermissionCheckBoxes = true;
        try
        {
            _draftPermissions.Clear();
            foreach (var permission in role.Permissions ?? [])
                _draftPermissions.Add(permission);
        }
        finally
        {
            _syncingPermissionCheckBoxes = false;
        }

        _suppressRoleEvents = true;
        _permissionRole.SelectedItem = _allRoles.FirstOrDefault(x => x.Id == role.Id);
        _suppressRoleEvents = false;
        SyncPermissionCheckBoxes();
        ApplyRoleEditorState();
    }

    private async Task RefreshRolesAsync()
    {
        using var request = NewRequest(HttpMethod.Get, Endpoint("roles"));
        using var response = await _http.SendAsync(request);
        var root = await ReadAsync(response);
        _allRoles = (root.GetProperty("data").Deserialize<RoleDto[]>(JsonOptions) ?? []).ToList();

        if (root.TryGetProperty("catalog", out var catalogElement))
        {
            var catalog = catalogElement.Deserialize<CatalogDto>(JsonOptions);
            if (catalog is not null)
            {
                _catalogGroups = catalog.Groups ?? [];
                _protectedPermissions = catalog.Protected ?? [];
                BuildPermissionMatrix();
            }
        }

        UpdateRoleChoices();
        ApplyRoleFilter();

        if (_selectedRole is not null)
        {
            var refreshed = _allRoles.FirstOrDefault(x => x.Id == _selectedRole.Id);
            if (refreshed is null)
            {
                ClearRoleEditor();
            }
            else
            {
                var preserveDraft = HasUnsavedPermissionChanges() ? new HashSet<string>(_draftPermissions, StringComparer.Ordinal) : null;
                SelectRole(refreshed);
                if (preserveDraft is not null)
                {
                    _draftPermissions.Clear();
                    foreach (var permission in preserveDraft)
                        _draftPermissions.Add(permission);
                    SyncPermissionCheckBoxes();
                    UpdatePermissionDraftStatus();
                }
            }
        }

        ApplyRoleEditorState();
    }

    private void UpdateRoleChoices()
    {
        var previousUserRole = (_role.SelectedItem as RoleOption)?.Key;
        var options = _allRoles
            .Select(r => new RoleOption(r.Key, r.IsSystem ? r.Name : r.Name + " · custom"))
            .ToArray();
        _role.DisplayMemberPath = nameof(RoleOption.Label);
        _role.ItemsSource = options;
        _role.SelectedItem = options.FirstOrDefault(x => x.Key == previousUserRole) ?? options.FirstOrDefault();

        var previousPermissionRoleId = (_permissionRole.SelectedItem as RoleDto)?.Id;
        _permissionRole.ItemsSource = _allRoles;
        _permissionRole.DisplayMemberPath = nameof(RoleDto.Name);
        _permissionRole.SelectedItem = _allRoles.FirstOrDefault(x => x.Id == previousPermissionRoleId)
            ?? _allRoles.FirstOrDefault(x => x.Id == _selectedRole?.Id);
    }

    private void ApplyRoleFilter()
    {
        var query = _roleSearch.Text.Trim();
        var filter = _roleFilter.SelectedItem as string ?? "All roles";
        var filtered = _allRoles
            .Where(r => filter switch
            {
                "System" => r.IsSystem,
                "Custom" => !r.IsSystem,
                _ => true,
            })
            .Where(r => query.Length == 0 ||
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _suppressRoleEvents = true;
        _rolesGrid.ItemsSource = filtered;
        if (_selectedRole is not null)
            _rolesGrid.SelectedItem = filtered.FirstOrDefault(x => x.Id == _selectedRole.Id);
        _suppressRoleEvents = false;
    }

    private async Task SaveRoleAsync()
    {
        if (_selectedRole is null)
        {
            await CreateRoleAsync();
            return;
        }

        _saveRole.IsEnabled = false;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = _roleName.Text.Trim(),
                ["description"] = string.IsNullOrWhiteSpace(_roleDescription.Text) ? null : _roleDescription.Text.Trim(),
            };
            using var request = NewRequest(HttpMethod.Patch, Endpoint("roles/" + _selectedRole.Id));
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.SendAsync(request);
            var root = await ReadAsync(response);
            var updated = root.GetProperty("data").Deserialize<RoleDto>(JsonOptions);
            SavedChanges = true;
            await RefreshRolesAsync();
            SetStatus("Role details saved.");
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Role details saved in tenant server.");
        }
        catch (Exception ex)
        {
            SetStatus("Role not saved: " + ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            ApplyRoleEditorState();
        }
    }

    private async Task CreateRoleAsync()
    {
        _saveRole.IsEnabled = false;
        try
        {
            var name = _roleName.Text.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Enter a role name before creating the role.");

            var payload = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["description"] = string.IsNullOrWhiteSpace(_roleDescription.Text) ? null : _roleDescription.Text.Trim(),
                ["permissions"] = Array.Empty<string>(),
            };
            using var request = NewRequest(HttpMethod.Post, Endpoint("roles"));
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.SendAsync(request);
            var root = await ReadAsync(response);
            var created = root.GetProperty("data").Deserialize<RoleDto>(JsonOptions);
            SavedChanges = true;
            await RefreshRolesAsync();
            if (created is not null)
            {
                var fresh = _allRoles.FirstOrDefault(x => x.Id == created.Id);
                if (fresh is not null) SelectRole(fresh);
            }
            SetStatus("Role created. Use the Permissions tab to grant access.");
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Custom role created.");
        }
        catch (Exception ex)
        {
            SetStatus("Role not created: " + ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            ApplyRoleEditorState();
        }
    }

    private async Task DeleteRoleAsync()
    {
        var role = _selectedRole;
        if (role is null || role.IsSystem) return;

        if (!DesktopGlassDialog.Confirm(
                $"Delete the custom role \"{role.Name}\"? Users must be reassigned before a role can be deleted.",
                "Delete role", this))
            return;

        _deleteRole.IsEnabled = false;
        try
        {
            using var request = NewRequest(HttpMethod.Delete, Endpoint("roles/" + role.Id));
            using var response = await _http.SendAsync(request);
            await ReadAsync(response);
            SavedChanges = true;
            ClearRoleEditor();
            await RefreshRolesAsync();
            SetStatus("Role deleted.");
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Custom role deleted.");
        }
        catch (Exception ex)
        {
            SetStatus("Role not deleted: " + ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            ApplyRoleEditorState();
        }
    }

    // ---------------------------------------------------------- Permissions

    private FrameworkElement BuildPermissionsContent()
    {
        var header = new WrapPanel { Margin = new Thickness(6, 12, 6, 4) };
        header.Children.Add(new TextBlock { Text = "Role", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _permissionRole.SelectionChanged += (_, _) =>
        {
            if (_suppressRoleEvents) return;
            if (_permissionRole.SelectedItem is RoleDto role)
                SelectRole(role);
        };
        header.Children.Add(_permissionRole);
        var reload = new Button { Content = "Reload from server", Height = 34, MinWidth = 155, Margin = new Thickness(12, 0, 0, 0) };
        reload.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        reload.Click += async (_, _) => await GuardAsync(reload, "Roles and permissions refreshed from tenant server.", RefreshRolesAsync);
        header.Children.Add(reload);

        var panel = new DockPanel { Margin = new Thickness(6, 0, 6, 6) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        _permissionSummary.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(_permissionSummary, Dock.Top);
        panel.Children.Add(_permissionSummary);
        _permissionHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        DockPanel.SetDock(_permissionHint, Dock.Top);
        panel.Children.Add(_permissionHint);

        var footer = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        _savePermissions.Click += async (_, _) => await SavePermissionsAsync();
        AutomationProperties.SetName(_savePermissions, "Save permission changes");
        footer.Children.Add(_savePermissions);
        var explain = new TextBlock
        {
            Text = "Protected permissions can only be changed by the restaurant owner. " +
                   "Checkboxes marked 🔒 are locked for non-owner administrators.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        explain.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer
        {
            Content = _matrix,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 6, 0, 6),
        });
        return panel;
    }

    private void BuildPermissionMatrix()
    {
        _matrix.Children.Clear();
        _permissionCheckBoxes.Clear();
        _groupPermissionBoxes.Clear();
        _groupCountLabels.Clear();

        if (_catalogGroups.Length == 0)
        {
            _matrix.Children.Add(new TextBlock { Text = "Permission catalog is unavailable. Use Reload from server." });
            return;
        }

        foreach (var group in _catalogGroups)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            var content = new StackPanel();
            var headingRow = new DockPanel();
            var title = new TextBlock
            {
                Text = $"{GroupIcon(group.Key)}  {group.Label}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            DockPanel.SetDock(title, Dock.Left);
            headingRow.Children.Add(title);
            var count = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            count.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
            headingRow.Children.Add(count);
            content.Children.Add(headingRow);

            var moduleActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            var selectAll = new Button { Content = "Select all", Height = 28, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 2, 10, 2) };
            selectAll.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            AutomationProperties.SetName(selectAll, $"Select all {group.Label} permissions");
            var clearAll = new Button { Content = "Clear all", Height = 28, MinWidth = 88, Padding = new Thickness(10, 2, 10, 2) };
            clearAll.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            AutomationProperties.SetName(clearAll, $"Clear all {group.Label} permissions");
            selectAll.Click += (_, _) => SetGroupPermissions(group, true);
            clearAll.Click += (_, _) => SetGroupPermissions(group, false);
            moduleActions.Children.Add(selectAll);
            moduleActions.Children.Add(clearAll);
            content.Children.Add(moduleActions);

            var boxes = new WrapPanel();
            var groupBoxes = new List<CheckBox>();

            foreach (var permission in group.Permissions)
            {
                var check = new CheckBox
                {
                    Content = permission.Protected ? permission.Label + "  🔒" : permission.Label,
                    Margin = new Thickness(0, 4, 22, 4),
                    Tag = permission,
                    ToolTip = permission.Protected
                        ? "Protected permission: only the restaurant owner can grant or revoke it."
                        : null,
                };
                var key = permission.Key;
                check.Checked += (_, _) => OnPermissionToggled(key, true);
                check.Unchecked += (_, _) => OnPermissionToggled(key, false);
                AutomationProperties.SetName(check, permission.Label);
                _permissionCheckBoxes[key] = check;
                groupBoxes.Add(check);
                boxes.Children.Add(check);
            }

            _groupPermissionBoxes[group.Key] = groupBoxes;
            _groupCountLabels[group.Key] = count;
            content.Children.Add(boxes);
            card.Child = content;
            _matrix.Children.Add(card);
        }

        SyncPermissionCheckBoxes();
        ApplyRoleEditorState();
    }

    private void OnPermissionToggled(string key, bool selected)
    {
        if (_syncingPermissionCheckBoxes) return;
        if (selected) _draftPermissions.Add(key);
        else _draftPermissions.Remove(key);
        UpdatePermissionCounts();
        UpdatePermissionDraftStatus();
    }

    private void SetGroupPermissions(PermissionGroupDto group, bool selected)
    {
        if (_selectedRole is null)
        {
            SetStatus("Select a role before changing permissions.");
            return;
        }

        if (!_groupPermissionBoxes.TryGetValue(group.Key, out var boxes)) return;

        _syncingPermissionCheckBoxes = true;
        try
        {
            foreach (var box in boxes)
                if (box.IsEnabled)
                    box.IsChecked = selected;
        }
        finally
        {
            _syncingPermissionCheckBoxes = false;
        }

        foreach (var permission in group.Permissions)
        {
            if (!_permissionCheckBoxes.TryGetValue(permission.Key, out var box) || !box.IsEnabled) continue;
            if (selected) _draftPermissions.Add(permission.Key);
            else _draftPermissions.Remove(permission.Key);
        }

        UpdatePermissionCounts();
        UpdatePermissionDraftStatus();
    }

    private void SyncPermissionCheckBoxes()
    {
        _syncingPermissionCheckBoxes = true;
        try
        {
            foreach (var (key, box) in _permissionCheckBoxes)
                box.IsChecked = _draftPermissions.Contains(key);
        }
        finally
        {
            _syncingPermissionCheckBoxes = false;
        }

        UpdatePermissionCounts();
        UpdatePermissionDraftStatus();
    }

    private void UpdatePermissionCounts()
    {
        var total = _permissionCheckBoxes.Count;
        _permissionSummary.Text = _selectedRole is null
            ? $"{total} permissions available — select a role to review its access."
            : $"{_draftPermissions.Count} of {total} permissions selected · {_selectedRole.Name}";

        foreach (var group in _catalogGroups)
        {
            if (!_groupPermissionBoxes.TryGetValue(group.Key, out var boxes) ||
                !_groupCountLabels.TryGetValue(group.Key, out var label))
                continue;
            label.Text = $"{boxes.Count(box => box.IsChecked == true)} / {boxes.Count}";
        }
    }

    private void UpdatePermissionDraftStatus()
    {
        if (_selectedRole is null)
        {
            _permissionHint.Text = "Select a role in the Roles tab or from the selector above.";
            _permissionHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            return;
        }

        if (HasUnsavedPermissionChanges())
        {
            _permissionHint.Text = "Unsaved changes — press Save permissions to apply them to this role.";
            _permissionHint.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        }
        else
        {
            _permissionHint.Text = "Selections are saved on the tenant server.";
            _permissionHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        }
    }

    private bool HasUnsavedPermissionChanges() =>
        _selectedRole is not null && !_draftPermissions.SetEquals(_selectedRole.Permissions ?? []);

    private async Task SavePermissionsAsync()
    {
        var role = _selectedRole;
        if (role is null)
        {
            SetStatus("Select a role before saving permissions.");
            return;
        }

        _savePermissions.IsEnabled = false;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["permissions"] = _draftPermissions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            };
            using var request = NewRequest(HttpMethod.Patch, Endpoint("roles/" + role.Id));
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.SendAsync(request);
            var root = await ReadAsync(response);
            var updated = root.GetProperty("data").Deserialize<RoleDto>(JsonOptions);
            SavedChanges = true;
            await RefreshRolesAsync();
            if (updated is not null)
            {
                var fresh = _allRoles.FirstOrDefault(x => x.Id == updated.Id);
                if (fresh is not null) SelectRole(fresh);
            }
            SetStatus($"Permissions saved for {role.Name}.");
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Role permissions saved in tenant server.");
        }
        catch (Exception ex)
        {
            SetStatus("Permissions not saved: " + ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            ApplyRoleEditorState();
        }
    }

    private void ApplyRoleEditorState()
    {
        var selected = _selectedRole;
        var locked = selected is not null &&
            (selected.IsProtected || (!_isOwner && selected.Key == "admin"));

        _roleName.IsEnabled = !locked;
        _roleDescription.IsEnabled = !locked;
        _saveRole.IsEnabled = !locked;
        _deleteRole.IsEnabled = selected is not null && !selected.IsSystem && !locked;
        _savePermissions.IsEnabled = selected is not null && !locked;

        foreach (var (key, box) in _permissionCheckBoxes)
        {
            var isProtected = _protectedPermissions.Contains(key);
            box.IsEnabled = selected is not null && !locked && !(isProtected && !_isOwner);
        }

        if (selected is null)
        {
            _roleInfo.Text = "Select an existing role to view or edit it, or enter a name to create a new custom role.";
        }
        else if (selected.IsProtected)
        {
            _roleInfo.Text = "Protected Super Admin role — always keeps every permission and cannot be modified or deleted.";
        }
        else if (!_isOwner && selected.Key == "admin")
        {
            _roleInfo.Text = "Administrator permissions can only be changed by the restaurant owner.";
        }
        else
        {
            var type = selected.IsSystem ? "System role" : "Custom role";
            _roleInfo.Text = $"{type} · {selected.UserCount} user(s) assigned · {selected.PermissionCount} permission(s).";
        }
    }

    // -------------------------------------------------------------- plumbing

    private async Task RefreshAllAsync()
    {
        try
        {
            await RefreshRolesAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Could not load tenant roles: " + ex.Message);
        }

        try
        {
            await RefreshUsersAsync();
            SetStatus("Users, roles and permissions refreshed from the tenant server.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not load tenant users: " + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        var isProblem = message.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not saved", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not created", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not deleted", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("Select a role", StringComparison.OrdinalIgnoreCase);
        _status.SetResourceReference(TextBlock.ForegroundProperty, isProblem ? "ErrorBrush" : "TextSecondaryBrush");
    }

    private async Task GuardAsync(Button button, string successMessage, Func<Task> action)    {
        button.IsEnabled = false;
        try
        {
            await action();
            SetStatus(successMessage);
            ApplyRoleEditorState();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string GroupIcon(string groupKey) => groupKey switch
    {
        "dashboard" => "▥",
        "pos" => "▣",
        "tables" => "▦",
        "kitchen" => "☷",
        "inventory" => "▤",
        "purchases" => "+",
        "expenses" => "−",
        "reports" => "▥",
        "users" => "♙",
        "settings" => "⚙",
        _ => "•",
    };

    private static TextBlock SectionHeading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static void AddField(Panel panel, string label, FrameworkElement input)
    {
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(input);
    }

    private Uri Endpoint(string relative) =>
        new(_authority.AbsoluteUri.TrimEnd('/') + "/api/v1/desktop/" + relative);

    private HttpRequestMessage NewRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            // Non-JSON bodies are reported with the HTTP status below.
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = $"Server rejected the request (HTTP {(int)response.StatusCode}).";
            if (document is not null)
            {
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in errors.EnumerateObject())
                    {
                        if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                        {
                            message = field.Value[0].ToString();
                            break;
                        }
                    }
                }
                else if (document.RootElement.TryGetProperty("message", out var serverMessage) &&
                         serverMessage.ValueKind == JsonValueKind.String)
                {
                    message = serverMessage.GetString() ?? message;
                }
            }
            document?.Dispose();
            throw new InvalidOperationException(message);
        }

        if (document is null)
            throw new InvalidOperationException("Server returned an unreadable response.");

        using (document)
            return document.RootElement.Clone();
    }

    private sealed record RoleOption(string Key, string Label);

    private sealed record DesktopStaffAccount(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("role_name")] string? RoleName)
    {
        public string RoleDisplay => string.IsNullOrWhiteSpace(RoleName) ? Role : RoleName;
    }

    private sealed record RoleDto(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("is_system")] bool IsSystem,
        [property: JsonPropertyName("protected")] bool IsProtected,
        [property: JsonPropertyName("user_count")] int UserCount,
        [property: JsonPropertyName("permission_count")] int PermissionCount,
        [property: JsonPropertyName("permissions")] string[]? Permissions)
    {
        public string TypeLabel => IsSystem ? "System" : "Custom";
    }

    private sealed record PermissionDto(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("protected")] bool Protected);

    private sealed record PermissionGroupDto(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("permissions")] PermissionDto[] Permissions);

    private sealed record CatalogDto(
        [property: JsonPropertyName("groups")] PermissionGroupDto[]? Groups,
        [property: JsonPropertyName("protected")] string[]? Protected);
}

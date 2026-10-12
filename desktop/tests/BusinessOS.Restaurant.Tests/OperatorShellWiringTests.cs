using System.Xml.Linq;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class OperatorShellWiringTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Startup_requires_a_tenant_bound_operator_before_creating_the_shell()
    {
        var app = Read("App.xaml.cs");
        Assert.Contains("session.TenantId, licenseTenant", app);
        Assert.Contains("new OperatorSignInWindow(licenseTenant", app);
        Assert.Contains("DesktopGlassDialog.ShowDialog(signIn)", app);
        Assert.Contains("new MainWindow(session)", app);
        Assert.True(app.IndexOf("DesktopGlassDialog.ShowDialog(signIn)", StringComparison.Ordinal) <
                    app.IndexOf("new MainWindow(session)", StringComparison.Ordinal));
        Assert.Contains("ShutdownMode.OnExplicitShutdown", app);
        Assert.Contains("ShutdownMode.OnMainWindowClose", app);
        Assert.Contains("ApplyPendingRestoreAsync()", app);
        Assert.True(app.IndexOf("ApplyPendingRestoreAsync()", StringComparison.Ordinal) <
                    app.IndexOf("new MainWindow(session)", StringComparison.Ordinal));

    }

    [Fact]
    public void Every_sidebar_route_is_hidden_without_a_granted_role()
    {
        var xaml = XDocument.Load(Path.Combine(Desktop(), "MainWindow.xaml"));
        var navigation = xaml.Descendants()
            .Where(x => x.Name.LocalName == "Button" && x.Attribute("CommandParameter") is not null)
            .ToArray();
        Assert.Equal(18, navigation.Length); // All Modules, 13 routes, and 4 narrow-screen mirrors.
        foreach (var button in navigation)
        {
            var visibility = (string?)button.Attribute("Visibility");
            Assert.Contains("CanView", visibility);
            Assert.Contains("BoolToVisibilityConverter", visibility);
        }

        var vm = Read("MainWindowViewModel.cs");
        Assert.Contains("RestaurantWorkspaceRoutes.CanOpen(_session.User.Role, route)", vm);
        Assert.Contains("if (!CanView(route))", vm);
        Assert.Contains("OperatorLabel", vm);
    }

    [Fact]
    public void Sign_in_uses_password_box_and_verifies_license_tenant_before_session_save()
    {
        var xaml = XDocument.Load(Path.Combine(Desktop(), "OperatorSignInWindow.xaml"));
        Assert.Contains(xaml.Descendants(), x => x.Name.LocalName == "PasswordBox");
        var controller = Read("OperatorSignInWindow.xaml.cs");
        Assert.Contains("response.TenantId, _licensedTenantId", controller);
        Assert.True(controller.IndexOf("response.TenantId, _licensedTenantId", StringComparison.Ordinal) <
                    controller.IndexOf("await _sessions.SaveAsync(session)", StringComparison.Ordinal));
        Assert.Contains("tenantUri.Scheme == Uri.UriSchemeHttp && tenantUri.IsLoopback", controller);
    }

    [Fact]
    public void Sign_in_is_bound_to_the_licensed_restaurant_company()
    {
        var app = Read("App.xaml.cs");
        Assert.Contains(
            "new OperatorSignInWindow(licenseTenant, activationState.TenantBaseUrl, license.TenantName)",
            app);
        var xaml = XDocument.Load(Path.Combine(Desktop(), "OperatorSignInWindow.xaml"));
        Assert.Contains(xaml.Descendants(),
            e => (string?)e.Attribute(X + "Name") == "LicensedRestaurantText");
        var controller = Read("OperatorSignInWindow.xaml.cs");
        Assert.Contains("licensedTenantName", controller);
        Assert.Contains("LicensedRestaurantText.Text", controller);
    }

    [Fact]
    public void Startup_opens_the_role_gated_module_home()
    {
        var vm = Read("MainWindowViewModel.cs");
        Assert.Contains("CurrentRoute = \"modules\";", vm);
        Assert.Contains("ModuleHome.Create(_session.User.Role, NavigateCommand)", vm);
        Assert.Contains("CanViewModules", vm);
        var home = Read("ModuleHome.cs");
        Assert.Contains("RestaurantWorkspaceRoutes.CanOpen(role, m.Route)", home);
        Assert.Contains("\"dashboard\"", home);
        Assert.Contains("\"settings\"", home);
    }

    [Fact]
    public void Login_modal_never_blurs_itself()
    {
        var dialog = Read("DesktopGlassDialog.cs");
        Assert.Contains(
            "owner is not null && !ReferenceEquals(owner, dialog)",
            dialog);
        Assert.Contains(
            "? owner.Content as UIElement",
            dialog);
    }

    [Fact]
    public void Shell_keeps_status_and_actions_in_the_top_menu_bar()
    {
        // The sidebar/menu-switcher experiment was removed: the top bar is the
        // only navigation. The old page header row is gone and its controls
        // moved into the top bar so the workspace keeps the full height.
        var main = Read("MainWindow.xaml.cs");
        Assert.DoesNotContain("BuildSideBar", main);
        Assert.DoesNotContain("_useSidebarLayout", main);
        Assert.DoesNotContain("ApplyShellLayout", main);

        var xaml = Read("MainWindow.xaml");
        Assert.DoesNotContain("x:Name=\"ShellHeader\"", xaml);
        Assert.DoesNotContain("x:Name=\"TopBarRow\"", xaml);
        Assert.Contains("x:Name=\"WorkspaceSurface\"", xaml);

        var topBar = xaml.Split("x:Name=\"TopNavigationBar\"")[1]
            .Split("x:Name=\"WorkspaceShell\"")[0];
        Assert.Contains("x:Name=\"NetworkModeBadge\"", topBar);
        Assert.Contains("x:Name=\"RefreshButton\"", topBar);
        Assert.Contains("x:Name=\"NotificationBellButton\"", topBar);
        // Keys and Full screen are not top-bar chrome: they ride with the POS
        // and Kitchen action rows so tablets keep a navigation-only top bar.
        Assert.DoesNotContain("x:Name=\"WorkspaceShortcutsButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"WorkspaceFullScreenButton\"", xaml);

        var views = Read("OperationalActionViews.cs");
        Assert.Contains("Name = \"WorkspaceKeysButton\"", views);
        Assert.Contains("Name = \"WorkspaceFullScreenButton\"", views);
        Assert.Contains("BuildWorkspaceChromeButtons()", views);
        var chrome = Read("OperationalWorkspaceChrome.cs");
        Assert.Contains("FullScreenToggleRequested", chrome);
        Assert.Contains("ShortcutsRequested", chrome);
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Desktop(), file));

    private static string Desktop()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", "BusinessOS.Restaurant.Desktop");
    }
}

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
        Assert.Contains("signIn.ShowDialog()", app);
        Assert.Contains("new MainWindow(session)", app);
        Assert.True(app.IndexOf("signIn.ShowDialog()", StringComparison.Ordinal) <
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
        Assert.Equal(12, navigation.Length);
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

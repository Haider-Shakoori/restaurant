using System.Xml.Linq;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class PremiumNavigationTests
{
    [Fact]
    public void Sidebar_has_a_single_route_aware_highlight_and_keyboard_focus_style()
    {
        var root = RepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = xaml.Descendants().Single(e =>
            e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == "NavButton");

        var bindings = style.Descendants()
            .Where(e => e.Name.LocalName == "Binding")
            .Select(e => (string?)e.Attribute("Path"))
            .ToArray();
        Assert.Contains("CurrentRoute", bindings);
        Assert.Contains("CommandParameter", bindings);
        Assert.Contains(style.Descendants(), e =>
            e.Name.LocalName == "MultiBinding" &&
            ((string?)e.Attribute("Converter"))?.Contains("NavigationRouteMatchConverter") == true);
        Assert.Contains(style.Descendants(), e =>
            e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsKeyboardFocused");

        var routes = xaml.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .Select(e => (string?)e.Attribute("CommandParameter"))
            .Where(value => value is not null)
            .ToArray();
        var expected = new[] { "dashboard", "pos", "tables", "kitchen", "menu",
            "inventory", "purchases", "expenses", "closing", "reports", "users", "settings" };
        Assert.Equal(expected.OrderBy(x => x), routes.OrderBy(x => x));
    }

    [Fact]
    public void Navigation_highlight_is_updated_only_after_the_requested_page_loads()
    {
        var root = RepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindowViewModel.cs"));
        Assert.Contains("[ObservableProperty] private string _currentRoute", viewModel);
        Assert.Contains("CurrentRoute = route;", viewModel);
        Assert.True(viewModel.IndexOf("CurrentPage = page;", StringComparison.Ordinal) <
                    viewModel.IndexOf("CurrentRoute = route;", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

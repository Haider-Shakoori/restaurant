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
        // A failed navigation must not leave the previous dashboard under a
        // new POS heading when SQLite rejects a query.
        Assert.True(viewModel.IndexOf("var page = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);", StringComparison.Ordinal) <
                    viewModel.IndexOf("PageTitle = pageTitle;", StringComparison.Ordinal));
        Assert.True(viewModel.IndexOf("CurrentPage = page;", StringComparison.Ordinal) <
                    viewModel.IndexOf("CurrentRoute = route;", StringComparison.Ordinal));
    }


    [Fact]
    public void Latest_navigation_or_refresh_request_invalidates_previously_started_loads()
    {
        var gate = new BusinessOS.Restaurant.Desktop.NavigationRequestGate();
        var firstNavigation = gate.Begin();
        Assert.True(gate.IsCurrent(firstNavigation));
        var refresh = gate.Begin();
        Assert.False(gate.IsCurrent(firstNavigation));
        Assert.True(gate.IsCurrent(refresh));
        var secondNavigation = gate.Begin();
        Assert.False(gate.IsCurrent(refresh));
        Assert.True(gate.IsCurrent(secondNavigation));
        Assert.False(gate.IsCurrent(0));
    }

    [Fact]
    public async Task A_stale_page_finishing_last_cannot_overwrite_the_new_page()
    {
        var gate = new BusinessOS.Restaurant.Desktop.NavigationRequestGate();
        var firstLoader = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoader = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string visiblePage = "Dashboard";

        async Task LoadAsync(Task<string> load)
        {
            var requestId = gate.Begin();
            var page = await load;
            if (gate.IsCurrent(requestId))
                visiblePage = page;
        }

        var first = LoadAsync(firstLoader.Task);
        var second = LoadAsync(secondLoader.Task);
        secondLoader.SetResult("Kitchen");
        await second;
        firstLoader.SetResult("POS");
        await first;
        Assert.Equal("Kitchen", visiblePage);
    }

    [Fact]
    public void Both_navigation_and_refresh_gate_the_page_before_publishing()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindowViewModel.cs"));
        Assert.Contains("private readonly NavigationRequestGate _pageRequests", source);
        var refresh = source.Split("private async Task RefreshAsync()", StringSplitOptions.None)[1]
            .Split("private async Task RefreshDiagnosticsAsync()", StringSplitOptions.None)[0];
        var navigate = source.Split("private async Task NavigateAsync(string? key)", StringSplitOptions.None)[1];
        Assert.Contains("var requestId = _pageRequests.Begin();", refresh);
        Assert.Contains("var requestId = _pageRequests.Begin();", navigate);
        Assert.Contains("if (!_pageRequests.IsCurrent(requestId))", refresh);
        Assert.Contains("if (!_pageRequests.IsCurrent(requestId))", navigate);
        Assert.Contains("_pageRequests.IsCurrent(requestId) && CurrentRoute == route", refresh);
        Assert.True(refresh.IndexOf("_pageRequests.Begin()", StringComparison.Ordinal) <
                    refresh.IndexOf("await RefreshDiagnosticsAsync();", StringComparison.Ordinal));
        Assert.True(navigate.IndexOf("if (!_pageRequests.IsCurrent(requestId))", StringComparison.Ordinal) <
                    navigate.IndexOf("PageTitle = pageTitle;", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

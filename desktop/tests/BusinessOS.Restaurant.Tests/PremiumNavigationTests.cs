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

    [Fact]
    public void Workspace_load_failure_has_a_non_blocking_retry_banner()
    {
        var root = RepositoryRoot();
        var shell = XDocument.Load(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindow.xaml"));
        var banner = shell.Descendants().Single(e =>
            e.Name.LocalName == "Border" && (string?)e.Attribute("Grid.Row") == "1");
        Assert.Contains("HasWorkspaceError", (string?)banner.Attribute("Visibility"));
        Assert.Contains(banner.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == "{Binding WorkspaceErrorMessage}");
        Assert.Contains(banner.Descendants(), e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Command") == "{Binding RetryWorkspaceCommand}");
        Assert.Equal("Auto", (string?)shell.Descendants().First(e =>
            e.Name.LocalName == "Grid" && e.Descendants().Any(x => x == banner))
            .Element(shell.Root!.Name.Namespace + "Grid.RowDefinitions")?
            .Elements().ElementAt(1).Attribute("Height"));

        var workspace = shell.Descendants().Single(e => e.Name.LocalName == "ContentControl" &&
            ((string?)e.Attribute("Content"))?.Contains("CurrentPage", StringComparison.Ordinal) == true);
        Assert.Equal("2", (string?)workspace.Parent?.Attribute("Grid.Row"));
    }

    [Fact]
    public void Navigation_failures_keep_the_existing_page_and_reuse_role_aware_retry()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindowViewModel.cs"));
        Assert.Contains("RetryWorkspaceCommand = new AsyncRelayCommand(RetryWorkspaceAsync)", source);
        Assert.Contains("failedRoute is null ? Task.CompletedTask : NavigateAsync(failedRoute)", source);
        Assert.Contains("App.LogRecoverableException(\"workspace-load-\" + route, exception)", source);
        Assert.Contains("if (!_pageRequests.IsCurrent(requestId))", source);
        Assert.Contains("if (!CanView(route))", source);

        var refresh = source.Split("private async Task RefreshAsync()", StringSplitOptions.None)[1]
            .Split("private async Task RefreshDiagnosticsAsync()", StringSplitOptions.None)[0];
        Assert.Contains("catch (Exception exception)", refresh);
        Assert.Contains("HandleWorkspaceLoadFailure(route, PageTitle, requestId, exception)", refresh);
        Assert.Contains("ClearWorkspaceError();", refresh);

        var navigate = source.Split("private async Task NavigateAsync(string? key)", StringSplitOptions.None)[1];
        Assert.Contains("catch (Exception exception)", navigate);
        Assert.Contains("HandleWorkspaceLoadFailure(route, pageTitle, requestId, exception)", navigate);
        Assert.True(navigate.IndexOf("CurrentPage = page;", StringComparison.Ordinal) <
            navigate.IndexOf("ClearWorkspaceError();", StringComparison.Ordinal));
        Assert.True(navigate.IndexOf("CurrentRoute = route;", StringComparison.Ordinal) <
            navigate.IndexOf("ClearWorkspaceError();", StringComparison.Ordinal));

        var diagnostics = source.Split("private async Task RefreshDiagnosticsAsync()", StringSplitOptions.None)[1]
            .Split("private Task RetryWorkspaceAsync()", StringSplitOptions.None)[0];
        Assert.Contains("workspace-network-diagnostics", diagnostics);
        Assert.Contains("workspace-license-diagnostics", diagnostics);
        Assert.Contains("License status unavailable", diagnostics);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

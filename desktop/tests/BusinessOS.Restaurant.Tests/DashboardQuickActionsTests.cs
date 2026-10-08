using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DashboardQuickActionsTests
{
    [Fact]
    public void Quick_actions_use_existing_shell_navigation_and_workspaces()
    {
        var root = RepositoryRoot();
        var pages = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));

        Assert.Contains("DashboardQuickActions()", pages);
        Assert.Contains("root.Children.Add(DashboardQuickActions());", pages);
        Assert.Contains("new Binding(nameof(MainWindowViewModel.NavigateCommand))", pages);
        Assert.Contains("CommandParameter = route", pages);
        foreach (var route in new[] { "pos", "tables", "kitchen", "inventory" })
            Assert.Contains($"\"{route}\"", pages);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

using BusinessOS.Restaurant.Authentication;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class RestaurantWorkspaceRoutesTests
{
    [Theory]
    [InlineData("owner", "settings", true)]
    [InlineData("manager", "purchases", true)]
    [InlineData(" CASHIER ", "closing", true)]
    [InlineData("cashier", "inventory", false)]
    [InlineData("waiter", "tables", true)]
    [InlineData("waiter", "closing", false)]
    [InlineData("kitchen", "kitchen", true)]
    [InlineData("kitchen", "dashboard", false)]
    [InlineData("kitchen", "pos", false)]
    [InlineData("unknown", "settings", false)]
    [InlineData(null, "pos", false)]
    [InlineData("owner", "invalid", false)]
    public void Only_authorized_workspaces_are_visible_and_navigable(
        string? role, string route, bool allowed)
    {
        Assert.Equal(allowed, RestaurantWorkspaceRoutes.CanOpen(role, route));
    }

    [Fact]
    public void Each_valid_operator_enters_an_authorized_workspace()
    {
        foreach (var role in new[] { "owner", "manager", "cashier", "waiter", "kitchen" })
        {
            var start = RestaurantWorkspaceRoutes.DefaultRoute(role);
            Assert.True(RestaurantWorkspaceRoutes.CanOpen(role, start));
        }

        Assert.Equal("kitchen", RestaurantWorkspaceRoutes.DefaultRoute("kitchen"));
        Assert.Equal(string.Empty, RestaurantWorkspaceRoutes.DefaultRoute("unknown"));
    }
}

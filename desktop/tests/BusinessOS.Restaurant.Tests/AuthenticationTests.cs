using BusinessOS.Restaurant.Authentication;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(RestaurantRoles.Owner, RestaurantCapability.ManageRestaurant, true)]
    [InlineData(RestaurantRoles.Manager, RestaurantCapability.ManageRestaurant, true)]
    [InlineData(RestaurantRoles.Cashier, RestaurantCapability.TakePayments, true)]
    [InlineData(RestaurantRoles.Waiter, RestaurantCapability.CreateOwnOrders, true)]
    [InlineData(RestaurantRoles.Waiter, RestaurantCapability.ViewAllOrders, false)]
    [InlineData(RestaurantRoles.Kitchen, RestaurantCapability.OperateKitchen, true)]
    [InlineData(RestaurantRoles.Kitchen, RestaurantCapability.TakePayments, false)]
    public void Role_authorizer_applies_expected_capabilities(
        string role,
        RestaurantCapability capability,
        bool expected)
    {
        var user = new AuthUser(1, "public-1", "User", "user@example.test", role);
        var authorizer = new RoleAuthorizer();

        Assert.Equal(expected, authorizer.Can(user, capability));
    }

    [Fact]
    public void Login_contract_matches_existing_tenant_auth_endpoint()
    {
        var request = new LoginRequest(
            "USER@EXAMPLE.TEST",
            "secret",
            "Restaurant Desktop");

        Assert.Equal("USER@EXAMPLE.TEST", request.Email);
        Assert.Equal("secret", request.Password);
        Assert.Equal("Restaurant Desktop", request.DeviceName);
    }
}

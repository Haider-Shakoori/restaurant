namespace BusinessOS.Restaurant.Authentication;

public static class RestaurantRoles
{
    public const string Owner = "owner";
    public const string Manager = "manager";
    public const string Cashier = "cashier";
    public const string Waiter = "waiter";
    public const string Kitchen = "kitchen";
}

public enum RestaurantCapability
{
    ViewAllOrders,
    CreateOwnOrders,
    OperateKitchen,
    TakePayments,
    ManageRestaurant,
}

public sealed class RoleAuthorizer
{
    public bool Can(AuthUser user, RestaurantCapability capability)
    {
        var role = user.Role.Trim().ToLowerInvariant();

        return capability switch
        {
            RestaurantCapability.ManageRestaurant =>
                role is RestaurantRoles.Owner or RestaurantRoles.Manager,

            RestaurantCapability.ViewAllOrders =>
                role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Cashier or RestaurantRoles.Kitchen,

            RestaurantCapability.CreateOwnOrders =>
                role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Cashier or RestaurantRoles.Waiter,

            RestaurantCapability.OperateKitchen =>
                role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Kitchen,

            RestaurantCapability.TakePayments =>
                role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Cashier,

            _ => false,
        };
    }
}

namespace BusinessOS.Restaurant.Authentication;

/// <summary>
/// Desktop page-level visibility based on the signed-in operator's role.
/// Operational mutations remain subject to the existing local service authorizer.
/// </summary>
public static class RestaurantWorkspaceRoutes
{
    public static bool CanOpen(string? role, string? route)
    {
        var normalizedRole = role?.Trim().ToLowerInvariant();
        var normalizedRoute = route?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedRole) || string.IsNullOrEmpty(normalizedRoute))
            return false;

        if (normalizedRole is RestaurantRoles.Owner or RestaurantRoles.Manager)
        {
            return normalizedRoute is "dashboard" or "pos" or "tables" or
                "kitchen" or "menu" or "inventory" or "purchases" or "expenses" or
                "closing" or "reports" or "users" or "settings";
        }

        return normalizedRole switch
        {
            RestaurantRoles.Cashier => normalizedRoute is "dashboard" or "pos" or "tables" or "closing",
            RestaurantRoles.Waiter => normalizedRoute is "dashboard" or "pos" or "tables",
            RestaurantRoles.Kitchen => normalizedRoute is "kitchen",
            _ => false,
        };
    }

    public static string DefaultRoute(string? role) =>
        CanOpen(role, "dashboard") ? "dashboard" :
        CanOpen(role, "kitchen") ? "kitchen" : string.Empty;
}

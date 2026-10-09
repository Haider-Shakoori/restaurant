namespace BusinessOS.Restaurant.LocalServer;

/// <summary>
/// Only physically compatible metric units may be used in restaurant recipes.
/// Every conversion returns the ingredient's existing stock base unit so KOT
/// reservations, average valuation and COGS remain authoritative.
/// </summary>
public static class RecipeUnitConversion
{
    private static readonly Dictionary<string, decimal> UnitFactors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["kg"] = 1000m,
            ["g"] = 1m,
            ["l"] = 1000m,
            ["ml"] = 1m,
            ["pcs"] = 1m,
        };

    public static string[] AllowedUnits(string baseUnit) => baseUnit.Trim().ToLowerInvariant() switch
    {
        "kg" or "g" => ["g", "kg"],
        "l" or "ml" => ["ml", "l"],
        "pcs" => ["pcs"],
        _ => throw new ArgumentException(
            "Unsupported stock unit. Use g, kg, ml, l or pcs.", nameof(baseUnit)),
    };

    public static decimal ToBase(decimal quantity, string selectedUnit, string baseUnit)
    {
        var units = AllowedUnits(baseUnit);
        if (!units.Contains(selectedUnit.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unit {selectedUnit} cannot be converted to {baseUnit} without a validated ingredient-specific conversion.");
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Recipe quantity must be positive.");
        if (selectedUnit.Equals("pcs", StringComparison.OrdinalIgnoreCase) && quantity != decimal.Truncate(quantity))
            throw new ArgumentException("Individual pieces must have a whole-number quantity.");

        var value = quantity * UnitFactors[selectedUnit.Trim()] / UnitFactors[baseUnit.Trim()];
        var rounded = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        if (rounded <= 0m || value != rounded)
            throw new ArgumentException(
                "Quantity is too small or has more than 4 decimal places in the ingredient base unit.");
        return rounded;
    }
}

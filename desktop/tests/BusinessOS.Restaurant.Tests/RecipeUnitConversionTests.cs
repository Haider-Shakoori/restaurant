using BusinessOS.Restaurant.LocalServer;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class RecipeUnitConversionTests
{
    [Theory]
    [InlineData("kg", "g", 0.25, 250)]
    [InlineData("g", "kg", 500, 0.5)]
    [InlineData("l", "ml", 1.5, 1500)]
    [InlineData("ml", "l", 250, 0.25)]
    [InlineData("pcs", "pcs", 3, 3)]
    public void Converts_recipe_display_quantity_to_inventory_base(
        string sourceUnit, string stockUnit, double input, double expected)
    {
        var quantity = RecipeUnitConversion.ToBase((decimal)input, sourceUnit, stockUnit);
        Assert.Equal((decimal)expected, quantity);
    }

    [Fact]
    public void Prevents_mass_volume_and_fractional_pieces_errors()
    {
        Assert.Throws<ArgumentException>(() => RecipeUnitConversion.ToBase(2, "ml", "g"));
        Assert.Throws<ArgumentException>(() => RecipeUnitConversion.ToBase(0.5m, "pcs", "pcs"));
        Assert.Throws<ArgumentException>(() => RecipeUnitConversion.ToBase(0, "kg", "g"));
        Assert.Throws<ArgumentException>(() => RecipeUnitConversion.ToBase(0.0000001m, "kg", "kg"));
        Assert.Equal(new[] { "g", "kg" }, RecipeUnitConversion.AllowedUnits("g"));
        Assert.Equal(new[] { "ml", "l" }, RecipeUnitConversion.AllowedUnits("l"));
    }
}

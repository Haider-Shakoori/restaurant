using System.Xml.Linq;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class RecipeWorkspaceNavigationTests
{
    [Fact]
    public void Owner_and_manager_have_dedicated_recipe_navigation()
    {
        var root = RepositoryRoot();
        var shell = XDocument.Load(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "MainWindow.xaml"));
        var recipes = shell.Descendants().Single(x =>
            x.Name.LocalName == "Button" &&
            (string?)x.Attribute("CommandParameter") == "recipes");
        Assert.Equal("▤   Recipes & Food Cost",
            (string?)recipes.Attribute("Content"));
        Assert.Contains("CanViewRecipes", (string?)recipes.Attribute("Visibility"));
    }

    [Fact]
    public void Recipe_route_reuses_version_creation_ingredient_view_and_food_cost()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        Assert.Contains("\"recipes\" => await MenuAsync(recipesOnly: true)", source);
        Assert.Contains("route == \"recipes\" && !modules.RecipesEnabled", source);
        Assert.Contains("var recipeRows = await (from version in db.Recipes.AsNoTracking()", source);
        Assert.Contains("panel.Children.Add(recipeGrid)", source);
        Assert.Contains("View Recipes", source);
        Assert.Contains("+ Create Recipe Version", source);
        Assert.Contains("Recipe Food-Cost Preview", source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

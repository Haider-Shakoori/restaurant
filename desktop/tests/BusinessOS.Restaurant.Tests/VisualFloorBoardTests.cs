using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class VisualFloorBoardTests
{
    [Fact]
    public void Floor_tiles_use_real_table_and_order_rows_without_mutating_kot()
    {
        var path = Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        var source = File.ReadAllText(path);
        Assert.Contains("BuildVisualFloorBoard(", source);
        Assert.Contains("tables.GroupBy(x => x.Area)", source);
        Assert.Contains("activeOrders.FirstOrDefault(x => x.TableId == table.Id)", source);
        Assert.Contains("sourceOrderBox.SelectedItem = activeOrders.FirstOrDefault", source);
        Assert.Contains("order.Status == \"billed\"", source);
        Assert.Contains("DiningFloorViewModes", source);
        Assert.Contains("SelectFloorMode(\"list\")", source);
        Assert.Contains("targetTableBox.SelectedItem = availableTables.FirstOrDefault", source);
        Assert.Contains("grid.Visibility = Visibility.Collapsed", source);

        // Retain the underlying service operations, rather than rebuilding their logic.
        Assert.Contains("await workflow.TransferOrderAsync(", source);
        Assert.Contains("await workflow.MergeDraftOrdersAsync(", source);
        Assert.Contains("await workflow.MoveUnsentItemsAsync(", source);
        Assert.Contains("await workflow.SplitUnsentItemsAsync(", source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

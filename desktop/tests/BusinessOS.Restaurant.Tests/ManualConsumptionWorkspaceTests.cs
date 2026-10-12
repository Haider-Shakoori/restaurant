using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Batch 5 regression guards: the manual consumption workflow keeps its
/// required reasons, notes, Mark-as-Finished action, auditable history with
/// filters, and the deduction mode stays an explicit ON/OFF switch.
/// </summary>
public sealed class ManualConsumptionWorkspaceTests
{
    [Fact]
    public void Manual_consumption_dialog_offers_required_reasons_notes_and_finish_action()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("\"Kitchen Consumption\", \"Wastage\", \"Spoilage\"", source);
        Assert.Contains("\"Damage\", \"Stock Correction\", \"Other\"", source);
        Assert.Contains("ManualConsumptionReason", source);
        Assert.Contains("ManualConsumptionNotes", source);
        Assert.Contains("ManualCountMarkFinished", source);
        Assert.Contains("Mark remaining as fully consumed", source);
        Assert.Contains("Confirm fully consumed", source);
        Assert.Contains("RecordManualConsumptionAsync(", source);
    }

    [Fact]
    public void Consumption_history_table_has_filters_and_stock_positions()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("ConsumptionHistoryGrid", source);
        Assert.Contains("BuildConsumptionHistoryAsync", source);
        Assert.Contains("ConsumptionReason", source);
        Assert.Contains("StockBefore", source);
        Assert.Contains("StockAfter", source);
        Assert.Contains("All materials", source);
        Assert.Contains("All reasons", source);
        Assert.Contains("All staff", source);
        Assert.Contains("historyFrom.SelectedDate", source);
        Assert.Contains("historyTo.SelectedDate", source);
    }

    [Fact]
    public void Deduction_mode_is_an_explicit_on_off_switch_with_descriptions()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "RestaurantModulesPanel.cs");
        Assert.Contains("Automatic Recipe Ingredient Deduction", source);
        Assert.Contains("Deduction mode: ON", source);
        Assert.Contains("Deduction mode: OFF", source);
        Assert.Contains("Ingredients are deducted automatically", source);
        Assert.Contains("Record the actual consumption from the Inventory module", source);
        Assert.Contains("BrandPrimaryBrush", source);
    }

    [Fact]
    public void Manual_consumption_service_keeps_notes_idempotency_and_mode_conflict_guards()
    {
        var source = Source("BusinessOS.Restaurant.LocalServer", "LocalInventoryService.cs");
        Assert.Contains("string? notes = null", source);
        Assert.Contains("manual_stock_count", source);
        Assert.Contains("inventory_mode_conflict", source);
        Assert.Contains("This count ID belongs to a different ingredient.", source);
        Assert.Contains("Consumption exceeds available stock.", source);
    }

    [Fact]
    public void Profit_and_loss_consumption_mode_uses_recorded_movements_not_purchases()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("purchases remain inventory", source);
        Assert.Contains("CostedFoodConsumption", source);
        Assert.Contains("MATERIAL USAGE", source);

        var profit = Source("BusinessOS.Restaurant.Desktop", "DashboardProfitLoss.cs");
        Assert.Contains("CostedFoodConsumption", profit);
        Assert.Contains("ConsumptionMode", profit);
    }

    private static string Source(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", Path.Combine(relativePath)));
    }
}

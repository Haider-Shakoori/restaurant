using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Batch 3 regression guards: financial KPI cards stay calculation-driven and
/// the Purchases/Expenses workspaces keep their shared chrome, filters and
/// existing CRUD service calls.
/// </summary>
public sealed class PurchasesExpensesWorkspaceTests
{
    [Fact]
    public void Dashboard_keeps_profit_loss_kpi_cards_with_afn_and_loss_indicators()
    {
        var source = Source();
        Assert.Contains("DashboardCards(", source);
        Assert.Contains("Profit & Loss · Today", source);
        Assert.Contains("AFN {dailyProfit.Revenue:N2}", source);
        Assert.Contains("AFN {dailyProfit.OperatingExpenses:N2}", source);
        Assert.Contains("AFN {dailyProfit.NetOperatingProfit:N2}", source);
        Assert.Contains("OPERATING PROFIT", source);
        Assert.Contains("OPERATING LOSS", source);
        Assert.Contains("DashboardProfitLoss.Compute(", source);
    }

    [Fact]
    public void Purchases_and_expenses_use_shared_page_chrome_and_client_side_filters()
    {
        var source = Source();
        Assert.Contains("PageHeader(\"Purchases & Suppliers\"", source);
        Assert.Contains("PageHeader(\"Expenses\"", source);
        Assert.Contains("SectionTitle(\"Suppliers\")", source);
        Assert.Contains("SectionTitle(\"Purchase orders\")", source);
        Assert.Contains("SectionTitle(\"Expense ledger\")", source);
        Assert.Contains("FilterGrid(supplierGrid", source);
        Assert.Contains("FilterGrid(grid", source);
        Assert.Equal(2, Count(source, "FilterGrid(grid"));
        Assert.Contains("SearchInput(\"Search suppliers\")", source);
        Assert.Contains("SearchInput(\"Search purchase orders\")", source);
        Assert.Contains("SearchInput(\"Search expenses\")", source);
        Assert.Contains("FilterSelect(\"All statuses\"", source);
        Assert.Contains("FilterSelect(new[] { \"All categories\" }", source);
        Assert.Contains("TableToolbar(orderSearch, orderStatus)", source);
        Assert.Contains("TableToolbar(expenseSearch, expenseFilter)", source);
        Assert.Contains("EmptyState(", source);
        Assert.Contains("UseSecondaryStyle(addSupplier)", source);
        Assert.Contains("UseSecondaryStyle(receivePending)", source);
        Assert.Contains("UseSecondaryStyle(historyToggle)", source);
        Assert.Contains("UseSecondaryStyle(cancel)", source);
    }

    [Fact]
    public void Purchases_and_expenses_keep_existing_crud_service_calls()
    {
        var source = Source();
        Assert.Contains("CreateSupplierAsync(", source);
        Assert.Contains("CreatePurchaseOrderAsync(", source);
        Assert.Contains("ReceivePurchaseOrderAsync(", source);
        Assert.Contains("RecordExpenseAsync(", source);
    }

    [Fact]
    public void Dashboard_reads_are_bounded_to_the_current_business_day_window()
    {
        var source = Source();
        Assert.Equal(2, Count(source, "AddHours(-48)"));
        Assert.Contains("OperationalWindowQueries.BillsSince(db, trendWindowStartUtc)", source);
        Assert.Contains("OperationalWindowQueries.PostedPaymentsSince(accounting, profitWindowStartUtc)", source);
        Assert.Contains("OperationalWindowQueries.ConsumptionMovementsSince(accounting, profitWindowStartUtc)", source);
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var index = value.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = value.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
    }
}

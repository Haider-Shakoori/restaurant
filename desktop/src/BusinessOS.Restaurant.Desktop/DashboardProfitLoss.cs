namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Local management view of bill-based operating P&L. Purchasing raw stock
/// raises inventory value, it does NOT count as food COGS until the stock is
/// consumed. Cash received is separate from revenue and profit.
/// Historical recipe movements without captured unit prices remain uncosted,
/// not silently reported as zero food cost.
/// </summary>
internal sealed record DashboardProfitLoss(
    decimal Revenue, decimal CostedFoodConsumption, decimal OperatingExpenses,
    decimal CashCollected, int UnvaluedConsumptionMovements,
    string ConsumptionMode)
{
    public decimal GrossProfit => Revenue - CostedFoodConsumption;
    public decimal NetOperatingProfit => GrossProfit - OperatingExpenses;
    // Manual counting is an end-of-period process. Until a signed physical
    // inventory close exists, the dashboard is a provisional operating P&L.
    public bool IsComplete => UnvaluedConsumptionMovements == 0 &&
        !ConsumptionMode.StartsWith("Manual", StringComparison.OrdinalIgnoreCase);

    public static DashboardProfitLoss Compute(
        decimal revenue,
        IEnumerable<(string Source, decimal QuantityDelta, decimal? UnitCost)> movements,
        decimal operatingExpenses,
        decimal cashCollected,
        string mode)
    {
        decimal consumed = 0m;
        var missing = 0;
        foreach (var movement in movements)
        {
            // Inventory adjustments and receipts do not represent food use.
            if (movement.Source is not ("kitchen_production" or "manual_stock_count") ||
                movement.QuantityDelta >= 0m)
                continue;
            if (movement.UnitCost is null)
            {
                missing++;
                continue;
            }
            consumed += -movement.QuantityDelta * movement.UnitCost.Value;
        }
        return new DashboardProfitLoss(
            Math.Round(revenue, 2), Math.Round(consumed, 2),
            Math.Round(operatingExpenses, 2), Math.Round(cashCollected, 2),
            missing, mode);
    }
}

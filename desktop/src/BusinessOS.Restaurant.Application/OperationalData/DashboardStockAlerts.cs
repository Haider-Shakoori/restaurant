namespace BusinessOS.Restaurant.Application.OperationalData;

/// <summary>
/// Calculates local inventory alerts without touching stock quantities or accounting.
/// Each entry is one item at one active branch; missing balances are not silently
/// represented as zero stock. Reorder level zero means alerts are not configured.
/// </summary>
public static class DashboardStockAlerts
{
    public static IReadOnlyList<DashboardStockAlert> Find(
        IEnumerable<DashboardStockBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);

        return balances
            .Where(row => row.ReorderLevel > 0m && row.Quantity <= row.ReorderLevel)
            .OrderBy(row => row.Quantity <= 0m ? 0 : 1)
            .ThenBy(row => row.Quantity / row.ReorderLevel)
            .ThenBy(row => row.BranchName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(row => new DashboardStockAlert(
                row.BranchName, row.ItemName, row.BaseUnit, row.Quantity, row.ReorderLevel))
            .ToArray();
    }
}

public sealed record DashboardStockBalance(
    string BranchName,
    string ItemName,
    string BaseUnit,
    decimal Quantity,
    decimal ReorderLevel);

public sealed record DashboardStockAlert(
    string BranchName,
    string ItemName,
    string BaseUnit,
    decimal Quantity,
    decimal ReorderLevel);

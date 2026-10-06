using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Restaurant.Desktop;

public sealed class ReportsViewModel : ObservableObject
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:5187/"), Timeout = TimeSpan.FromSeconds(5) };
    private string _branchId = "branch-1";
    private DateTime _from = DateTime.Today.AddDays(-6);
    private DateTime _to = DateTime.Today;
    private string _sales = "AFN 0"; private string _profit = "AFN 0"; private string _inventory = "AFN 0"; private string _variance = "AFN 0";
    private string _outstanding = "AFN 0"; private string _margin = "0%"; private string _status = "Ready"; private bool _busy;

    public ReportsViewModel() => RefreshReportsCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    public AsyncRelayCommand RefreshReportsCommand { get; }
    public ObservableCollection<ReportPaymentRow> Payments { get; } = [];
    public ObservableCollection<ReportItemRow> TopItems { get; } = [];
    public ObservableCollection<ReportSalesDayRow> SalesTrend { get; } = [];
    public ObservableCollection<ReportAccountingRow> Accounting { get; } = [];
    public ObservableCollection<ReportClosingRow> Closings { get; } = [];
    public ObservableCollection<ReportInventoryRow> Inventory { get; } = [];
    public string BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime To { get => _to; set => SetProperty(ref _to, value); }
    public string Sales { get => _sales; private set => SetProperty(ref _sales, value); }
    public string GrossProfit { get => _profit; private set => SetProperty(ref _profit, value); }
    public string InventoryValue { get => _inventory; private set => SetProperty(ref _inventory, value); }
    public string CashVariance { get => _variance; private set => SetProperty(ref _variance, value); }
    public string Outstanding { get => _outstanding; private set => SetProperty(ref _outstanding, value); }
    public string GrossMargin { get => _margin; private set => SetProperty(ref _margin, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { if(SetProperty(ref _busy,value)) RefreshReportsCommand.NotifyCanExecuteChanged(); } }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(BranchId)) { Status="Enter a branch ID."; return; }
        if (To.Date < From.Date) { Status="End date must be on or after start date."; return; }
        IsBusy=true; Status="Loading local reports...";
        var q=$"?branch_id={Uri.EscapeDataString(BranchId.Trim())}&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}";
        try
        {
            var summary=await _http.GetFromJsonAsync<Envelope<Summary>>("api/v1/reports/summary"+q);
            if(summary?.Data is not null){ Sales=Money(summary.Data.NetSales); GrossProfit=Money(summary.Data.GrossProfit); InventoryValue=Money(summary.Data.InventoryValue); CashVariance=Money(summary.Data.CashVariance); Outstanding=Money(summary.Data.Outstanding); GrossMargin=$"{summary.Data.GrossMarginPercent:N2}%"; }
            await Load("api/v1/reports/payments"+q, Payments);
            await Load("api/v1/reports/top-items"+q+"&limit=10", TopItems);
            await Load("api/v1/reports/sales-trend"+q, SalesTrend);
            await Load("api/v1/reports/accounting"+q, Accounting);
            await Load("api/v1/reports/closings"+q, Closings);
            await Load("api/v1/reports/inventory?branch_id="+Uri.EscapeDataString(BranchId.Trim()), Inventory);
            Status=$"Local report refreshed at {DateTime.Now:HH:mm:ss}.";
        }
        catch(Exception ex) { Status=$"Reports unavailable: {ex.Message}"; }
        finally { IsBusy=false; }
    }

    private async Task Load<T>(string path, ObservableCollection<T> target)
    {
        var response=await _http.GetFromJsonAsync<Envelope<List<T>>>(path);
        target.Clear(); foreach(var row in response?.Data ?? []) target.Add(row);
    }
    private static string Money(decimal value) => $"AFN {value:N2}";
    private sealed record Envelope<T>(T Data);
    private sealed record Summary(decimal NetSales, decimal GrossProfit, decimal GrossMarginPercent, decimal InventoryValue, decimal CashVariance, decimal Outstanding);
}
public sealed record ReportPaymentRow(string Method, int Count, decimal Amount);
public sealed record ReportItemRow(string ItemName, int Quantity, decimal Sales);
public sealed record ReportSalesDayRow(DateTime BusinessDate, int BillCount, decimal NetSales, decimal Payments, decimal CostOfGoods, decimal GrossProfit);
public sealed record ReportAccountingRow(string Code, string Account, string Category, decimal Debit, decimal Credit);
public sealed record ReportClosingRow(DateTime BusinessDate, string Status, int Version, decimal NetSales, decimal Payments, decimal CashVariance);
public sealed record ReportInventoryRow(string Sku, string ItemName, string Unit, decimal Quantity, decimal AverageUnitCost, decimal Value, decimal ReorderLevel, bool IsLowStock);

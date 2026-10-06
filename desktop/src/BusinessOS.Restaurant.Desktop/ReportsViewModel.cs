using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace BusinessOS.Restaurant.Desktop;

public sealed class ReportsViewModel : ObservableObject
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:5187/"), Timeout = TimeSpan.FromSeconds(5) };
    private string _branchId = "branch-1";
    private DateTime _from = DateTime.Today;
    private DateTime _to = DateTime.Today;
    private string _sales = "AFN 0";
    private string _profit = "AFN 0";
    private string _inventory = "AFN 0";
    private string _variance = "AFN 0";

    public ReportsViewModel() => RefreshReportsCommand = new AsyncRelayCommand(RefreshAsync);
    public AsyncRelayCommand RefreshReportsCommand { get; }
    public ObservableCollection<ReportPaymentRow> Payments { get; } = [];
    public ObservableCollection<ReportItemRow> TopItems { get; } = [];
    public string BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime To { get => _to; set => SetProperty(ref _to, value); }
    public string Sales { get => _sales; private set => SetProperty(ref _sales, value); }
    public string GrossProfit { get => _profit; private set => SetProperty(ref _profit, value); }
    public string InventoryValue { get => _inventory; private set => SetProperty(ref _inventory, value); }
    public string CashVariance { get => _variance; private set => SetProperty(ref _variance, value); }

    private async Task RefreshAsync()
    {
        var q=$"?branch_id={Uri.EscapeDataString(BranchId)}&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}";
        try
        {
            var summary=await _http.GetFromJsonAsync<Envelope<Summary>>("api/v1/reports/summary"+q);
            if(summary?.Data is not null){ Sales=$"AFN {summary.Data.NetSales:N2}"; GrossProfit=$"AFN {summary.Data.GrossProfit:N2}"; InventoryValue=$"AFN {summary.Data.InventoryValue:N2}"; CashVariance=$"AFN {summary.Data.CashVariance:N2}"; }
            var payments=await _http.GetFromJsonAsync<Envelope<List<ReportPaymentRow>>>("api/v1/reports/payments"+q);
            Payments.Clear(); foreach(var row in payments?.Data ?? []) Payments.Add(row);
            var items=await _http.GetFromJsonAsync<Envelope<List<ReportItemRow>>>("api/v1/reports/top-items"+q+"&limit=10");
            TopItems.Clear(); foreach(var row in items?.Data ?? []) TopItems.Add(row);
        }
        catch { }
    }

    private sealed record Envelope<T>(T Data);
    private sealed record Summary(decimal NetSales, decimal GrossProfit, decimal InventoryValue, decimal CashVariance);
}
public sealed record ReportPaymentRow(string Method, int Count, decimal Amount);
public sealed record ReportItemRow(string ItemName, int Quantity, decimal Sales);

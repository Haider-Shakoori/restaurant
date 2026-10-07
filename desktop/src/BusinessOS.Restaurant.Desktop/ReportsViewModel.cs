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
    private DateTime _from = DateTime.Today;
    private DateTime _to = DateTime.Today;
    private string _sales = "AFN 0";
    private string _profit = "AFN 0";
    private string _inventory = "AFN 0";
    private string _variance = "AFN 0";
    private string _kitchenAverage = "0.00 min";
    private string _kitchenLate = "0";
    private string _kitchenRush = "0";
    private string _kitchenRounds = "0";

    public ReportsViewModel() => RefreshReportsCommand = new AsyncRelayCommand(RefreshAsync);
    public AsyncRelayCommand RefreshReportsCommand { get; }
    public ObservableCollection<ReportPaymentRow> Payments { get; } = [];
    public ObservableCollection<ReportItemRow> TopItems { get; } = [];
    public ObservableCollection<ReportKitchenStationRow> KitchenStations { get; } = [];
    public string BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime To { get => _to; set => SetProperty(ref _to, value); }
    public string Sales { get => _sales; private set => SetProperty(ref _sales, value); }
    public string GrossProfit { get => _profit; private set => SetProperty(ref _profit, value); }
    public string InventoryValue { get => _inventory; private set => SetProperty(ref _inventory, value); }
    public string CashVariance { get => _variance; private set => SetProperty(ref _variance, value); }
    public string KitchenAverage { get => _kitchenAverage; private set => SetProperty(ref _kitchenAverage, value); }
    public string KitchenLateItems { get => _kitchenLate; private set => SetProperty(ref _kitchenLate, value); }
    public string KitchenRushItems { get => _kitchenRush; private set => SetProperty(ref _kitchenRush, value); }
    public string KitchenRounds { get => _kitchenRounds; private set => SetProperty(ref _kitchenRounds, value); }

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

            var kitchen=await _http.GetFromJsonAsync<Envelope<KitchenSummary>>("api/v1/reports/kitchen"+q);
            if(kitchen?.Data is not null)
            {
                KitchenAverage=$"{kitchen.Data.AverageTotalMinutes:N2} min";
                KitchenLateItems=kitchen.Data.LateItems.ToString();
                KitchenRushItems=kitchen.Data.RushItems.ToString();
                KitchenRounds=kitchen.Data.KotRounds.ToString();
                KitchenStations.Clear();
                foreach(var row in kitchen.Data.Stations ?? []) KitchenStations.Add(row);
            }
        }
        catch { }
    }

    private sealed record Envelope<T>(T Data);
    private sealed record Summary(decimal NetSales, decimal GrossProfit, decimal InventoryValue, decimal CashVariance);
    private sealed record KitchenSummary(
        int KotRounds,
        int RushItems,
        int LateItems,
        double AverageTotalMinutes,
        List<ReportKitchenStationRow>? Stations);
}
public sealed record ReportKitchenStationRow(
    string StationId,
    string Station,
    int Tickets,
    int Items,
    int RushItems,
    int LateItems,
    int ActiveItems,
    double AverageQueueMinutes,
    double AveragePreparationMinutes,
    double AverageTotalMinutes,
    double UtilizationPercent);
public sealed record ReportPaymentRow(string Method, int Count, decimal Amount);
public sealed record ReportItemRow(string ItemName, int Quantity, decimal Sales);

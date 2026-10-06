using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalDatabaseFactory _databaseFactory = new();
    private readonly ConnectionSettingsStore _settingsStore = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadStaffAsync();
        await RefreshLanStatusAsync();
    }

    private async Task LoadStaffAsync()
    {
        try
        {
            await _databaseFactory.EnsureCreatedAsync();
            await using var db = _databaseFactory.Create();
            var staff = await db.StaffUsers
                .Where(value => value.IsActive)
                .OrderBy(value => value.Name)
                .AsNoTracking()
                .Select(value => new StaffChoice(
                    value.Id,
                    value.Name,
                    value.Role,
                    value.Name + " — " + value.Role))
                .ToListAsync();

            StaffCombo.ItemsSource = staff;

            if (staff.Count > 0)
            {
                StaffCombo.SelectedIndex = 0;
            }
        }
        catch (Exception exception)
        {
            PairingErrorText.Text = exception.Message;
        }
    }

    private async Task RefreshLanStatusAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var port = settings?.LocalServerPort ?? 8787;

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2),
            };

            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/v1/local/info");
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var urls = document.RootElement.GetProperty("base_urls")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            LocalServerStatus.Text = "Local host online";
            LanEndpointText.Text = urls.Length > 0
                ? string.Join("   •   ", urls)
                : $"http://127.0.0.1:{port}";
        }
        catch
        {
            LocalServerStatus.Text = "Local host unavailable";
            LanEndpointText.Text = $"Expected port: {port}";
        }
    }

    private async void GeneratePairingCode_Click(object sender, RoutedEventArgs e)
    {
        PairingErrorText.Text = string.Empty;
        PairingCodeText.Text = "—";
        PairingExpiryText.Text = string.Empty;

        if (StaffCombo.SelectedItem is not StaffChoice staff)
        {
            PairingErrorText.Text = "Select an active staff member first.";
            return;
        }

        var settings = await _settingsStore.LoadAsync();
        var port = settings?.LocalServerPort ?? 8787;

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5),
            };

            using var response = await http.PostAsJsonAsync(
                $"http://127.0.0.1:{port}/api/v1/local/admin/pairing-code",
                new { staff_user_id = staff.Id });

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                using var error = JsonDocument.Parse(body);
                PairingErrorText.Text = error.RootElement.TryGetProperty("message", out var message)
                    ? message.GetString() ?? "Unable to generate a pairing code."
                    : "Unable to generate a pairing code.";
                return;
            }

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.GetProperty("data");
            PairingCodeText.Text = data.GetProperty("pairing_code").GetString() ?? "—";

            var expires = data.GetProperty("expires_at").GetDateTimeOffset();
            PairingExpiryText.Text = $"For {staff.Name} • expires {expires.ToLocalTime():t}";
        }
        catch (Exception exception)
        {
            PairingErrorText.Text = exception.Message;
        }
    }

    private sealed record StaffChoice(long Id, string Name, string Role, string DisplayName);
}

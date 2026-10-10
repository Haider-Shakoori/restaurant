using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Media.Imaging;
using QRCoder;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Restaurant.Desktop;

public sealed class LanDiagnosticsViewModel : ObservableObject
{
    private readonly WindowsActivationStore _activationStore;
    private readonly WindowsSessionStore _sessionStore;
    private readonly LocalTerminalManagementService _terminalManagement;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    private string _networkMode = "Loading";
    private string _leaseStatus = "Unknown";
    private string _cloudStatus = "Unknown";
    private string _terminalSummary = "0 terminals";
    private string _statusMessage = "Loading local restaurant diagnostics...";
    private int _pendingCloudMutations;
    private int _openCloudConflicts;
    private LocalTerminalSnapshot? _selectedTerminal;
    private AuthSession? _session;
    private bool _isBusy;
    private string _pairingDetails = "Pairing details are unavailable until Desktop activation is complete.";
    private string _pairingPayload = string.Empty;
    private BitmapImage? _pairingQrImage;
    private string _mobileAllowance = "Unknown";

    public LanDiagnosticsViewModel()
    {
        var factory = new LocalDatabaseFactory();
        _activationStore = new WindowsActivationStore();
        _sessionStore = new WindowsSessionStore();
        _terminalManagement = new LocalTerminalManagementService(
            factory,
            new ConnectionSettingsStore(),
            _activationStore);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ToggleSelectedTerminalCommand = new AsyncRelayCommand(
            ToggleSelectedTerminalAsync,
            CanManageSelectedTerminal);
        UnpairSelectedTerminalCommand = new AsyncRelayCommand(
            UnpairSelectedTerminalAsync,
            CanManageSelectedTerminal);
    }

    public ObservableCollection<LocalTerminalSnapshot> Terminals { get; } = [];

    public ReportsViewModel Reports { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ToggleSelectedTerminalCommand { get; }

    public IAsyncRelayCommand UnpairSelectedTerminalCommand { get; }

    public string NetworkMode
    {
        get => _networkMode;
        private set => SetProperty(ref _networkMode, value);
    }

    public string LeaseStatus
    {
        get => _leaseStatus;
        private set => SetProperty(ref _leaseStatus, value);
    }

    public string CloudStatus
    {
        get => _cloudStatus;
        private set => SetProperty(ref _cloudStatus, value);
    }

    public string TerminalSummary
    {
        get => _terminalSummary;
        private set => SetProperty(ref _terminalSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int PendingCloudMutations
    {
        get => _pendingCloudMutations;
        private set => SetProperty(ref _pendingCloudMutations, value);
    }

    public string PairingDetails
    {
        get => _pairingDetails;
        private set => SetProperty(ref _pairingDetails, value);
    }

    public string PairingPayload
    {
        get => _pairingPayload;
        private set => SetProperty(ref _pairingPayload, value);
    }

    public BitmapImage? PairingQrImage
    {
        get => _pairingQrImage;
        private set => SetProperty(ref _pairingQrImage, value);
    }

    public string MobileAllowance
    {
        get => _mobileAllowance;
        private set => SetProperty(ref _mobileAllowance, value);
    }

    public int OpenCloudConflicts
    {
        get => _openCloudConflicts;
        private set => SetProperty(ref _openCloudConflicts, value);
    }

    public LocalTerminalSnapshot? SelectedTerminal
    {
        get => _selectedTerminal;
        set
        {
            if (SetProperty(ref _selectedTerminal, value))
            {
                ToggleSelectedTerminalCommand.NotifyCanExecuteChanged();
                UnpairSelectedTerminalCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleTerminalLabel));
            }
        }
    }

    public string ToggleTerminalLabel =>
        SelectedTerminal?.IsEnabled == false ? "Enable terminal" : "Disable terminal";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                ToggleSelectedTerminalCommand.NotifyCanExecuteChanged();
                UnpairSelectedTerminalCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            _session = await _sessionStore.LoadAsync();
            var activation = await _activationStore.LoadAsync();

            if (activation is null)
            {
                NetworkMode = "Not activated";
                LeaseStatus = "No activation";
                CloudStatus = "Unavailable";
                TerminalSummary = "0 terminals";
                StatusMessage = "Activate this Windows installation before enabling the local restaurant host.";
                Terminals.Clear();
                PairingDetails = "Pairing details are unavailable until Desktop activation is complete.";
                PairingPayload = string.Empty;
                PairingQrImage = null;
                MobileAllowance = "Not activated";
                return;
            }

            var diagnostics = await _terminalManagement.GetDiagnosticsAsync(
                activation.Snapshot.TenantId);
            var terminals = await _terminalManagement.GetTerminalsAsync();
            var connection = await new ConnectionSettingsStore().LoadAsync();
            var port = connection?.LocalServerPort ?? 8787;
            var descriptor = LocalServerDescriptor.Create(
                new LocalServerOptions(activation.Snapshot.TenantId, port));
            var localAddress = descriptor.BaseUrls.FirstOrDefault();
            var cloudAddress = connection?.TenantBaseUrl ?? activation.TenantBaseUrl;
            var standalone = DesktopOperatingMode.IsStandalone(activation);
            var pairingToken = standalone
                ? (Token: (string?)null, ExpiresAt: (DateTimeOffset?)null)
                : await TryCreateCloudPairingTokenAsync(activation, cloudAddress);

            var pairingExpiry = pairingToken.ExpiresAt is null
                ? "Cloud pairing token unavailable; QR configures connection addresses only."
                : $"One-time mobile activation token expires {pairingToken.ExpiresAt:HH:mm:ss} UTC.";

            PairingDetails = standalone
                ? $"Local: {localAddress ?? "unavailable"}\\nMode: Standalone Offline (LAN only, web disabled)"
                : localAddress is null
                ? $"Local: unavailable\nCloud: {cloudAddress}\nMode: Automatic (cloud until LAN returns)\n{pairingExpiry}"
                : $"Local: {localAddress}\nCloud: {cloudAddress}\nMode: Automatic (LAN preferred)\n{pairingExpiry}";

            PairingPayload = JsonSerializer.Serialize(new
            {
                type = "businessos.restaurant.pairing.v1",
                tenant_id = activation.Snapshot.TenantId,
                local_url = localAddress,
                cloud_url = standalone ? null : cloudAddress,
                connection_mode = standalone ? "local" : "automatic",
                pairing_token = pairingToken.Token,
                pairing_expires_at = pairingToken.ExpiresAt,
            });
            PairingQrImage = CreateQrImage(PairingPayload);

            NetworkMode = diagnostics.NetworkMode switch
            {
                LocalNetworkMode.Healthy => "Healthy",
                LocalNetworkMode.Degraded => "Degraded",
                LocalNetworkMode.IsolatedLocal => "Local-only",
                _ => diagnostics.NetworkMode,
            };

            LeaseStatus = diagnostics.OfflineLeaseValid
                ? $"Valid until {diagnostics.OfflineValidUntil:yyyy-MM-dd HH:mm} UTC"
                : "Expired / invalid";

            CloudStatus = standalone ? "Disabled by signed standalone license" :
                diagnostics.LastCloudSuccessAtUtc is null
                ? diagnostics.SyncEnabled ? "No successful cloud sync yet" : "Cloud sync disabled"
                : $"Last sync {diagnostics.LastCloudSuccessAtUtc:yyyy-MM-dd HH:mm:ss} UTC";

            PendingCloudMutations = diagnostics.PendingCloudMutations;
            OpenCloudConflicts = diagnostics.OpenCloudConflicts;
            TerminalSummary =
                $"{diagnostics.OnlineTerminals} online · {diagnostics.StaleTerminals} stale · " +
                $"{diagnostics.OfflineTerminals} offline · {diagnostics.DisabledTerminals} disabled";

            Terminals.Clear();
            foreach (var terminal in terminals)
            {
                Terminals.Add(terminal);
            }

            var mobileCount = terminals.Count(value =>
                string.Equals(value.ClientType, "android", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.ClientType, "ios", StringComparison.OrdinalIgnoreCase));
            MobileAllowance = activation.Snapshot.MobileDeviceLimit is int mobileLimit
                ? $"{mobileCount} / {mobileLimit} paired"
                : $"{mobileCount} paired · unlimited";

            StatusMessage = standalone
                ? "Standalone Offline is active. No tenant web sync or cloud pairing requests are sent. Local SQLite, POS, kitchen, cashier and enabled LAN devices remain available."
                : diagnostics.NetworkMode == LocalNetworkMode.IsolatedLocal
                ? "Cloud is unavailable or intentionally disabled. Local ordering, KOT and cashier operations remain authoritative while the signed offline lease is valid."
                : diagnostics.NetworkMode == LocalNetworkMode.Degraded
                    ? "Cloud connectivity is degraded. Local restaurant operations remain available and queued changes will reconcile automatically."
                    : "LAN and cloud reconciliation are healthy.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Diagnostics could not be refreshed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanManageSelectedTerminal() =>
        !IsBusy &&
        SelectedTerminal is not null &&
        _session is not null &&
        IsManager(_session.User.Role);

    private async Task ToggleSelectedTerminalAsync()
    {
        var terminal = SelectedTerminal;
        var session = _session;

        if (terminal is null || session is null || !IsManager(session.User.Role))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _terminalManagement.SetEnabledAsync(
                terminal.DeviceId,
                !terminal.IsEnabled,
                session.User.Id);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    private async Task UnpairSelectedTerminalAsync()
    {
        var terminal = SelectedTerminal;
        var session = _session;

        if (terminal is null || session is null || !IsManager(session.User.Role))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _terminalManagement.UnpairAsync(terminal.DeviceId);
        }
        finally
        {
            IsBusy = false;
        }

        SelectedTerminal = null;
        await RefreshAsync();
    }

    private async Task<(string? Token, DateTimeOffset? ExpiresAt)> TryCreateCloudPairingTokenAsync(
        ActivationState activation,
        string cloudAddress)
    {
        var session = _session;
        if (session is null || !IsManager(session.User.Role))
        {
            return (null, null);
        }

        try
        {
            var baseUri = new Uri(cloudAddress.TrimEnd('/') + "/", UriKind.Absolute);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(baseUri, "api/v1/pairing-tokens"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);
            request.Headers.TryAddWithoutValidation("X-Device-Id", activation.DeviceId);
            request.Headers.TryAddWithoutValidation("X-Device-Secret", activation.DeviceSecret);
            request.Headers.TryAddWithoutValidation("X-App-Version", "1.0.0");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            var token = data.GetProperty("pairing_token").GetString();
            var expires = data.TryGetProperty("expires_at", out var expiresProperty) &&
                          DateTimeOffset.TryParse(expiresProperty.GetString(), out var parsed)
                ? parsed
                : (DateTimeOffset?)null;

            return (string.IsNullOrWhiteSpace(token) ? null : token, expires);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static BitmapImage CreateQrImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(qrData);
        var bytes = qr.GetGraphic(8);

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsManager(string role) =>
        role.Equals(RestaurantRoles.Owner, StringComparison.OrdinalIgnoreCase) ||
        role.Equals(RestaurantRoles.Manager, StringComparison.OrdinalIgnoreCase) ||
        role.Equals("admin", StringComparison.OrdinalIgnoreCase);
}

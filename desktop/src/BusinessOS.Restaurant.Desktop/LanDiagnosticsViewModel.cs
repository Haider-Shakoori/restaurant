using System.Collections.ObjectModel;
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
            PairingDetails = localAddress is null
                ? "No private LAN address is currently available. Mobile devices can use the cloud endpoint."
                : $"Local: {localAddress}\nCloud: {connection?.TenantBaseUrl ?? activation.TenantBaseUrl}\nMode: Automatic (LAN preferred)";

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

            CloudStatus = diagnostics.LastCloudSuccessAtUtc is null
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

            StatusMessage = diagnostics.NetworkMode == LocalNetworkMode.IsolatedLocal
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

    private static bool IsManager(string role) =>
        role.Equals(RestaurantRoles.Owner, StringComparison.OrdinalIgnoreCase) ||
        role.Equals(RestaurantRoles.Manager, StringComparison.OrdinalIgnoreCase);
}

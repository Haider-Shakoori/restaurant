using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;

namespace BusinessOS.Restaurant.Desktop;

public partial class App : System.Windows.Application
{
    private LocalHostBootstrapper? _localHost;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _localHost = new LocalHostBootstrapper(
            new WindowsActivationStore(),
            new LocalRestaurantServer(),
            new ConnectionSettingsStore());

        try
        {
            await _localHost.StartIfConfiguredAsync();
        }
        catch
        {
            // The desktop remains usable when the LAN host cannot bind.
            // Diagnostics/UI reporting are added with the local-host management surface.
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_localHost is not null)
        {
            _localHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}

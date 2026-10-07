using System.Windows;
using BusinessOS.Restaurant.Desktop.Appearance;

namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoadedAsync;
        Closed += OnClosed;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateBackdropVisibility(ThemeManager.Current);
    }

    private void OnThemeChanged(AppearanceTheme theme)
    {
        // Both modes share the same layout; only Glass renders the photographic backdrop.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateBackdropVisibility(theme));
            return;
        }

        UpdateBackdropVisibility(theme);
    }

    private void UpdateBackdropVisibility(AppearanceTheme theme)
    {
        RestaurantBackdrop.Visibility = theme == AppearanceTheme.Glass
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _initialized = false;
            App.LogRecoverableException("Main window initialization", exception);
            MessageBox.Show(
                "Restaurant Desktop could not finish loading this screen. The application will remain open.\n\n" +
                "A diagnostic log was written under LocalAppData\\BusinessOS\\Restaurant\\logs.",
                "BusinessOS Restaurant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

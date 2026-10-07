namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, System.Windows.RoutedEventArgs e)
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
            System.Windows.MessageBox.Show(
                "Restaurant Desktop could not finish loading this screen. The application will remain open.\n\n" +
                "A diagnostic log was written under LocalAppData\\BusinessOS\\Restaurant\\logs.",
                "BusinessOS Restaurant",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}

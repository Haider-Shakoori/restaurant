namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly LanDiagnosticsViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new LanDiagnosticsViewModel();
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        };
    }
}

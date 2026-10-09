using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop;
using BusinessOS.Restaurant.Desktop.Appearance;
using BusinessOS.Restaurant.Persistence;

namespace BusinessOS.Restaurant.UiSmoke;

/// <summary>
/// Test-only WPF interaction runner. Never ship in the application installer.
/// Creates an in-memory synthetic operator directly in this separate harness:
/// the production application's licensing and sign-in startup are NOT changed.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(25);
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RUNNER_TEMP")))
        {
            Console.Error.WriteLine("UI smoke harness is restricted to ephemeral GitHub Actions runners.");
            return 2;
        }

        // Never touch an already-existing restaurant database, even on CI.
        var databasePath = new LocalDatabaseFactory().DatabasePath;
        if (File.Exists(databasePath))
        {
            Console.Error.WriteLine("Refusing UI smoke: the local restaurant database already exists.");
            return 2;
        }

        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/BusinessOS.Restaurant.Desktop;component/Themes/Classic.xaml"),
        });
        ThemeManager.Apply(AppearanceTheme.Classic);

        var owner = OpenWindow("owner");
        application.MainWindow = owner;
        owner.Loaded += async (_, _) =>
        {
            try
            {
                await RunAsync(application, owner);
                Console.WriteLine($"WPF UI INTERACTION SMOKE PASSED ({_checks} checks).");
                application.Shutdown(0);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("WPF UI INTERACTION SMOKE FAILED: " + exception);
                application.Shutdown(1);
            }
        };

        return application.Run(owner);
    }

    private static MainWindow OpenWindow(string role) =>
        new(new AuthSession(
            "https://restaurant-ci.example.test",
            "not-a-real-access-token",
            new AuthUser(901, "ci-test-operator", "CI " + role, "ci@example.test", role),
            "isolated-test-tenant",
            DateTimeOffset.UtcNow))
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 800,
            Left = 50,
            Top = 50,
        };

    private static async Task RunAsync(System.Windows.Application app, MainWindow owner)
    {
        var vm = (MainWindowViewModel)owner.DataContext;
        await WaitUntilAsync(() => vm.CurrentPage is not null || vm.HasWorkspaceError,
            "initial Dashboard render");

        Assert(!vm.HasWorkspaceError, "Dashboard opens without an error");
        CheckNavigation(owner, "menu");
        CheckNavigation(owner, "inventory");
        CheckNavigation(owner, "purchases");
        CheckNavigation(owner, "expenses");
        CheckNavigation(owner, "pos");
        CheckNavigation(owner, "kitchen");

        await NavigateAsync(owner, "menu");
        await CheckFormAsync(app, owner, "+ Add Menu Item", "New Menu Item", "Save Menu Item",
            "Enter a menu item name.");

        await NavigateAsync(owner, "inventory");
        await CheckFormAsync(app, owner, "+ Add Ingredient", "Add Ingredient", "Save Ingredient",
            "Enter ingredient name and SKU.");

        await NavigateAsync(owner, "purchases");
        await CheckFormAsync(app, owner, "+ Add Supplier", "Add Supplier", "Save Supplier",
            "Supplier code and name are required.");
        await CheckFormAsync(app, owner, "+ New Purchase & Receive", "Purchase & Receive Inventory",
            "Create PO and Receive Stock", "Select branch, supplier and at least one purchase line.");

        await NavigateAsync(owner, "expenses");
        Assert(FindButton(owner, "+ Create Expense") is not null, "Create Expense is clickable");
        await CheckFormAsync(app, owner, "+ Create Expense", "Create Expense", "Save Expense", null);

        foreach (var route in new[] { "pos", "tables", "kitchen", "closing", "users" })
            await NavigateAsync(owner, route);

        // A second real shell tests role visibility and command-level denial.
        var waiter = OpenWindow("waiter");
        waiter.Show();
        var waiterVm = (MainWindowViewModel)waiter.DataContext;
        await WaitUntilAsync(() => waiterVm.CurrentPage is not null || waiterVm.HasWorkspaceError,
            "waiter Dashboard render");
        Assert(!waiterVm.HasWorkspaceError, "Waiter Dashboard loads");
        foreach (var restricted in new[] { "menu", "inventory", "purchases", "expenses", "users", "settings" })
        {
            var nav = FindNavigation(waiter, restricted);
            Assert(nav is not null && nav.Visibility != Visibility.Visible,
                $"Waiter cannot see {restricted} navigation");
        }

        var initialRoute = waiterVm.CurrentRoute;
        await waiterVm.NavigateCommand.ExecuteAsync("purchases");
        Assert(waiterVm.CurrentRoute == initialRoute && !waiterVm.HasWorkspaceError,
            "Waiter cannot open Purchases by directly invoking route command");

        waiter.Close();
        owner.Close();
    }

    private static void CheckNavigation(MainWindow window, string route)
    {
        var button = FindNavigation(window, route);
        Assert(button is not null && button.IsEnabled && button.Visibility == Visibility.Visible,
            $"Owner navigation button '{route}' is available");
    }

    private static async Task NavigateAsync(MainWindow window, string route)
    {
        var vm = (MainWindowViewModel)window.DataContext;
        var button = FindNavigation(window, route)
            ?? throw new InvalidOperationException($"Navigation button '{route}' not found.");
        var previousPage = vm.CurrentPage;
        await WaitUntilAsync(() => button.Command?.CanExecute(button.CommandParameter) == true,
            $"'{route}' navigation command becomes available");
        Click(button);
        await WaitUntilAsync(() => vm.HasWorkspaceError ||
            (vm.CurrentRoute == route && vm.CurrentPage is not null &&
             !ReferenceEquals(previousPage, vm.CurrentPage)), $"click-to-navigate '{route}'");

        Assert(!vm.HasWorkspaceError, $"Navigation '{route}' renders without errors: {vm.WorkspaceErrorMessage}");
        Assert(vm.CurrentRoute == route && vm.CurrentPage is not null,
            $"Navigation '{route}' updates the rendered workspace");
    }

    private static async Task CheckFormAsync(
        System.Windows.Application app, MainWindow main, string createLabel, string dialogTitle,
        string saveLabel, string? expectedError)
    {
        var create = FindButton(main, createLabel)
            ?? throw new InvalidOperationException($"Missing '{createLabel}' on current workspace.");
        Assert(create.IsEnabled, $"'{createLabel}' is enabled");

        var present = app.Windows.Cast<Window>().ToHashSet();
        Window? modal = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        timer.Tick += (_, _) =>
        {
            modal = app.Windows.Cast<Window>().FirstOrDefault(x =>
                !present.Contains(x) && x.IsVisible && x.Title == dialogTitle);
            if (modal is not null) timer.Stop();
        };
        timer.Start();

        // Schedule the click instead of invoking ShowDialog inline: WPF's nested
        // dispatcher frame can then show the modal while this runner awaits it.
        _ = main.Dispatcher.BeginInvoke(new Action(() => Click(create)), DispatcherPriority.Normal);
        try
        {
            await WaitUntilAsync(() => modal is not null, $"'{dialogTitle}' modal opened");
            var opened = modal!;
            Assert(FindButton(opened, saveLabel) is { IsEnabled: true },
                $"'{dialogTitle}' contains enabled '{saveLabel}'");
            Assert(Descendants<TextBox>(opened).Any(),
                $"'{dialogTitle}' contains editable input fields");
            if (expectedError is not null)
            {
                Click(FindButton(opened, saveLabel)!);
                await WaitUntilAsync(() => Descendants<TextBlock>(opened)
                        .Any(x => x.Text.Contains(expectedError, StringComparison.Ordinal)),
                    $"'{dialogTitle}' reports invalid input without saving");
                Assert(opened.IsVisible, $"'{dialogTitle}' stays open on invalid input");
            }
        }
        finally
        {
            timer.Stop();
            if (modal?.IsVisible == true) modal.Close();
        }
    }

    private static Button? FindNavigation(DependencyObject root, string route) =>
        Descendants<Button>(root).FirstOrDefault(button =>
            string.Equals(button.CommandParameter as string, route, StringComparison.Ordinal));

    private static Button? FindButton(DependencyObject root, string label) =>
        Descendants<Button>(root).FirstOrDefault(button =>
            string.Equals(button.Content?.ToString(), label, StringComparison.Ordinal));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var descendant in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
                yield return descendant;
        }
    }

    private static void Click(Button button)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Cannot invoke a disabled button.");

        // ButtonBase.OnClick invokes the Click handlers, then the bound ICommand.
        // Raising the routed event alone does not execute the navigation binding.
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        var command = button.Command;
        if (command is not null && command.CanExecute(button.CommandParameter))
            command.Execute(button.CommandParameter);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate() && clock.Elapsed < ActionTimeout)
            await Task.Delay(80);

        if (!predicate())
            throw new TimeoutException($"Timed out after {ActionTimeout.TotalSeconds} sec waiting for {description}.");
    }
}

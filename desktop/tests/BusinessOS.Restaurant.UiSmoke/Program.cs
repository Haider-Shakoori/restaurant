using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // Every image is rendered from the real WPF window on an isolated Windows CI
    // runner. Synthetic staff credentials and an empty local SQLite store are used;
    // no production license, user data or privileged credentials are captured.
    private static string GalleryDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("RUNNER_TEMP")
            ?? throw new InvalidOperationException("RUNNER_TEMP missing."),
        "restaurant-desktop-screenshots");

    private static readonly string[] GalleryRoutes =
    [
        "dashboard", "pos", "tables", "kitchen", "menu", "inventory",
        "purchases", "expenses", "closing", "reports", "users", "settings",
    ];

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
        // Load the exact same controls and implicit text styling as App.xaml:
        // screenshots must exercise production button contrast, not WPF defaults.
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/BusinessOS.Restaurant.Desktop;component/Styles/Buttons.xaml"),
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
                PrintDiagnostics();
                application.Shutdown(1);
            }
        };

        return application.Run(owner);
    }

    private static AuthSession TestSession(string role) =>
        new(
            "https://restaurant-ci.example.test",
            "not-a-real-access-token",
            new AuthUser(901, "ci-test-operator", "CI " + role, "ci@example.test", role),
            "isolated-test-tenant",
            DateTimeOffset.UtcNow);

    private static void PrintDiagnostics()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BusinessOS", "Restaurant", "logs");
            if (!Directory.Exists(directory)) return;
            foreach (var log in Directory.GetFiles(directory, "desktop-*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Take(2))
            {
                Console.Error.WriteLine("Recent desktop diagnostic: " + Path.GetFileName(log));
                foreach (var line in File.ReadLines(log).TakeLast(75))
                    Console.Error.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not read CI diagnostic log: " + ex.Message);
        }
    }

    private static MainWindow OpenWindow(string role) =>
        new(TestSession(role))
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 800,
            Left = 50,
            Top = 50,
        };

    private static async Task RunAsync(System.Windows.Application app, MainWindow owner)
    {
        // Operational workspaces read the protected Windows operator session,
        // not just the window DataContext. Create only synthetic CI credentials
        // on this disposable runner; production startup remains unchanged.
        var sessions = new WindowsSessionStore();
        await sessions.SaveAsync(TestSession("owner"));
        var vm = (MainWindowViewModel)owner.DataContext;
        await WaitUntilAsync(() => vm.CurrentPage is not null || vm.HasWorkspaceError,
            "initial Dashboard render");

        Assert(!vm.HasWorkspaceError, "Dashboard opens without an error");
        await CaptureGalleryAsync(owner);
        await CheckButtonTextInheritanceAsync(owner);
        await CaptureAuthWindowsAsync(owner);
        CheckNavigation(owner, "menu");
        CheckNavigation(owner, "inventory");
        CheckNavigation(owner, "purchases");
        CheckNavigation(owner, "expenses");
        CheckNavigation(owner, "pos");
        CheckNavigation(owner, "kitchen");

        // The reported issue was inside the Glass-mode cloud-management dialog.
        // Open the real Dining Floors tab under Glass, check contrast, capture
        // it, and confirm a theme switch never leaves dark text on dark buttons.
        ThemeManager.Apply(AppearanceTheme.Glass);
        await NavigateAsync(owner, "tables");
        await CheckFormAsync(app, owner, "+ Add / Edit Floors & Tables",
            "Restaurant · Catalog, Floors & Inventory", "Create", null);
        ThemeManager.Apply(AppearanceTheme.Classic);

        await NavigateAsync(owner, "menu");
        await CheckFormAsync(app, owner, "+ Add Menu Item (Tenant)",
            "Restaurant · Catalog, Floors & Inventory", "Create", null);

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
        await CheckFormAsync(app, owner, "Add Users / Manage Roles",
            "Restaurant · Users & Roles", "Create account", null);

        // A second real shell tests role visibility and command-level denial.
        await sessions.SaveAsync(TestSession("waiter"));
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
        await sessions.ClearAsync();
    }

    private static async Task CaptureGalleryAsync(MainWindow window)
    {
        var directory = GalleryDirectory;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.txt"),
            "BusinessOS Restaurant Desktop CI screenshots\n" +
            "Source commit: " + (Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unknown") + "\n" +
            "These PNGs are actual WPF-rendered windows from GitHub-hosted Windows Actions.\n" +
            "Screens use a synthetic operator and a fresh empty SQLite test database.\n" +
            "No production license or restaurant data was used.\n" +
            "The runner is not a physical 4K workstation, thermal printer, or LAN tablet.\n" +
            "Classic and Glass contain all 12 operational navigation pages.\n" +
            "Dialog screenshots are also captured during existing WPF smoke interactions.\n");

        // A reproducible in-app size avoids dependence on the hosted runner's
        // virtual desktop resolution. This is NOT a hardware/4K DPI acceptance test.
        window.WindowState = WindowState.Normal;
        window.Width = 1440;
        window.Height = 900;
        foreach (var (theme, label) in new[]
        {
            (AppearanceTheme.Classic, "classic"),
            (AppearanceTheme.Glass, "glass"),
        })
        {
            ThemeManager.Apply(theme);
            for (var index = 0; index < GalleryRoutes.Length; index++)
            {
                var route = GalleryRoutes[index];
                await NavigateAsync(window, route);
                if (route is "settings" or "kitchen")
                    AssertWorkspaceGlassSurface(window, theme, route);
                AssertReadableActionButtons(window, label + "/" + route,
                    requireButtons: route is "tables" or "menu" or "pos");
                await CaptureWindowAsync(window,
                    Path.Combine(directory, label, $"{index + 1:00}-{route}.png"));
            }
        }

        ThemeManager.Apply(AppearanceTheme.Classic);
        window.Width = 1280;
        window.Height = 800;
        await NavigateAsync(window, "dashboard");
        Console.WriteLine("Gallery captured: 24 operational pages across Classic and Glass themes.");
    }

    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var linear = value / 255d;
            return linear <= 0.04045 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var light = Math.Max(Luminance(a), Luminance(b));
        var dark = Math.Min(Luminance(a), Luminance(b));
        return (light + 0.05) / (dark + 0.05);
    }

    private static void AssertReadableActionButtons(
        DependencyObject root, string location, bool requireButtons = true)
    {
        var template = System.Windows.Application.Current.FindResource("PremiumActionTemplate") as ControlTemplate;
        Assert(template is not null, "real WPF action template is loaded");
        var buttons = Descendants<Button>(root)
            .Where(button => button.IsVisible && button.IsEnabled &&
                ReferenceEquals(button.Template, template))
            .ToArray();
        if (requireButtons)
            Assert(buttons.Length > 0, location + " has themed action buttons");

        foreach (var button in buttons)
        {
            if (button.Background is not SolidColorBrush background ||
                button.Foreground is not SolidColorBrush foreground)
                throw new InvalidOperationException("Expected solid themed button brushes for " + button.Content);
            var ratio = ContrastRatio(foreground.Color, background.Color);
            Assert(ratio >= 4.5,
                $"{location}: '{button.Content}' text/background contrast {ratio:0.00}:1 passes AA");
        }
    }

    private static async Task CheckButtonTextInheritanceAsync(MainWindow owner)
    {
        var label = new TextBlock { Text = "Readable label" };
        var primary = new Button { Content = label };
        var secondaryLabel = new TextBlock { Text = "Neutral action" };
        var secondary = new Button { Content = secondaryLabel };
        secondary.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(primary);
        panel.Children.Add(secondary);
        var probe = new Window
        {
            Title = "Button theme regression",
            Owner = owner,
            Width = 430,
            Height = 200,
            Content = panel,
        };
        try
        {
            probe.Show();
            foreach (var theme in new[] { AppearanceTheme.Classic, AppearanceTheme.Glass })
            {
                ThemeManager.Apply(theme);
                await probe.Dispatcher.InvokeAsync(probe.UpdateLayout, DispatcherPriority.Render);
                AssertReadableActionButtons(probe, "probe/" + theme);
                foreach (var button in new[] { primary, secondary })
                {
                    var text = (TextBlock)button.Content;
                    Assert(text.Foreground is SolidColorBrush && button.Foreground is SolidColorBrush &&
                           ((SolidColorBrush)text.Foreground).Color == ((SolidColorBrush)button.Foreground).Color,
                        theme + " nested TextBlock uses Button.Foreground instead of global TextPrimaryBrush");
                }
            }
        }
        finally
        {
            probe.Close();
            ThemeManager.Apply(AppearanceTheme.Classic);
        }
    }

    private static void AssertWorkspaceGlassSurface(MainWindow window, AppearanceTheme theme, string route)
    {
        var surfaces = Descendants<Border>(window)
            .Where(x => x.Name == "WorkspaceFrostedContent").ToArray();
        Assert(surfaces.Length == 1, $"{route} owns exactly one frosted workspace surface");

        var surface = surfaces[0];
        Assert(surface.Background is SolidColorBrush,
            $"{route} background follows a live theme resource");
        var brush = (SolidColorBrush)surface.Background;
        if (theme == AppearanceTheme.Glass)
        {
            Assert(brush.Color.A >= 180 && brush.Color.A < 255,
                $"{route} has translucent white glass in Glass mode");
            Assert(surface.Padding.Left >= 18,
                $"{route} glass content is padded");
        }
        else
        {
            Assert(brush.Color.A == 0,
                $"{route} is transparent in Classic mode");
            Assert(surface.Padding.Left == 0,
                $"{route} retains Classic content spacing");
        }
    }

    private static async Task CaptureAuthWindowsAsync(MainWindow owner)
    {
        var signIn = new OperatorSignInWindow(
            "isolated-test-tenant", "https://restaurant-ci.example.test")
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        try
        {
            signIn.Show();
            await CaptureWindowAsync(signIn, Path.Combine(GalleryDirectory, "authentication", "operator-sign-in.png"));
        }
        finally
        {
            signIn.Close();
        }

        var activation = new ActivationWindow
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        try
        {
            activation.Show();
            await CaptureWindowAsync(activation, Path.Combine(GalleryDirectory, "authentication", "license-activation.png"));
        }
        finally
        {
            activation.Close();
        }
    }

    private static async Task CaptureWindowAsync(Window window, string destination)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(160); // allow nested WPF content and fonts to finish rendering

        // Capture WPF client content, including the shell's sidebar and toolbar.
        // RenderTargetBitmap works without a physical monitor/print device.
        var content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException("Screenshot window has no WPF content.");
        content.UpdateLayout();
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        if (width < 240 || height < 180)
            throw new InvalidOperationException(
                $"Screenshot '{Path.GetFileName(destination)}' has invalid layout {width}x{height}.");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var stream = File.Create(destination))
            encoder.Save(stream);

        if (new FileInfo(destination).Length < 2048)
            throw new InvalidOperationException("Screenshot appears empty: " + destination);
        Console.WriteLine($"SCREENSHOT: {Path.GetRelativePath(GalleryDirectory, destination)} ({width}x{height})");
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
            AssertReadableActionButtons(opened, "dialog/" + ThemeManager.Current);
            await CaptureWindowAsync(opened, Path.Combine(
                GalleryDirectory, "dialogs",
                ThemeManager.Current.ToString().ToLowerInvariant() + "-" +
                new string(dialogTitle.ToLowerInvariant().Select(c =>
                    char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()) + ".png"));
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
        var visited = new HashSet<DependencyObject>();
        return Traverse(root);

        IEnumerable<T> Traverse(DependencyObject node)
        {
            if (!visited.Add(node)) yield break;
            if (node is T match) yield return match;

            // Workspace pages and modal forms are generated dynamically.
            // Logical children exist before layout realizes the full visual tree.
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                foreach (var descendant in Traverse(child))
                    yield return descendant;
            }

            if (node is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    foreach (var descendant in Traverse(VisualTreeHelper.GetChild(node, i)))
                        yield return descendant;
                }
            }
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

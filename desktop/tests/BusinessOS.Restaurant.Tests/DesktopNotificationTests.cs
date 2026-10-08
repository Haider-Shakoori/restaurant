using System.Xml.Linq;
using BusinessOS.Restaurant.Desktop;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DesktopNotificationTests
{
    [Fact]
    public void Feed_tracks_unread_levels_and_marks_history_read()
    {
        var feed = new DesktopNoticeFeed();
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var success = feed.Publish(DesktopNoticeLevel.Success, "Payment posted", now);
        var error = feed.Publish(DesktopNoticeLevel.Error, "Printer unavailable", now.AddSeconds(1));
        Assert.NotNull(success);
        Assert.Equal("Success", success.Kind);
        Assert.Equal("Action needs attention", error?.Heading);
        Assert.Equal(2, feed.UnreadCount);
        Assert.Equal("Printer unavailable", feed.History[0].Message);
        feed.MarkAllRead();
        Assert.Equal(0, feed.UnreadCount);
        Assert.All(feed.History, notice => Assert.True(notice.IsRead));
    }

    [Fact]
    public void Feed_deduplicates_repeated_errors_and_bounds_history()
    {
        var feed = new DesktopNoticeFeed();
        var now = DateTimeOffset.UtcNow;
        Assert.Null(feed.Publish(DesktopNoticeLevel.Info, "   ", now));
        var first = feed.Publish(DesktopNoticeLevel.Error, "KDS offline", now);
        Assert.NotNull(first);
        Assert.Null(feed.Publish(DesktopNoticeLevel.Error, "KDS offline", now.AddSeconds(2)));
        Assert.Equal(1, feed.History.Count);
        Assert.NotNull(feed.Publish(DesktopNoticeLevel.Error, "KDS offline", now.AddSeconds(11)));
        for (var i = 0; i < 120; i++)
            feed.Publish(DesktopNoticeLevel.Info, $"Event {i}", now.AddMinutes(i + 1));
        Assert.Equal(100, feed.History.Count);
        Assert.Equal(100, feed.UnreadCount);
        Assert.Equal("Event 119", feed.History[0].Message);
    }

    [Fact]
    public void Shell_exposes_bell_popover_toasts_and_theme_palettes()
    {
        var shell = XDocument.Load(FileAt("src", "BusinessOS.Restaurant.Desktop", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Contains(shell.Descendants(), e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute(x + "Name") == "NotificationBellButton" &&
            (string?)e.Attribute("Command") == "{Binding ToggleNotificationsCommand}");
        Assert.Contains(shell.Descendants(), e => e.Name.LocalName == "Popup" &&
            ((string?)e.Attribute("IsOpen"))?.Contains("NotificationsOpen", StringComparison.Ordinal) == true);
        Assert.Contains(shell.Descendants(), e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding ToastNotifications}");
        foreach (var theme in new[] { "Glass.xaml", "Classic.xaml" })
        {
            var colors = XDocument.Load(FileAt("src", "BusinessOS.Restaurant.Desktop", "Themes", theme));
            foreach (var key in new[] { "ErrorBrush", "WarningBrush", "ErrorSoftBrush", "WarningSoftBrush" })
                Assert.Contains(colors.Descendants(), e => (string?)e.Attribute(x + "Key") == key);
        }
    }

    [Fact]
    public void Notifications_preserve_existing_checkout_and_printer_confirmation_dialogs()
    {
        var code = File.ReadAllText(FileAt("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));
        Assert.Contains("DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text)", code);
        Assert.Contains("DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message)", code);
        var printer = File.ReadAllText(FileAt("src", "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        Assert.Contains("Confirm printer recovery", printer);
        Assert.Contains("MessageBoxButton.YesNo", printer);

        var viewModel = File.ReadAllText(FileAt("src", "BusinessOS.Restaurant.Desktop", "MainWindowViewModel.cs"));
        Assert.Contains("ReleaseNotifications()", viewModel);
        Assert.Contains("DesktopNoticeEvents.Posted -= OnDesktopNotice;", viewModel);
        Assert.Contains("TimeSpan.FromSeconds(level == DesktopNoticeLevel.Error ? 9 : 5)", viewModel);
    }

    private static string FileAt(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException(), "desktop" }
            .Concat(parts).ToArray());
    }
}

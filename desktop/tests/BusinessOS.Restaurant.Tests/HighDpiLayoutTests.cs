using System.Xml.Linq;
using BusinessOS.Restaurant.Desktop;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class HighDpiLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(420)]
    [InlineData(980)]
    [InlineData(1900)]
    [InlineData(3500)]
    public void Hour_positions_fill_available_plot_without_deforming_glyphs(double width)
    {
        Assert.Equal(16, DashboardChartLayout.HourX(0, 24, width), 6);
        Assert.Equal(width - 16, DashboardChartLayout.HourX(23, 24, width), 6);
        Assert.Equal(width / 2, DashboardChartLayout.HourX(0, 1, width), 6);
        Assert.InRange(DashboardChartLayout.HourX(12, 24, width), 16, width - 16);
        Assert.Equal(width - 14, DashboardChartLayout.GuideEnd(width), 6);
    }

    [Fact]
    public void Four_k_shell_limits_workspace_width_without_scaling_foreground()
    {
        var shell = XDocument.Load(FileAt("src", "BusinessOS.Restaurant.Desktop", "MainWindow.xaml"));
        var image = shell.Descendants().Single(e => e.Name.LocalName == "Image" &&
            (string?)e.Attribute(X + "Name") == "RestaurantBackdrop");
        Assert.Equal("UniformToFill", (string?)image.Attribute("Stretch"));
        var cache = image.Descendants().Single(e => e.Name.LocalName == "BitmapCache");
        Assert.Equal("0.5", (string?)cache.Attribute("RenderAtScale"));

        var workspace = shell.Descendants().Single(e => e.Name.LocalName == "ContentControl" &&
            ((string?)e.Attribute("Content"))?.Contains("CurrentPage", StringComparison.Ordinal) == true);
        Assert.Equal("1900", (string?)workspace.Attribute("MaxWidth"));
        Assert.Equal("Stretch", (string?)workspace.Attribute("HorizontalAlignment"));
        Assert.DoesNotContain(shell.Descendants(), e => e.Name.LocalName == "Viewbox");
    }

    [Fact]
    public void Sales_chart_updates_canvas_coordinates_without_a_stretch_fill_viewbox()
    {
        var source = File.ReadAllText(FileAt("src", "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        var start = source.IndexOf("private static UIElement BuildOperationsOverview(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private static UIElement", start + 2, StringComparison.Ordinal);
        var chart = source.Substring(start, (end < 0 ? source.Length : end) - start);
        Assert.Contains("plot.SizeChanged +=", chart);
        Assert.Contains("DashboardChartLayout.HourX", chart);
        Assert.Contains("StrokeThickness = 3", chart);
        Assert.Contains("Width = 9, Height = 9", chart);
        Assert.DoesNotContain("Stretch = Stretch.Fill", chart);
    }

    private static string FileAt(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "desktop")))
            directory = directory.Parent;
        return Path.Combine(new[] { directory?.FullName ?? throw new DirectoryNotFoundException(), "desktop" }
            .Concat(parts).ToArray());
    }
}

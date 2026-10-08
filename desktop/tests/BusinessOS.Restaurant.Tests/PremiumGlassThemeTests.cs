using System.Xml.Linq;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class PremiumGlassThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Both_themes_define_matching_nested_surface_resources()
    {
        var classic = Load("Themes", "Classic.xaml");
        var glass = Load("Themes", "Glass.xaml");
        var keys = new[]
        {
            "GridRowBackgroundBrush",
            "GridAlternatingRowBackgroundBrush",
            "GridHeaderBackgroundBrush",
            "GridSelectedRowBrush",
            "TopBarActionBrush",
        };

        foreach (var key in keys)
        {
            var opaque = BrushColor(classic, key);
            var translucent = BrushColor(glass, key);
            Assert.True(opaque.Length == 7 || (opaque.Length == 9 && opaque.StartsWith("#FF", StringComparison.OrdinalIgnoreCase)),
                $"Classic nested brush {key} must be fully opaque, got {opaque}");
            Assert.StartsWith("#", translucent);
            Assert.Equal(9, translucent.Length); // #AARRGGBB
            Assert.False(translucent.StartsWith("#FF", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Grids_and_shell_follow_the_live_theme_instead_of_hard_coded_white()
    {
        var app = Load("App.xaml");
        var markup = app.ToString();
        Assert.Contains("{DynamicResource GridRowBackgroundBrush}", markup);
        Assert.Contains("{DynamicResource GridAlternatingRowBackgroundBrush}", markup);
        Assert.Contains("{DynamicResource GridHeaderBackgroundBrush}", markup);
        Assert.Contains("{DynamicResource GridSelectedRowBrush}", markup);

        var main = Load("MainWindow.xaml");
        var ghost = main.Descendants().Single(e =>
            e.Name.LocalName == "Style" && (string?)e.Attribute(X + "Key") == "TopBarGhostButton");
        Assert.Contains("{DynamicResource TopBarActionBrush}", ghost.ToString());
        var image = main.Descendants().Single(e =>
            e.Name.LocalName == "Image" && (string?)e.Attribute(X + "Name") == "RestaurantBackdrop");
        Assert.Equal("UniformToFill", (string?)image.Attribute("Stretch"));
        Assert.Equal("HighQuality", image.Attributes().Single(a => a.Name.LocalName == "RenderOptions.BitmapScalingMode").Value);
    }

    [Fact]
    public void Responsive_windows_and_login_preserve_glass_background_and_navigation_controls()
    {
        var main = Load("MainWindow.xaml");
        Assert.Equal("Maximized", (string?)main.Root?.Attribute("WindowState"));
        Assert.Equal("OnWindowSizeChanged", (string?)main.Root?.Attribute("SizeChanged"));
        Assert.Contains(main.Descendants(), x => x.Name.LocalName == "Border" &&
            (string?)x.Attribute(X + "Name") == "OperatorBadge");
        Assert.Contains(main.Descendants(), x => x.Name.LocalName == "Button" &&
            (string?)x.Attribute(X + "Name") == "SwitchOperatorButton");

        var login = Load("OperatorSignInWindow.xaml");
        Assert.Contains(login.Descendants(), x => x.Name.LocalName == "ScrollViewer");
        Assert.Contains(login.Descendants(), x => x.Name.LocalName == "PasswordBox" &&
            (string?)x.Attribute(X + "Name") == "PasswordInput");

        var glass = Load("Themes", "Glass.xaml");
        var backdropBlur = glass.Descendants().Single(x => x.Name.LocalName == "BlurEffect" &&
            (string?)x.Attribute(X + "Key") == "BackgroundBlurEffect");
        Assert.Equal("24", (string?)backdropBlur.Attribute("Radius"));

        var desktop = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
            Path.Combine(RepositoryRoot(), "desktop", "src", "BusinessOS.Restaurant.Desktop", "MainWindow.xaml")))!,
            "MainWindow.xaml.cs"));
        Assert.Contains("ApplyResponsiveLayout()", desktop, StringComparison.Ordinal);
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "app.manifest"));
        Assert.Contains("PerMonitorV2", manifest, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string BrushColor(XDocument document, string key) =>
        (string?)document.Descendants().Single(e =>
            e.Name.LocalName == "SolidColorBrush" && (string?)e.Attribute(X + "Key") == key)
            .Attribute("Color") ?? throw new InvalidOperationException(key);

    private static XDocument Load(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        return XDocument.Load(Path.Combine(new[] { root, "desktop", "src", "BusinessOS.Restaurant.Desktop" }.Concat(parts).ToArray()));
    }
}

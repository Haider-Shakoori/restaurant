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

    [Fact]
    public void Kitchen_and_settings_use_theme_aware_white_glass_workspace()
    {
        var classic = Load("Themes", "Classic.xaml");
        var glass = Load("Themes", "Glass.xaml");
        Assert.Equal("#00FFFFFF", BrushColor(classic, "WorkspaceFrostedBackgroundBrush"));
        Assert.Equal("#D9F8FBFF", BrushColor(glass, "WorkspaceFrostedBackgroundBrush"));

        var desktopRoot = Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop");
        var kitchen = File.ReadAllText(Path.Combine(desktopRoot, "OperationalActionViews.cs"));
        var settings = File.ReadAllText(Path.Combine(desktopRoot, "RestaurantOperationalPages.cs"));
        var wrapper = File.ReadAllText(Path.Combine(desktopRoot, "WorkspaceFrostedSurface.cs"));
        Assert.Contains("Content = WorkspaceFrostedSurface.Wrap(root)", kitchen, StringComparison.Ordinal);
        Assert.Contains("return Scroll(WorkspaceFrostedSurface.Wrap(panel))", settings, StringComparison.Ordinal);
        Assert.Contains("WorkspaceFrostedBackgroundBrush", wrapper, StringComparison.Ordinal);
        Assert.Contains("SetResourceReference(Border.PaddingProperty", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void Classic_and_Glass_buttons_have_distinct_accessible_palettes_and_real_text_inheritance()
    {
        var classic = Load("Themes", "Classic.xaml");
        var glass = Load("Themes", "Glass.xaml");

        Assert.Equal("#2563EB", BrushColor(classic, "ButtonPrimaryBrush"));
        Assert.Equal("#F3C969", BrushColor(glass, "ButtonPrimaryBrush"));
        Assert.Equal("#FFFFFF", BrushColor(classic, "ButtonForegroundBrush"));
        Assert.Equal("#111827", BrushColor(glass, "ButtonForegroundBrush"));

        foreach (var theme in new[] { classic, glass })
        {
            foreach (var (backgroundKey, foregroundKey) in new[]
            {
                ("ButtonPrimaryBrush", "ButtonForegroundBrush"),
                ("ButtonHoverBrush", "ButtonForegroundBrush"),
                ("ButtonPressedBrush", "ButtonForegroundBrush"),
                ("ButtonSecondaryBackgroundBrush", "ButtonSecondaryForegroundBrush"),
                ("ButtonSecondaryHoverBrush", "ButtonSecondaryForegroundBrush"),
                ("ButtonSecondaryPressedBrush", "ButtonSecondaryForegroundBrush"),
                ("ButtonDisabledBackgroundBrush", "ButtonDisabledForegroundBrush"),
            })
            {
                var background = BrushColor(theme, backgroundKey);
                var foreground = BrushColor(theme, foregroundKey);
                Assert.True(ColorContrast(background, foreground) >= 4.5,
                    $"Poor contrast between {backgroundKey} {background} and {foregroundKey} {foreground}.");
            }
        }

        var app = Load("App.xaml");
        Assert.Contains(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary" &&
            (string?)element.Attribute("Source") == "Styles/Buttons.xaml");
        var buttons = Load("Styles", "Buttons.xaml");
        var markup = buttons.ToString();
        Assert.Contains("TextElement.Foreground", markup);
        Assert.Contains("AncestorType={x:Type Button}", markup);
        Assert.Contains("PremiumActionTemplate", markup);
        Assert.Contains("PrimaryActionButton", markup);
        Assert.Contains("SecondaryActionButton", markup);
        Assert.Contains("ButtonSecondaryHoverBrush", markup);
    }

    private static double ColorContrast(string background, string foreground)
    {
        static double Channel(string hex, int start)
        {
            var value = Convert.ToInt32(hex.Substring(start, 2), 16) / 255d;
            return value <= 0.04045 ? value / 12.92 :
                Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(string hex)
        {
            var offset = hex.Length == 9 ? 3 : 1;
            return 0.2126 * Channel(hex, offset) +
                   0.7152 * Channel(hex, offset + 2) +
                   0.0722 * Channel(hex, offset + 4);
        }

        var a = Luminance(background);
        var b = Luminance(foreground);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
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

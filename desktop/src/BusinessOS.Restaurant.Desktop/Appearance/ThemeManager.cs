using System.Windows;

namespace BusinessOS.Restaurant.Desktop.Appearance;

public static class ThemeManager
{
    private const string GlassSource = "Themes/Glass.xaml";

    public static AppearanceTheme Current { get; private set; } = AppearanceTheme.Glass;

    public static event Action<AppearanceTheme>? ThemeChanged;

    public static void Apply(AppearanceTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            Current = theme;
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source is not null &&
            dictionary.Source.OriginalString.EndsWith(GlassSource, StringComparison.OrdinalIgnoreCase));

        if (theme == AppearanceTheme.Glass)
        {
            if (existing is null)
            {
                var assembly = typeof(ThemeManager).Assembly.GetName().Name;
                dictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        $"pack://application:,,,/{assembly};component/{GlassSource}",
                        UriKind.Absolute),
                });
            }
        }
        else if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        Current = theme;
        ThemeChanged?.Invoke(theme);
    }

    public static AppearanceTheme Toggle()
    {
        var next = Current == AppearanceTheme.Glass
            ? AppearanceTheme.Classic
            : AppearanceTheme.Glass;
        Apply(next);
        return next;
    }
}

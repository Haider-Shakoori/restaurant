using System.Globalization;
using System.Windows.Data;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Highlights a top-navigation dropdown while the selected workspace belongs
/// to its group. The navigation command remains authoritative; this converter
/// only changes presentation.
/// </summary>
public sealed class NavigationGroupMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Length == 2 &&
               values[0] is string selected &&
               values[1] is string group &&
               group.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(candidate => string.Equals(selected, candidate, StringComparison.OrdinalIgnoreCase));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Navigation group highlighting is one-way.");
}

using System.Globalization;
using System.Windows.Data;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Matches the selected workspace to each sidebar button's existing route parameter.
/// The navigation command remains authoritative; this converter only changes presentation.
/// </summary>
public sealed class NavigationRouteMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Length == 2 &&
               values[0] is string selected &&
               values[1] is string candidate &&
               string.Equals(selected, candidate, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Navigation route highlighting is one-way.");
}

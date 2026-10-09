namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Resolution-independent dashboard chart geometry in WPF device-independent pixels.
/// The chart stretches horizontally without stretching strokes, text, or point markers.
/// </summary>
internal static class DashboardChartLayout
{
    private const double HorizontalInset = 16;

    public static double GuideEnd(double width) => Math.Max(14, width - 14);

    public static double HourX(int hour, int count, double width)
    {
        if (count <= 1)
            return Math.Max(0, width) / 2;

        var span = Math.Max(0, width - HorizontalInset * 2);
        return HorizontalInset + Math.Clamp(hour, 0, count - 1) * span / (count - 1);
    }
}

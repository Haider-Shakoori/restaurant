using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Unified frosted white content surface for Settings and Kitchen/KOT.
/// Dynamic resources react immediately to Classic/Glass switches without
/// reloading KDS or interrupting its live kitchen polling.
/// </summary>
internal static class WorkspaceFrostedSurface
{
    public static Border Wrap(UIElement content)
    {
        var surface = new Border
        {
            Name = "WorkspaceFrostedContent",
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
        };
        surface.SetResourceReference(Border.BackgroundProperty, "WorkspaceFrostedBackgroundBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "WorkspaceFrostedBorderBrush");
        surface.SetResourceReference(Border.PaddingProperty, "WorkspaceFrostedPadding");
        return surface;
    }
}

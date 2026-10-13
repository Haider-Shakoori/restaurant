using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Top-down dining furniture with a visible tabletop and surrounding chairs.
/// Uses stock WPF vectors (no remote assets, fonts or image dependencies).
/// Seats scale from 1 to 10 visible chairs; the actual capacity is in the
/// accessible label and on the table so large banqueting tables remain clear.
/// </summary>
internal static class RestaurantTableFurniture
{
    internal static FrameworkElement Create(string tableName, int capacity, Color stateColor)
    {
        const double width = 166;
        const double height = 106;
        var seats = Math.Clamp(capacity, 1, 10);
        var round = capacity <= 4;
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 2, 0, 4),
            IsHitTestVisible = false,
        };

        var chairBrush = new SolidColorBrush(Color.FromArgb(150, stateColor.R, stateColor.G, stateColor.B));
        var tabletop = new SolidColorBrush(Color.FromArgb(52, stateColor.R, stateColor.G, stateColor.B));
        var rim = new SolidColorBrush(stateColor);

        // Seats form an actual floor-plan silhouette rather than a list icon.
        for (var i = 0; i < seats; i++)
        {
            var angle = -Math.PI / 2 + i * (2 * Math.PI / seats);
            var chair = new Ellipse
            {
                Width = 17, Height = 17,
                Fill = chairBrush,
                Stroke = rim,
                StrokeThickness = 1.4,
            };
            Canvas.SetLeft(chair, width / 2 + Math.Cos(angle) * (round ? 58 : 69) - chair.Width / 2);
            Canvas.SetTop(chair, height / 2 + Math.Sin(angle) * (round ? 39 : 42) - chair.Height / 2);
            canvas.Children.Add(chair);
        }

        Shape surface = round
            ? new Ellipse { Width = 91, Height = 66 }
            : new Rectangle { Width = 116, Height = 66, RadiusX = 20, RadiusY = 20 };
        surface.Fill = tabletop;
        surface.Stroke = rim;
        surface.StrokeThickness = 2.5;
        Canvas.SetLeft(surface, (width - surface.Width) / 2);
        Canvas.SetTop(surface, (height - surface.Height) / 2);
        canvas.Children.Add(surface);

        var capacityText = new TextBlock
        {
            Text = $"{capacity} seats",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = rim,
            TextAlignment = TextAlignment.Center,
            Width = 90,
        };
        Canvas.SetLeft(capacityText, (width - capacityText.Width) / 2);
        Canvas.SetTop(capacityText, (height - 20) / 2);
        canvas.Children.Add(capacityText);
        System.Windows.Automation.AutomationProperties.SetName(canvas, $"{tableName}, {capacity} seats");
        return canvas;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Applies the same translucent iOS-inspired backdrop to native WPF dialogs.
/// The owner is blurred only while the dialog is visible, and its original
/// effect is restored in finally even on cancellation or exceptions.
/// File pickers and OS credential windows intentionally remain native.
/// </summary>
internal static class DesktopGlassDialog
{
    public static bool? ShowDialog(Window dialog)
    {
        var owner = dialog.Owner ?? System.Windows.Application.Current?.Windows
            .OfType<Window>().FirstOrDefault(x => x.IsActive && !ReferenceEquals(x, dialog))
            ?? System.Windows.Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, dialog) && owner.IsLoaded)
        {
            if (dialog.Owner is null) dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else if (dialog.WindowStartupLocation == WindowStartupLocation.Manual)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Never override dialog content templates or command controls.
        // A translucent frosted surface envelopes the existing forms.
        if (dialog.Content is UIElement content && content is not Border { Name: "DesktopGlassModalBody" })
        {
            dialog.Content = null;
            dialog.Content = new Border
            {
                Name = "DesktopGlassModalBody",
                Background = new SolidColorBrush(Color.FromArgb(246, 246, 249, 254)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = content,
            };
        }
        dialog.Background = new SolidColorBrush(Color.FromArgb(250, 242, 247, 252));

        // This is intentionally the owner's *current content*, rather than
        // overwriting the owner's Window.Effect or installing a persistent
        // application-wide blur that could degrade kitchen performance.
        var backdrop = owner?.Content as UIElement;
        var originalEffect = backdrop?.Effect;
        try
        {
            if (backdrop is not null)
                backdrop.Effect = new BlurEffect
                {
                    Radius = 11,
                    RenderingBias = RenderingBias.Performance,
                    KernelType = KernelType.Gaussian,
                };
            return dialog.ShowDialog();
        }
        finally
        {
            if (backdrop is not null) backdrop.Effect = originalEffect;
        }
    }
}

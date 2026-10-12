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
    public static bool Confirm(string message, string title, Window? owner = null)
    {
        var dialog = BuildPrompt(message, title, confirmation: true, owner);
        return ShowDialog(dialog) == true;
    }

    public static void Notice(string message, string title, Window? owner = null)
    {
        var dialog = BuildPrompt(message, title, confirmation: false, owner);
        ShowDialog(dialog);
    }

    private static Window BuildPrompt(string message, string title, bool confirmation, Window? owner)
    {
        var content = new StackPanel { Margin = new Thickness(23, 22, 23, 18) };
        content.Children.Add(new TextBlock
        {
            Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        content.Children.Add(new TextBlock
        {
            Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22),
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = confirmation ? "Cancel" : "OK",
            MinWidth = 100, Height = 38, Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };
        actions.Children.Add(cancel);
        var dialog = new Window
        {
            Title = title, Width = 530, SizeToContent = SizeToContent.Height,
            MaxHeight = 440, MinHeight = 160,
            ResizeMode = ResizeMode.NoResize,
            Content = new ScrollViewer
            {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        if (owner is not null) dialog.Owner = owner;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        if (confirmation)
        {
            var proceed = new Button
            {
                Content = "Confirm", MinWidth = 110, Height = 38, IsDefault = true,
            };
            proceed.Click += (_, _) => dialog.DialogResult = true;
            actions.Children.Add(proceed);
        }
        content.Children.Add(actions);
        return dialog;
    }

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
        // A window must never blur itself: at startup the sign-in window is
        // also the active window, and self-blurring made the login unreadable.
        var backdrop = owner is not null && !ReferenceEquals(owner, dialog)
            ? owner.Content as UIElement
            : null;
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

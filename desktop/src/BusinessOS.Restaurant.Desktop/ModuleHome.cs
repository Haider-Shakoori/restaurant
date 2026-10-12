using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BusinessOS.Restaurant.Authentication;

namespace BusinessOS.Restaurant.Desktop;

internal static class ModuleHome
{
    public static FrameworkElement Create(string role, ICommand navigate)
    {
        var cards = new WrapPanel { Margin = new Thickness(22, 18, 12, 18) };
        // Large native Windows glyphs: crisp at any size, no extra downloads.
        (string Route, string Glyph, string Title)[] modules =
        [
            ("dashboard", "\uE80F", "Dashboard"),
            ("pos", "\uE7BF", "POS & Orders"),
            ("tables", "\uE71D", "Tables & Floor"),
            ("kitchen", "\uE945", "Kitchen / KOT"),
            ("menu", "\uE8A5", "Menu & Products"),
            ("recipes", "\uE8EF", "Recipes"),
            ("inventory", "\uE7B8", "Inventory"),
            ("purchases", "\uE719", "Purchases"),
            ("expenses", "\uE787", "Expenses"),
            ("closing", "\uE73E", "Daily Closing"),
            ("reports", "\uE9D9", "Reports"),
            ("users", "\uE77B", "Users & Roles"),
            ("settings", "\uE713", "Settings"),
        ];
        foreach (var module in modules.Where(m => RestaurantWorkspaceRoutes.CanOpen(role, m.Route)))
        {
            var icon = new TextBlock
            {
                Text = module.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 56,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            // Shared orange branding for every authorized module card.
            icon.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
            var title = new TextBlock
            {
                Text = module.Title,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            var content = new StackPanel();
            content.Children.Add(icon);
            content.Children.Add(title);
            var card = new Button
            {
                Content = content,
                Width = 258,
                Height = 182,
                Margin = new Thickness(10),
                Padding = new Thickness(22, 20, 18, 18),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Command = navigate,
                CommandParameter = module.Route,
            };
            card.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            cards.Children.Add(card);
        }

        return new ScrollViewer
        {
            Content = cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }
}

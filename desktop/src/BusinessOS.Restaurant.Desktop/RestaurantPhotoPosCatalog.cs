using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BusinessOS.Restaurant.Authentication;

namespace BusinessOS.Restaurant.Desktop;

internal sealed record PhotoPosItem(string Id, string Name, string Category, decimal Price, string? ImageUrl);

/// <summary>
/// Touch-friendly photo catalogue for the native Windows POS. Uses the SAME local
/// menu-item IDs as KOT/order entry. Images are public tenant media, saved
/// privately to a per-user cache for use when the Internet is unavailable.
/// </summary>
internal static class RestaurantPhotoPosCatalog
{
    private static readonly SemaphoreSlim DownloadSlots = new(3);
    private const int MaximumImageBytes = 3 * 1024 * 1024;

    public static FrameworkElement Build(
        IReadOnlyList<PhotoPosItem> items,
        TextBox search,
        Action<PhotoPosItem> onSelect)
    {
        var root = new StackPanel { Name = "PosPhotoCatalog", Margin = new Thickness(0, 8, 0, 12) };
        var title = new TextBlock { Text = "PHOTO MENU · TAP TO ADD", FontWeight = FontWeights.Bold,
            FontSize = 13, Margin = new Thickness(0, 8, 0, 10) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(title);
        var categoryFilters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var cards = new WrapPanel { Name = "PosPhotoMenuTiles" };
        var activeCategory = "All items";
        var tileControls = new List<(PhotoPosItem Item, Button Tile)>();

        void Filter()
        {
            var text = search.Text.Trim();
            foreach (var (item, tile) in tileControls)
                tile.Visibility = (activeCategory == "All items" ||
                    string.Equals(item.Category, activeCategory, StringComparison.OrdinalIgnoreCase)) &&
                    (item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                     item.Category.Contains(text, StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var category in new[] { "All items" }
            .Concat(items.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase)))
        {
            var filter = new Button { Content = category, MinHeight = 34, MinWidth = 74,
                Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 7, 7) };
            filter.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            filter.Click += (_, _) =>
            {
                activeCategory = category;
                Filter();
            };
            categoryFilters.Children.Add(filter);
        }
        root.Children.Add(categoryFilters);

        foreach (var item in items)
        {
            var stack = new StackPanel();
            var imageFrame = new Grid { Height = 104, Background = new SolidColorBrush(Color.FromRgb(232, 238, 244)) };
            var placeholder = new TextBlock { Text = "🍽", FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            imageFrame.Children.Add(placeholder);
            if (!string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                var image = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
                imageFrame.Children.Add(image);
                image.Loaded += async (_, _) =>
                {
                    try
                    {
                        var origin = (await new WindowsSessionStore().LoadAsync())?.TenantBaseUrl;
                        var location = await CacheMenuPhotoAsync(item.ImageUrl, origin);
                        if (location is null) return;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = location;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image.Source = bitmap;
                        image.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        // Missing internet or corrupt image: a usable dish tile remains.
                    }
                };
            }
            stack.Children.Add(imageFrame);
            var name = new TextBlock { Text = item.Name, FontSize = 13,
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                MaxHeight = 46, Margin = new Thickness(9, 9, 9, 4) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            stack.Children.Add(name);
            var price = new TextBlock { Text = $"AFN {item.Price:N0}  ·  + Add",
                FontSize = 12, FontWeight = FontWeights.Bold,
                Margin = new Thickness(9, 0, 9, 9) };
            price.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
            stack.Children.Add(price);
            var tileSurface = new Border { Width = 156, MinHeight = 177, CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1), ClipToBounds = true, Child = stack };
            tileSurface.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            tileSurface.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            var tile = new Button { Content = tileSurface, Tag = item.Id,
                Width = 158, MinHeight = 179, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 9, 9),
                ToolTip = $"Select {item.Name} · AFN {item.Price:N2}" };
            tile.Click += (_, _) => onSelect(item);
            cards.Children.Add(tile);
            tileControls.Add((item, tile));
        }
        search.TextChanged += (_, _) => Filter();
        root.Children.Add(cards);

        if (items.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "No available menu items. Sync the catalogue from Web.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
        }
        return root;
    }

    public static bool IsAllowedTenantMediaUrl(string? candidate, string? tenantUrl)
    {
        return Uri.TryCreate(candidate, UriKind.Absolute, out var image) &&
            Uri.TryCreate(tenantUrl, UriKind.Absolute, out var tenant) &&
            image.Scheme == Uri.UriSchemeHttps &&
            tenant.Scheme == Uri.UriSchemeHttps &&
            image.Port == tenant.Port &&
            string.Equals(image.IdnHost, tenant.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            image.AbsolutePath.StartsWith("/media/menu-items/", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(image.UserInfo);
    }

    private static async Task<Uri?> CacheMenuPhotoAsync(string url, string? tenantUrl)
    {
        var activation = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
        var standalone = BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activation);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS", "Restaurant", "menu-photos");

        // A standalone file URI may only reference pictures copied into our
        // own per-user photo directory, never an arbitrary path or network share.
        if (standalone && Uri.TryCreate(url, UriKind.Absolute, out var local) && local.IsFile)
        {
            var normalized = Path.GetFullPath(local.LocalPath);
            var parent = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            return normalized.StartsWith(parent, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(normalized) ? new Uri(normalized) : null;
        }

        if (!IsAllowedTenantMediaUrl(url, tenantUrl)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var file = Path.Combine(root, hash + ".img");
        if (File.Exists(file) && new FileInfo(file).Length > 0 &&
            File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddMinutes(-15))
            return new Uri(file);

        // Standalone mode must not fetch menu photos from a remote URL.
        // Previously downloaded pictures remain available from the private cache.
        if (standalone)
            return File.Exists(file) ? new Uri(file) : null;

        await DownloadSlots.WaitAsync();
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > 0 &&
            File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddMinutes(-15))
                return new Uri(file);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp") ||
                response.Content.Headers.ContentLength > MaximumImageBytes)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var bytes = new byte[32 * 1024];
            while (true)
            {
                var n = await stream.ReadAsync(bytes);
                if (n == 0) break;
                if (memory.Length + n > MaximumImageBytes) return null;
                memory.Write(bytes, 0, n);
            }
            Directory.CreateDirectory(root);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, memory.ToArray()); File.Move(temp, file, overwrite: true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new Uri(file);
        }
        catch (HttpRequestException) { return File.Exists(file) ? new Uri(file) : null; }
        catch (TaskCanceledException) { return File.Exists(file) ? new Uri(file) : null; }
        finally { DownloadSlots.Release(); }
    }
}

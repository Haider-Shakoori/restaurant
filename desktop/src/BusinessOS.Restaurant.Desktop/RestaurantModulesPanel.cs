using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// The tenant owns module configuration. A successful web/API refresh is cached in
/// SQLite; offline terminals display the last known values and cannot modify
/// the tenant's master switches while disconnected.
/// </summary>
internal static class RestaurantModulesPanel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<FrameworkElement> CreateAsync()
    {
        var cache = new LocalRestaurantSettingsService(new LocalDatabaseFactory());
        var initial = await cache.GetModulesAsync();
        var session = await new WindowsSessionStore().LoadAsync();
        var canEdit = session?.User.Role.Trim().ToLowerInvariant() is "owner" or "admin";
        var activation = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
        var standalone = BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activation);

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = standalone ? "Standalone Offline · Local restaurant modules" : "Modules shared with the restaurant web app",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        root.Children.Add(new TextBlock
        {
            Text = standalone
                ? "Restaurant modules are stored on this computer and remain independent of the web. POS, KOT and payments stay active."
                : "POS, orders, KOT, payments and table closing remain active. Changes are made online and synchronized to this terminal; the last successful configuration stays visible offline.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var recipes = Check("Recipes and portion costing", initial.RecipesEnabled);
        var inventory = Check("Inventory and stock tracking", initial.InventoryEnabled);
        var purchasing = Check("Purchasing and suppliers (requires inventory)", initial.PurchasingEnabled);
        var automatic = Check("Automatic ingredient consumption (requires recipes + inventory)", initial.AutomaticRecipeConsumptionEnabled);
        foreach (var check in new[] { recipes, inventory, purchasing, automatic })
        {
            check.IsEnabled = canEdit;
            root.Children.Add(check);
        }

        void ApplyDependencies()
        {
            if (inventory.IsChecked != true)
            {
                purchasing.IsChecked = false;
                automatic.IsChecked = false;
            }
            if (recipes.IsChecked != true)
                automatic.IsChecked = false;
            purchasing.IsEnabled = canEdit && inventory.IsChecked == true;
            automatic.IsEnabled = canEdit && recipes.IsChecked == true && inventory.IsChecked == true;
        }

        recipes.Click += (_, _) => ApplyDependencies();
        inventory.Click += (_, _) => ApplyDependencies();
        ApplyDependencies();

        var status = new TextBlock
        {
            Text = canEdit
                ? "Showing the locally cached module configuration. Refresh from Web to check for updates."
                : "Module switches can be edited by an Owner or Admin. Showing last-known settings.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 14),
        };
        root.Children.Add(status);

        var controls = new WrapPanel();
        var refresh = new Button { Content = "Sync modules from Web", Height = 38, MinWidth = 195, Margin = new Thickness(0, 0, 10, 8) };
        var save = new Button { Content = standalone ? "Save local modules" : "Save modules to Web", Height = 38, MinWidth = 180, IsEnabled = canEdit, Margin = new Thickness(0, 0, 10, 8) };
        refresh.Visibility = standalone ? Visibility.Collapsed : Visibility.Visible;
        controls.Children.Add(refresh);
        controls.Children.Add(save);
        root.Children.Add(controls);

        RestaurantModuleFlags CurrentSelection() => new(
            RecipesEnabled: recipes.IsChecked == true,
            InventoryEnabled: inventory.IsChecked == true,
            PurchasingEnabled: purchasing.IsChecked == true,
            AutomaticRecipeConsumptionEnabled: automatic.IsChecked == true);

        void Display(RestaurantModuleFlags flags)
        {
            recipes.IsChecked = flags.RecipesEnabled;
            inventory.IsChecked = flags.InventoryEnabled;
            purchasing.IsChecked = flags.PurchasingEnabled;
            automatic.IsChecked = flags.AutomaticRecipeConsumptionEnabled;
            ApplyDependencies();
        }

        async Task<RestaurantModuleFlags> RequestAsync(HttpMethod method, RestaurantModuleFlags? update = null)
        {
            if (standalone)
                throw new InvalidOperationException("No web requests are allowed in standalone mode.");
            var signedIn = await new WindowsSessionStore().LoadAsync()
                ?? throw new InvalidOperationException("Sign in to synchronize modules.");

            if (!Uri.TryCreate(signedIn.TenantBaseUrl, UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("A secure tenant URL is required.");

            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(12),
            };
            var url = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/api/v1/desktop/modules");
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (method == HttpMethod.Post && update is not null)
            {
                request.Content = JsonContent.Create(new
                {
                    recipes_enabled = update.RecipesEnabled,
                    inventory_enabled = update.InventoryEnabled,
                    purchasing_enabled = update.PurchasingEnabled,
                    automatic_recipe_consumption_enabled = update.AutomaticRecipeConsumptionEnabled,
                });
            }

            using var response = await http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "Restaurant server rejected module settings (HTTP " + (int)response.StatusCode + "). " +
                    (json.Length > 150 ? json[..150] : json));

            using var document = JsonDocument.Parse(json);
            var data = document.RootElement.GetProperty("data");
            return new RestaurantModuleFlags(
                data.GetProperty("recipes_enabled").GetBoolean(),
                data.GetProperty("inventory_enabled").GetBoolean(),
                data.GetProperty("purchasing_enabled").GetBoolean(),
                data.GetProperty("automatic_recipe_consumption_enabled").GetBoolean());
        }

        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try
            {
                var flags = await RequestAsync(HttpMethod.Get);
                await cache.ApplyCloudModulesAsync(flags);
                Display(flags);
                status.Text = "Modules synchronized from the Web. Refresh the current desktop workspace to apply changes.";
            }
            catch (Exception ex) { status.Text = "Offline cache retained: " + ex.Message; }
            finally { refresh.IsEnabled = true; }
        };

        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var requested = CurrentSelection();
                if (requested.PurchasingEnabled && !requested.InventoryEnabled ||
                    requested.AutomaticRecipeConsumptionEnabled &&
                    (!requested.RecipesEnabled || !requested.InventoryEnabled))
                    throw new InvalidOperationException("Enable required dependent modules first.");

                RestaurantModuleFlags confirmed;
                if (standalone)
                {
                    await cache.ApplyStandaloneModulesAsync(requested);
                    confirmed = requested;
                }
                else
                {
                    confirmed = await RequestAsync(HttpMethod.Post, requested);
                    await cache.ApplyCloudModulesAsync(confirmed);
                }
                Display(confirmed);
                status.Text = "Modules saved to Web and cached locally. Refresh the workspace to apply them.";
            }
            catch (Exception ex) { status.Text = "Modules not changed: " + ex.Message; }
            finally { save.IsEnabled = canEdit; }
        };

        root.Children.Add(new TextBlock
        {
            Text = "No module switch deletes recipes, supplier orders, stock movements or payment history.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static CheckBox Check(string name, bool value) => new()
    {
        Content = name,
        IsChecked = value,
        Margin = new Thickness(0, 7, 0, 7),
        FontSize = 14,
    };
}

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BusinessOS.Restaurant.Authentication;
using Microsoft.Win32;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Tenant-authoritative, online catalog editing. The local SQLite catalog is a
/// synchronized cache and must never be changed independently for these edits.
/// </summary>
internal sealed class DesktopCloudManagementWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthSession _session;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(35),
    };
    private readonly Uri _root;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly List<CatalogEditor> _editors = [];

    public DesktopCloudManagementWindow(AuthSession session, string initialTab)
    {
        if (session.User.Role.Trim().ToLowerInvariant() is not ("owner" or "admin" or "manager"))
            throw new UnauthorizedAccessException("Catalog management requires the owner or manager role.");
        if (!Uri.TryCreate(session.TenantBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A secure HTTPS tenant endpoint is required for catalog management.");

        _session = session;
        _root = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/api/v1/desktop/management");
        Title = "Restaurant · Catalog, Floors & Inventory";
        Width = 1000;
        Height = 760;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "Restaurant management", FontSize = 23, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "These edits are saved to the tenant server, not a disposable local catalog snapshot. " +
                   "Refresh the Desktop workspace after server synchronization to see updated menu/floor/stock reference data.",
            TextWrapping = TextWrapping.Wrap,
        });
        var reload = new Button { Content = "Refresh from tenant", Height = 34, Width = 174, Margin = new Thickness(0, 9, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reload.Click += async (_, _) => await RefreshAsync();
        heading.Children.Add(reload);
        heading.Children.Add(_status);
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var tabs = new TabControl();
        Add(tabs, new CatalogEditor("Categories", "categories", [
            new("name", "Category name"),
            new("is_active", "Category active", Kind.Flag),
        ]));
        Add(tabs, new CatalogEditor("Menu & Images", "menu_items", [
            new("name", "Menu item name"), new("sku", "SKU (optional)"),
            new("description", "Description (optional)"), new("price", "Price (AFN)", Kind.Number),
            new("menu_category_id", "Menu category", Kind.Category),
            new("is_available", "Available to order", Kind.Flag),
        ], allowImage: true));
        Add(tabs, new CatalogEditor("Dining Floors", "areas", [
            new("name", "Floor / area name"), new("branch_id", "Branch", Kind.Branch),
            new("is_active", "Floor active", Kind.Flag),
        ]));
        Add(tabs, new CatalogEditor("Dining Tables", "tables", [
            new("name", "Table name"), new("code", "Table code"),
            new("dining_area_id", "Dining floor", Kind.Area),
            new("capacity", "Seats", Kind.Number), new("is_active", "Table active", Kind.Flag),
        ]));
        Add(tabs, new CatalogEditor("Inventory Items", "inventory_items", [
            new("name", "Ingredient name"), new("sku", "SKU"),
            new("base_unit", "Stock unit (kg, g, l, ml, pcs)"),
            new("purchase_unit", "Purchase unit"),
            new("purchase_to_base_factor", "Purchase-to-stock multiplier", Kind.Number),
            new("reorder_level", "Reorder threshold", Kind.Number),
            new("is_active", "Ingredient active", Kind.Flag),
        ]));

        var tabIndex = _editors.FindIndex(editor => editor.Key == initialTab);
        tabs.SelectedIndex = tabIndex < 0 ? 0 : tabIndex;
        root.Children.Add(tabs);
        Content = root;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _http.Dispose();
    }

    private void Add(TabControl tabs, CatalogEditor editor)
    {
        editor.SaveRequested += async (sender, _) => await SaveAsync((CatalogEditor)sender!);
        _editors.Add(editor);
        tabs.Items.Add(new TabItem { Header = editor.Title, Content = editor.Content });
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var response = await _http.SendAsync(Request(HttpMethod.Get, _root));
            var data = await ReadAsync(response);
            foreach (var editor in _editors) editor.Reload(data);
            _status.Text = "Management data refreshed from the restaurant tenant.";
        }
        catch (Exception ex)
        {
            _status.Text = "Cloud management unavailable: " + ex.Message;
        }
    }

    private async Task SaveAsync(CatalogEditor editor)
    {
        editor.SetSaving(true);
        try
        {
            var selected = editor.Selected;
            var path = editor.Route + (selected is null ? "" : "/" + selected.Id);
            using var req = Request(selected is null ? HttpMethod.Post : HttpMethod.Patch, UriFor(path));
            req.Content = JsonContent.Create(editor.Payload(), options: JsonOptions);
            using var response = await _http.SendAsync(req);
            var saved = await ReadAsync(response);
            var id = saved.TryGetProperty("data", out var item) &&
                     item.TryGetProperty("id", out var idField) ? idField.ToString() : selected?.Id;

            if (editor.AllowImage && !string.IsNullOrWhiteSpace(editor.ImagePath))
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("The server did not return an item ID for image upload.");
                using var imageRequest = Request(HttpMethod.Post, UriFor("menu-items/" + id + "/image"));
                using var form = new MultipartFormDataContent();
                using var imageStream = File.OpenRead(editor.ImagePath);
                if (imageStream.Length > 5 * 1024 * 1024)
                    throw new InvalidOperationException("Menu image exceeds the server 5 MB limit.");
                var file = new StreamContent(imageStream);
                file.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(editor.ImagePath).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg",
                });
                form.Add(file, "image", Path.GetFileName(editor.ImagePath));
                imageRequest.Content = form;
                using var imageResponse = await _http.SendAsync(imageRequest);
                await ReadAsync(imageResponse);
            }

            editor.Clear();
            await RefreshAsync();
            _status.Text = "Changes saved to tenant. Desktop operational cache updates on next successful cloud refresh.";
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, editor.Title + " saved to tenant.");
        }
        catch (Exception ex)
        {
            _status.Text = "Changes not fully saved: " + ex.Message;
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, _status.Text);
        }
        finally { editor.SetSaving(false); }
    }

    private Uri UriFor(string relative) => new(_root.AbsoluteUri.TrimEnd('/') + "/" + relative);

    private HttpRequestMessage Request(HttpMethod method, Uri endpoint)
    {
        var req = new HttpRequestMessage(method, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        if (!response.IsSuccessStatusCode)
        {
            var message = $"Tenant returned HTTP {(int)response.StatusCode}.";
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                    {
                        message = field.Value[0].ToString();
                        break;
                    }
                }
            }
            else if (document.RootElement.TryGetProperty("message", out var error))
                message = error.ToString();
            throw new InvalidOperationException(message);
        }
        return document.RootElement.Clone();
    }

    private enum Kind { Text, Number, Flag, Branch, Area, Category }
    private sealed record Field(string Key, string Label, Kind Type = Kind.Text);
    private sealed record Choice(string Id, string Label);
    private sealed record ManagementRow(string Id, string Label, JsonElement Data);

    private sealed class CatalogEditor
    {
        public event EventHandler? SaveRequested;
        public string Title { get; }
        public string Key { get; }
        public string Route => Key == "menu_items" ? "menu-items" : Key == "inventory_items" ? "inventory" : Key;
        public bool AllowImage { get; }
        public string? ImagePath { get; private set; }
        public FrameworkElement Content { get; }
        public ManagementRow? Selected { get; private set; }

        private readonly ComboBox _rows = new() { Height = 35 };
        private readonly Dictionary<string, FrameworkElement> _inputs = [];
        private readonly Field[] _fields;
        private readonly Button _save = new() { Height = 38, Content = "Create", MinWidth = 145 };
        private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 9) };

        public CatalogEditor(string title, string key, Field[] fields, bool allowImage = false)
        {
            Title = title;
            Key = key;
            AllowImage = allowImage;
            _fields = fields;

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sidebar = new StackPanel { Margin = new Thickness(0, 5, 15, 8) };
            sidebar.Children.Add(new TextBlock { Text = "Select an existing record", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            _rows.DisplayMemberPath = nameof(ManagementRow.Label);
            _rows.SelectionChanged += (_, _) =>
            {
                if (_rows.SelectedItem is ManagementRow row) Select(row);
            };
            sidebar.Children.Add(_rows);
            var create = new Button { Content = "+ New " + title.TrimEnd('s'), Height = 36, Margin = new Thickness(0, 10, 0, 0) };
            create.Click += (_, _) => Clear();
            sidebar.Children.Add(create);
            sidebar.Children.Add(new TextBlock { Text = "Production KOT and prior receipts stay immutable. In-use tables cannot be moved or disabled.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            layout.Children.Add(sidebar);

            var form = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            foreach (var field in _fields)
            {
                form.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
                FrameworkElement input = field.Type switch
                {
                    Kind.Flag => new CheckBox { IsChecked = true, Margin = new Thickness(0, 5, 0, 3) },
                    Kind.Branch or Kind.Area or Kind.Category => new ComboBox { Height = 34, DisplayMemberPath = nameof(Choice.Label) },
                    _ => new TextBox { Height = 34 },
                };
                _inputs[field.Key] = input;
                form.Children.Add(input);
            }

            if (AllowImage)
            {
                var choose = new Button { Content = "Choose / replace menu image", Height = 34, Margin = new Thickness(0, 12, 0, 4) };
                choose.Click += (_, _) =>
                {
                    var picker = new OpenFileDialog { Filter = "Menu images|*.jpg;*.jpeg;*.png;*.webp" };
                    if (picker.ShowDialog() == true)
                    {
                        ImagePath = picker.FileName;
                        _hint.Text = "Image: " + Path.GetFileName(ImagePath);
                    }
                };
                form.Children.Add(choose);
            }
            _save.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
            form.Children.Add(_save);
            form.Children.Add(_hint);
            var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(scroller, 1);
            layout.Children.Add(scroller);
            Content = layout;
        }

        public void Reload(JsonElement root)
        {
            var data = root.GetProperty("data");
            var records = data.GetProperty(Key).EnumerateArray().Select(x => new ManagementRow(
                x.GetProperty("id").ToString(),
                x.TryGetProperty("name", out var name) ? name.ToString() :
                    x.TryGetProperty("code", out var code) ? code.ToString() : x.GetProperty("id").ToString(),
                x.Clone())).ToArray();
            _rows.ItemsSource = records;
            FillChoices(data, "branches", "branch_id");
            FillChoices(data, "areas", "dining_area_id");
            FillChoices(data, "categories", "menu_category_id", true);
        }

        private void FillChoices(JsonElement data, string collection, string field, bool allowBlank = false)
        {
            if (!_inputs.TryGetValue(field, out var input) || input is not ComboBox box) return;
            var choices = data.GetProperty(collection).EnumerateArray()
                .Where(x => !x.TryGetProperty("is_active", out var active) || active.ValueKind != JsonValueKind.False)
                .Select(x => new Choice(x.GetProperty("id").ToString(), x.GetProperty("name").ToString())).ToList();
            if (allowBlank) choices.Insert(0, new Choice("", "Uncategorized"));
            box.ItemsSource = choices;
            box.SelectedIndex = choices.Count > 0 ? 0 : -1;
        }

        private void Select(ManagementRow row)
        {
            Selected = row;
            foreach (var field in _fields)
            {
                if (!row.Data.TryGetProperty(field.Key, out var value)) continue;
                var input = _inputs[field.Key];
                if (input is CheckBox flag) flag.IsChecked = value.ValueKind == JsonValueKind.True;
                else if (input is TextBox text) text.Text = value.ValueKind == JsonValueKind.Null ? "" : value.ToString();
                else if (input is ComboBox combo && combo.ItemsSource is IEnumerable<Choice> choices)
                    combo.SelectedItem = choices.FirstOrDefault(x => x.Id == value.ToString());
            }
            _save.Content = "Save changes";
            ImagePath = null;
            _hint.Text = "Editing " + row.Label;
        }

        public void Clear()
        {
            Selected = null;
            _rows.SelectedItem = null;
            foreach (var field in _fields)
            {
                var input = _inputs[field.Key];
                if (input is CheckBox flag) flag.IsChecked = true;
                else if (input is TextBox text) text.Text = field.Key is "capacity" or "purchase_to_base_factor" ? "1" : field.Key == "reorder_level" ? "0" : "";
                else if (input is ComboBox combo) combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
            }
            ImagePath = null;
            _save.Content = "Create";
            _hint.Text = "";
        }

        public Dictionary<string, object?> Payload()
        {
            var payload = new Dictionary<string, object?>();
            foreach (var field in _fields)
            {
                var input = _inputs[field.Key];
                if (input is CheckBox flag) payload[field.Key] = flag.IsChecked == true;
                else if (input is ComboBox combo) payload[field.Key] = (combo.SelectedItem as Choice)?.Id is { Length: > 0 } id ? id : null;
                else if (input is TextBox text)
                {
                    var value = text.Text.Trim();
                    if (field.Type == Kind.Number)
                    {
                        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
                            throw new InvalidOperationException(field.Label + " must be a number.");
                        payload[field.Key] = field.Key == "capacity" ? (object)(int)quantity : quantity;
                    }
                    else payload[field.Key] = value.Length == 0 ? null : value;
                }
            }
            return payload;
        }

        public void SetSaving(bool state) => _save.IsEnabled = !state;
    }
}

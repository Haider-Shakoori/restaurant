using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>Local-only menu and dining setup: never uses HTTP or cloud outbox.</summary>
internal sealed class StandaloneCatalogWindow : Window
{
    private readonly LocalDatabaseFactory _factory = new();
    private readonly string _actorRole;
    private readonly StandaloneCatalogService _catalog = new(new LocalDatabaseFactory());
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _content = new() { Margin = new Thickness(15) };

    public StandaloneCatalogWindow(string role)
    {
        if (role.Trim().ToLowerInvariant() is not ("owner" or "admin" or "manager"))
            throw new UnauthorizedAccessException("Only restaurant managers may edit local catalog data.");

        _actorRole = role;
        Title = "Standalone Restaurant · Local Setup";
        Width = 860; Height = 680; MinWidth = 620; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;
        Loaded += async (_, _) => await RebuildAsync();
    }

    private static TextBox Field(string hint, string? text = null) => new()
    {
        Text = text ?? "", ToolTip = hint, MinWidth = 160, Height = 34,
        Margin = new Thickness(0, 0, 10, 8),
    };

    private static Button Action(string label) => new()
    {
        Content = label, MinHeight = 34, Margin = new Thickness(0, 0, 10, 8),
        Padding = new Thickness(12, 4, 12, 4),
    };

    private static StackPanel Block(string title, string description)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        block.Children.Add(new TextBlock { Text = title, FontSize = 18,
            FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) });
        block.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12) });
        return block;
    }

    private async Task RebuildAsync()
    {
        await _factory.EnsureCreatedAsync();
        await using var db = _factory.Create();
        var branches = await db.Branches.AsNoTracking().OrderBy(b => b.Name).ToArrayAsync();
        var areas = await db.DiningAreas.AsNoTracking().OrderBy(a => a.Name).ToArrayAsync();
        var categories = await db.MenuCategories.AsNoTracking().OrderBy(c => c.Name).ToArrayAsync();
        var existingDishes = await db.MenuItems.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync();
        var tables = await db.DiningTables.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync();
        var stations = await db.KitchenStations.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync();

        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = "Standalone Offline · local SQLite only",
            FontSize = 23, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _content.Children.Add(new TextBlock
        {
            Text = "Create restaurant branches, areas, tables, menu categories and dishes on this PC. " +
                   "These changes are never sent to the web. Back up local data in Settings → Backup & Restore.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var branchBlock = Block("1 · Branch", "Add a local branch before defining areas and stock.");
        var branchName = Field("Branch name"); branchName.Width = 270;
        var branchCode = Field("Branch code"); branchCode.Width = 160;
        var branchSave = Action("Add local branch");
        var branchRow = new WrapPanel();
        branchRow.Children.Add(branchName); branchRow.Children.Add(branchCode); branchRow.Children.Add(branchSave);
        branchBlock.Children.Add(branchRow);
        branchSave.Click += async (_, _) => await SaveAsync(async database =>
        {
            var name = branchName.Text.Trim(); var code = branchCode.Text.Trim().ToUpperInvariant();
            Require(name.Length is >= 2 and <= 120 && code.Length is >= 1 and <= 20, "Enter a branch name and short code.");
            Require(!await database.Branches.AnyAsync(b => b.Code == code), "Branch code already exists.");
            database.Branches.Add(new LocalBranch
            {
                Id = Guid.CreateVersion7().ToString("N"), Name = name, Code = code, IsActive = true,
            });
        });

        var areaBlock = Block("2 · Dining area", "Assign a hall, floor or seating area to a branch.");
        var areaName = Field("Dining area name");
        var branchChoice = new ComboBox { Width = 245, Height = 34, Margin = new Thickness(0, 0, 10, 8),
            DisplayMemberPath = "Name", ItemsSource = branches, SelectedIndex = branches.Length == 0 ? -1 : 0 };
        var areaSave = Action("Add local area");
        var areaRow = new WrapPanel();
        areaRow.Children.Add(branchChoice); areaRow.Children.Add(areaName); areaRow.Children.Add(areaSave);
        areaBlock.Children.Add(areaRow);
        areaSave.Click += async (_, _) => await SaveAsync(async database =>
        {
            var branch = branchChoice.SelectedItem as LocalBranch;
            Require(branch is not null, "Create a branch first.");
            var name = areaName.Text.Trim();
            Require(name.Length is >= 2 and <= 120, "Enter a dining area name.");
            database.DiningAreas.Add(new LocalDiningArea
            {
                Id = Guid.CreateVersion7().ToString("N"), BranchId = branch!.Id,
                Name = name, SortOrder = 0, IsActive = true,
            });
            await Task.CompletedTask;
        });

        var tableBlock = Block("3 · Dining table", "Tables begin Available and change with real orders and payments.");
        var areaChoice = new ComboBox { Width = 200, Height = 34, Margin = new Thickness(0, 0, 10, 8),
            DisplayMemberPath = "Name", ItemsSource = areas, SelectedIndex = areas.Length == 0 ? -1 : 0 };
        var tableName = Field("Table name"); var tableCode = Field("Table code");
        var capacity = Field("Seats", "4"); capacity.Width = 70;
        var tableSave = Action("Add local table");
        var tableRow = new WrapPanel();
        foreach (var c in new FrameworkElement[] { areaChoice, tableName, tableCode, capacity, tableSave })
            tableRow.Children.Add(c);
        tableBlock.Children.Add(tableRow);
        tableSave.Click += async (_, _) => await SaveAsync(async database =>
        {
            var area = areaChoice.SelectedItem as LocalDiningArea;
            Require(area is not null, "Create a dining area first.");
            var name = tableName.Text.Trim(); var code = tableCode.Text.Trim().ToUpperInvariant();
            Require(name.Length is >= 1 and <= 120 && code.Length is >= 1 and <= 30,
                "Enter table name and code.");
            Require(int.TryParse(capacity.Text, out var seats) && seats is >= 1 and <= 50,
                "Seats must be between 1 and 50.");
            Require(!await database.DiningTables.AnyAsync(t => t.DiningAreaId == area!.Id && t.Code == code),
                "This table code already exists in that area.");
            database.DiningTables.Add(new LocalDiningTable
            {
                Id = Guid.CreateVersion7().ToString("N"), DiningAreaId = area!.Id,
                Name = name, Code = code, Capacity = seats, Status = "available", IsActive = true,
            });
        });

        var categoryBlock = Block("4 · Menu category", "Categories group dishes in the photo POS.");
        var categoryName = Field("Category name"); var categorySave = Action("Add category");
        var categoryRow = new WrapPanel(); categoryRow.Children.Add(categoryName);
        categoryRow.Children.Add(categorySave); categoryBlock.Children.Add(categoryRow);
        categorySave.Click += async (_, _) => await SaveAsync(async database =>
        {
            var name = categoryName.Text.Trim();
            Require(name.Length is >= 2 and <= 120, "Enter a menu category.");
            Require(!await database.MenuCategories.AnyAsync(c => c.Name == name), "Category already exists.");
            database.MenuCategories.Add(new LocalMenuCategory
            {
                Id = Guid.CreateVersion7().ToString("N"), Name = name, SortOrder = 0, IsActive = true,
            });
        });

        var menuBlock = Block("5 · Menu dish", "Set the AFN price and optional photo. The photo is copied locally; it is never uploaded.");
        var dishName = Field("Dish name"); var sku = Field("SKU (optional)");
        var price = Field("Price in AFN"); price.Width = 120;
        var categoryChoice = new ComboBox { Width = 190, Height = 34, Margin = new Thickness(0, 0, 10, 8),
            DisplayMemberPath = "Name", ItemsSource = categories, SelectedIndex = categories.Length == 0 ? -1 : 0 };
        var photo = Field("Optional local photo"); photo.Width = 260; photo.IsReadOnly = true;
        var browse = Action("Select photo"); var menuSave = Action("Add dish");
        browse.Click += (_, _) =>
        {
            var chooser = new OpenFileDialog
            {
                Title = "Select local menu photo", Filter = "Menu photos|*.jpg;*.jpeg;*.png",
                CheckFileExists = true,
            };
            if (chooser.ShowDialog(this) == true) photo.Text = chooser.FileName;
        };
        var menuRow = new WrapPanel();
        foreach (var c in new FrameworkElement[]
            { categoryChoice, dishName, sku, price, photo, browse, menuSave })
            menuRow.Children.Add(c);
        menuBlock.Children.Add(menuRow);
        menuSave.Click += async (_, _) => await SaveAsync(async database =>
        {
            var category = categoryChoice.SelectedItem as LocalMenuCategory;
            Require(category is not null, "Create a category first.");
            var name = dishName.Text.Trim();
            Require(name.Length is >= 2 and <= 180, "Enter a dish name.");
            Require(decimal.TryParse(price.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
                && amount >= 0 && amount <= 1_000_000, "Enter a valid nonnegative AFN price.");
            var code = sku.Text.Trim();
            Require(code.Length <= 80, "SKU cannot exceed 80 characters.");
            var image = await CopyPhotoAsync(photo.Text.Trim());
            database.MenuItems.Add(new LocalMenuItem
            {
                Id = Guid.CreateVersion7().ToString("N"), MenuCategoryId = category!.Id,
                Name = name, Sku = code.Length == 0 ? null : code,
                Price = amount, Currency = "AFN", ImageUrl = image,
                SortOrder = 0, IsAvailable = true,
            });
        });

        var editDishBlock = Block("6 · Edit or hide existing dish",
            "A dish's current name, AFN price, SKU or sale availability can change without altering the historical amounts on paid orders.");
        var dishChoice = new ComboBox { Width = 235, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = existingDishes, Margin = new Thickness(0, 0, 10, 8) };
        var editDishName = Field("Dish name"); var editPrice = Field("AFN price"); editPrice.Width = 120;
        var editSku = Field("SKU"); editSku.Width = 120;
        var dishAvailable = new CheckBox { Content = "Available for sale", IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 8) };
        dishChoice.SelectionChanged += (_, _) =>
        {
            if (dishChoice.SelectedItem is not LocalMenuItem dish) return;
            editDishName.Text = dish.Name;
            editPrice.Text = dish.Price.ToString(CultureInfo.CurrentCulture);
            editSku.Text = dish.Sku ?? "";
            dishAvailable.IsChecked = dish.IsAvailable;
        };
        if (existingDishes.Length > 0) dishChoice.SelectedIndex = 0;
        var editDishSave = Action("Save dish changes");
        editDishSave.Click += async (_, _) => await UpdateLocalAsync(async () =>
        {
            if (dishChoice.SelectedItem is not LocalMenuItem dish)
                throw new InvalidOperationException("Select a dish first.");
            if (!decimal.TryParse(editPrice.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                throw new ArgumentException("Enter a valid AFN price.");
            await _catalog.UpdateDishAsync(dish.Id, editDishName.Text, value,
                dishAvailable.IsChecked == true, editSku.Text, _actorRole);
        });
        var dishEditRow = new WrapPanel();
        foreach (var control in new FrameworkElement[]
            { dishChoice, editDishName, editPrice, editSku, dishAvailable, editDishSave })
            dishEditRow.Children.Add(control);
        editDishBlock.Children.Add(dishEditRow);

        var editTableBlock = Block("7 · Edit or disable table",
            "Occupied, reserved and unsettled tables cannot be deactivated, even if their order is billed.");
        var tableChoice = new ComboBox { Width = 200, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = tables, Margin = new Thickness(0, 0, 10, 8) };
        var editTableName = Field("Table name"); var editSeats = Field("Seats"); editSeats.Width = 70;
        var tableActive = new CheckBox { Content = "Active", IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8) };
        tableChoice.SelectionChanged += (_, _) =>
        {
            if (tableChoice.SelectedItem is not LocalDiningTable table) return;
            editTableName.Text = table.Name;
            editSeats.Text = table.Capacity.ToString(CultureInfo.InvariantCulture);
            tableActive.IsChecked = table.IsActive;
        };
        if (tables.Length > 0) tableChoice.SelectedIndex = 0;
        var editTableSave = Action("Save table changes");
        editTableSave.Click += async (_, _) => await UpdateLocalAsync(async () =>
        {
            if (tableChoice.SelectedItem is not LocalDiningTable table)
                throw new InvalidOperationException("Select a table first.");
            if (!int.TryParse(editSeats.Text, out var seats))
                throw new ArgumentException("Enter a valid seat count.");
            await _catalog.UpdateTableAsync(table.Id, editTableName.Text, seats,
                tableActive.IsChecked == true, _actorRole);
        });
        var tableEditRow = new WrapPanel();
        foreach (var control in new FrameworkElement[]
            { tableChoice, editTableName, editSeats, tableActive, editTableSave })
            tableEditRow.Children.Add(control);
        editTableBlock.Children.Add(tableEditRow);

        var categoryEditBlock = Block("8 · Edit menu category",
            "Disable categories without deleting existing dishes or order history.");
        var categoryEditChoice = new ComboBox { Width = 195, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = categories, Margin = new Thickness(0, 0, 10, 8) };
        var editCategoryName = Field("Category name");
        var categoryActive = new CheckBox { Content = "Active", IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8) };
        categoryEditChoice.SelectionChanged += (_, _) =>
        {
            if (categoryEditChoice.SelectedItem is not LocalMenuCategory selected) return;
            editCategoryName.Text = selected.Name;
            categoryActive.IsChecked = selected.IsActive;
        };
        if (categories.Length > 0) categoryEditChoice.SelectedIndex = 0;
        var categoryEditSave = Action("Save category");
        categoryEditSave.Click += async (_, _) => await UpdateLocalAsync(async () =>
        {
            if (categoryEditChoice.SelectedItem is not LocalMenuCategory selected)
                throw new InvalidOperationException("Select a category first.");
            await _catalog.UpdateCategoryAsync(selected.Id, editCategoryName.Text,
                categoryActive.IsChecked == true, _actorRole);
        });
        var categoryEditRow = new WrapPanel();
        foreach (var control in new FrameworkElement[]
            { categoryEditChoice, editCategoryName, categoryActive, categoryEditSave })
            categoryEditRow.Children.Add(control);
        categoryEditBlock.Children.Add(categoryEditRow);

        var stationBlock = Block("9 · Kitchen station & KOT routing",
            "Create a local kitchen station, then assign individual dishes to the appropriate kitchen in the same branch.");
        var routeBranch = new ComboBox { Width = 175, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = branches, Margin = new Thickness(0, 0, 10, 8) };
        if (branches.Length > 0) routeBranch.SelectedIndex = 0;
        var stationCode = Field("Station code"); stationCode.Width = 125;
        var stationName = Field("Station name");
        var saveStation = Action("Add station");
        saveStation.Click += async (_, _) => await UpdateLocalAsync(async () =>
        {
            if (routeBranch.SelectedItem is not LocalBranch branch)
                throw new InvalidOperationException("Create a branch first.");
            await _catalog.UpsertKitchenStationAsync(null, branch.Id, stationCode.Text,
                stationName.Text, _actorRole);
        });
        var stationRow = new WrapPanel();
        foreach (var control in new FrameworkElement[]
            { routeBranch, stationCode, stationName, saveStation })
            stationRow.Children.Add(control);
        stationBlock.Children.Add(stationRow);

        var routeDish = new ComboBox { Width = 210, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = existingDishes, Margin = new Thickness(0, 0, 10, 8) };
        var routeBranchChoice = new ComboBox { Width = 175, Height = 34,
            DisplayMemberPath = "Name", ItemsSource = branches, Margin = new Thickness(0, 0, 10, 8) };
        var routeStationChoice = new ComboBox { Width = 210, Height = 34,
            DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 10, 8) };
        routeBranchChoice.SelectionChanged += (_, _) =>
        {
            var b = routeBranchChoice.SelectedItem as LocalBranch;
            var options = stations.Where(x => x.BranchId == b?.Id && x.IsActive).ToArray();
            routeStationChoice.ItemsSource = options;
            routeStationChoice.SelectedIndex = options.Length > 0 ? 0 : -1;
        };
        if (existingDishes.Length > 0) routeDish.SelectedIndex = 0;
        if (branches.Length > 0) routeBranchChoice.SelectedIndex = 0;
        var routeSave = Action("Route dish to kitchen");
        routeSave.Click += async (_, _) => await UpdateLocalAsync(async () =>
        {
            if (routeDish.SelectedItem is not LocalMenuItem dish ||
                routeBranchChoice.SelectedItem is not LocalBranch branch ||
                routeStationChoice.SelectedItem is not LocalKitchenStation station)
                throw new InvalidOperationException("Select a dish, branch and kitchen station.");
            await _catalog.RouteDishToStationAsync(dish.Id, branch.Id, station.Id, _actorRole);
        });
        var routeRow = new WrapPanel();
        foreach (var control in new FrameworkElement[]
            { routeDish, routeBranchChoice, routeStationChoice, routeSave })
            routeRow.Children.Add(control);
        stationBlock.Children.Add(routeRow);

        foreach (var part in new[] { branchBlock, areaBlock, tableBlock, categoryBlock, menuBlock,
            editDishBlock, editTableBlock, categoryEditBlock, stationBlock })
            _content.Children.Add(part);
        _content.Children.Add(_status);
    }

    private async Task UpdateLocalAsync(Func<Task> action)
    {
        try
        {
            await action();
            await RebuildAsync();
            _status.Text = "Saved locally. Existing orders, KOT and accounting history are unchanged.";
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private async Task SaveAsync(Func<RestaurantDbContext, Task> action)
    {
        try
        {
            await _factory.EnsureCreatedAsync();
            await using var db = _factory.Create();
            await action(db);
            await db.SaveChangesAsync();
            await RebuildAsync();
            _status.Text = "Saved to standalone SQLite. No web synchronization performed.";
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static async Task<string?> CopyPhotoAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        Require(extension is ".png" or ".jpg" or ".jpeg", "Only PNG/JPEG images are accepted.");
        var file = new FileInfo(path);
        Require(file.Exists && file.Length is > 0 and <= 3_000_000, "Photo must exist and be 3 MB or smaller.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS", "Restaurant", "menu-photos");
        Directory.CreateDirectory(root);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var dest = Path.Combine(root, hash + extension);
        File.Copy(path, dest, overwrite: true);
        return new Uri(dest).AbsoluteUri;
    }
}

using System.Text.Json;

namespace BusinessOS.Restaurant.Desktop.Appearance;

public sealed record AppearanceSettings(string Theme = "Glass");

public sealed class AppearanceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public AppearanceSettingsStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _path = Path.Combine(root, "config", "appearance.json");
    }

    public AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppearanceSettings();
            }

            return JsonSerializer.Deserialize<AppearanceSettings>(
                File.ReadAllText(_path),
                JsonOptions) ?? new AppearanceSettings();
        }
        catch
        {
            return new AppearanceSettings();
        }
    }

    public void Save(AppearanceTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(new AppearanceSettings(theme.ToString()), JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}

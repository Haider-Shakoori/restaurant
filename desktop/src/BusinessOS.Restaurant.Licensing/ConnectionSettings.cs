using System.Text.Json;

namespace BusinessOS.Restaurant.Licensing;

public sealed record ConnectionSettings(
    string TenantBaseUrl,
    bool SyncEnabled = true,
    bool LocalServerEnabled = true,
    int LocalServerPort = 8787);

public sealed class ConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public ConnectionSettingsStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _settingsPath = Path.Combine(root, "config", "connection.json");
    }

    public async Task<ConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<ConnectionSettings>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    public async Task SaveAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private static void Validate(ConnectionSettings settings)
    {
        if (!Uri.TryCreate(settings.TenantBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("TenantBaseUrl must be an absolute HTTP or HTTPS URL.", nameof(settings));
        }

        if (settings.LocalServerPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "LocalServerPort must be between 1024 and 65535.");
        }
    }
}

namespace BusinessOS.Restaurant.Licensing;

public sealed class InstallationIdentityProvider
{
    private readonly string _identityPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InstallationIdentityProvider(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _identityPath = Path.Combine(root, "licensing", "installation.id");
    }

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_identityPath)!);

            if (File.Exists(_identityPath))
            {
                var existing = (await File.ReadAllTextAsync(_identityPath, cancellationToken)).Trim();

                if (Guid.TryParse(existing, out var parsed))
                {
                    return parsed.ToString("D");
                }
            }

            var id = Guid.NewGuid().ToString("D");
            var temporary = _identityPath + ".tmp";
            await File.WriteAllTextAsync(temporary, id, cancellationToken);
            File.Move(temporary, _identityPath, overwrite: true);
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }
}

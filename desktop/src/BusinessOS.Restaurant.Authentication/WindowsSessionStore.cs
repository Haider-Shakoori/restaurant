using System.Security.Cryptography;
using System.Text.Json;

namespace BusinessOS.Restaurant.Authentication;

public sealed class WindowsSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _sessionPath;

    public WindowsSessionStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _sessionPath = Path.Combine(root, "auth", "session.dat");
    }

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected restaurant session storage requires Windows DPAPI.");
        }

        if (!File.Exists(_sessionPath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_sessionPath, cancellationToken);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        try
        {
            return JsonSerializer.Deserialize<AuthSession>(plainBytes, JsonOptions)
                ?? throw new CryptographicException("The protected user session is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected restaurant session storage requires Windows DPAPI.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);

        try
        {
            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            var temporary = _sessionPath + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _sessionPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Task ClearAsync()
    {
        if (File.Exists(_sessionPath))
        {
            File.Delete(_sessionPath);
        }

        return Task.CompletedTask;
    }
}

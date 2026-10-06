using System.Security.Cryptography;
using System.Text.Json;

namespace BusinessOS.Restaurant.Licensing;

public sealed class WindowsActivationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _statePath;

    public WindowsActivationStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        var licensing = Path.Combine(root, "licensing");
        _statePath = Path.Combine(licensing, "activation.dat");
    }

    public async Task<ActivationState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected restaurant activation storage requires Windows DPAPI.");
        }

        if (!File.Exists(_statePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_statePath, cancellationToken);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);

        try
        {
            return JsonSerializer.Deserialize<ActivationState>(plainBytes, JsonOptions)
                ?? throw new CryptographicException("The protected activation state is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public async Task SaveAsync(ActivationState state, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected restaurant activation storage requires Windows DPAPI.");
        }

        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);

        try
        {
            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                DataProtectionScope.LocalMachine);

            var temporary = _statePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _statePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Task ClearAsync()
    {
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }

        return Task.CompletedTask;
    }
}

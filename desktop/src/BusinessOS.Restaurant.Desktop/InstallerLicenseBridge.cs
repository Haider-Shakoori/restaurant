using System.Globalization;
using System.Text;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop;

internal static class InstallerLicenseBridge
{
    private const string StatusArgument = "--installer-license-status=";
    private const string ActivateFileArgument = "--installer-activate-file=";
    private const string ResultArgument = "--installer-result=";

    public static async Task<bool> TryHandleAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var statusPath = GetArgument(args, StatusArgument);
        if (!string.IsNullOrWhiteSpace(statusPath))
        {
            var coordinator = new RestaurantLicenseCoordinator();
            var status = await coordinator.GetStatusAsync(cancellationToken);
            await WriteStatusAsync(statusPath, status, null, cancellationToken);
            return true;
        }

        var licenseFile = GetArgument(args, ActivateFileArgument);
        if (string.IsNullOrWhiteSpace(licenseFile))
        {
            return false;
        }

        var resultPath = GetArgument(args, ResultArgument);
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            throw new ArgumentException("Installer activation requires --installer-result.");
        }

        string licenseKey = string.Empty;

        try
        {
            licenseKey = (await File.ReadAllTextAsync(licenseFile, cancellationToken)).Trim();

            var coordinator = new RestaurantLicenseCoordinator();
            await coordinator.ActivateAsync(
                licenseKey,
                RestaurantLicenseCoordinator.DefaultCentralBaseUri,
                cancellationToken);

            var status = await coordinator.GetStatusAsync(cancellationToken);
            await WriteStatusAsync(resultPath, status, null, cancellationToken);
        }
        catch (Exception exception)
        {
            await WriteStatusAsync(
                resultPath,
                RestaurantLicenseStatus.Missing,
                InstallerMessage(exception),
                cancellationToken);
        }
        finally
        {
            if (!string.IsNullOrEmpty(licenseKey))
            {
                licenseKey = new string('\0', licenseKey.Length);
            }

            try
            {
                if (File.Exists(licenseFile))
                {
                    File.Delete(licenseFile);
                }
            }
            catch
            {
            }
        }

        return true;
    }

    private static async Task WriteStatusAsync(
        string path,
        RestaurantLicenseStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"activated={(status.HasActivation ? 1 : 0)}");
        builder.AppendLine($"valid={(status.IsValid ? 1 : 0)}");
        builder.AppendLine($"plan={Safe(status.PlanName)}");
        builder.AppendLine($"days_remaining={status.DaysRemaining.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"subscription_ends_at={Safe(status.SubscriptionEndsAt?.ToString("O"))}");
        builder.AppendLine($"offline_valid_until={Safe(status.OfflineValidUntil?.ToString("O"))}");
        builder.AppendLine($"tenant_base_url={Safe(status.TenantBaseUrl)}");
        builder.AppendLine($"error={Safe(error)}");

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string? GetArgument(IReadOnlyList<string> args, string prefix)
    {
        var match = args.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? null
            : match[prefix.Length..].Trim().Trim('"');
    }

    private static string InstallerMessage(Exception exception) =>
        exception switch
        {
            LicenseApiException api => api.Message,
            System.Security.Cryptography.CryptographicException =>
                "The server license signature could not be verified.",
            _ => "Activation could not be completed. Check the license key and internet connection.",
        };

    private static string Safe(string? value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("=", "-", StringComparison.Ordinal);
}

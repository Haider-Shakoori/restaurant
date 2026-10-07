using System.Globalization;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop.Activation;

internal static class InstallerCommandHandler
{
    public static async Task<int?> TryHandleAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var statusFile = Value(args, "--installer-license-status=");
        if (!string.IsNullOrWhiteSpace(statusFile))
        {
            var service = new DesktopActivationService();
            var state = await service.LoadUsableAsync(
                refreshWhenCloseToExpiry: false,
                cancellationToken);
            await WriteResultAsync(statusFile, DesktopActivationService.Describe(state), null, cancellationToken);
            return 0;
        }

        var licenseFile = Value(args, "--installer-activate-file=");
        if (string.IsNullOrWhiteSpace(licenseFile))
        {
            return null;
        }

        var resultFile = Value(args, "--installer-result=");
        if (string.IsNullOrWhiteSpace(resultFile))
        {
            return 21;
        }

        try
        {
            var licenseKey = (await File.ReadAllTextAsync(licenseFile, cancellationToken)).Trim();
            var service = new DesktopActivationService();
            var state = await service.ActivateAsync(licenseKey, cancellationToken);
            await WriteResultAsync(
                resultFile,
                DesktopActivationService.Describe(state),
                null,
                cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            await WriteResultAsync(
                resultFile,
                null,
                InstallerError(exception),
                cancellationToken);
            return 20;
        }
        finally
        {
            try
            {
                if (File.Exists(licenseFile))
                {
                    File.Delete(licenseFile);
                }
            }
            catch
            {
                // A temporary installer key file is best-effort deleted.
            }
        }
    }

    private static string? Value(IReadOnlyList<string> args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..]
            .Trim()
            .Trim('"');

    private static async Task WriteResultAsync(
        string path,
        DesktopActivationStatus? status,
        string? error,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string>
        {
            $"activated={(status?.IsActivated == true ? "1" : "0")}",
            $"plan={Safe(status?.PlanName)}",
            $"plan_code={Safe(status?.PlanCode)}",
            $"days_remaining={(status?.DaysRemaining ?? 0).ToString(CultureInfo.InvariantCulture)}",
            $"subscription_ends_at={Safe(status?.SubscriptionEndsAt?.ToString("O", CultureInfo.InvariantCulture))}",
            $"tenant_base_url={Safe(status?.TenantBaseUrl)}",
            $"message={Safe(status?.Message)}",
            $"error={Safe(error)}",
        };

        await File.WriteAllLinesAsync(path, lines, cancellationToken);
    }

    private static string InstallerError(Exception exception) =>
        exception is LicenseApiException
            ? exception.Message
            : exception is System.Security.Cryptography.CryptographicException
                ? "The licensing server response could not be verified."
                : "Activation could not be completed. Check the key and internet connection.";

    private static string Safe(string? value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("=", "-", StringComparison.Ordinal)
            .Trim();
}

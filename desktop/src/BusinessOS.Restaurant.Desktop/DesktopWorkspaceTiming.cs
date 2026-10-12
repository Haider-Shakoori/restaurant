using System.IO;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Small, non-blocking navigation timing log for diagnosing slow POS/KOT pages
/// on a real Windows machine. Never records orders, customer names, images,
/// credentials, tenant URLs or payment details.
/// </summary>
internal static class DesktopWorkspaceTiming
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const int MaxLogBytes = 256 * 1024;
    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BusinessOS", "Restaurant", "logs", "navigation-performance.log");

    public static void Record(string operation, string route, long elapsedMilliseconds)
    {
        var safeRoute = new string(route.Where(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(36).ToArray());
        var safeAction = operation is "navigation" or "refresh" ? operation : "workspace";
        var duration = Math.Clamp(elapsedMilliseconds, 0, 600_000);
        var line = $"{DateTimeOffset.UtcNow:O} {safeAction} {safeRoute} {duration}ms{Environment.NewLine}";
        _ = Task.Run(async () =>
        {
            try
            {
                await Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var file = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    if (File.Exists(file) && new FileInfo(file).Length > MaxLogBytes)
                        File.Delete(file); // bounded log size; no unlimited growth
                    await File.AppendAllTextAsync(file, line).ConfigureAwait(false);
                }
                finally { Gate.Release(); }
            }
            catch
            {
                // Diagnostics must never prevent a cashier or waiter workflow.
            }
        });
    }
}

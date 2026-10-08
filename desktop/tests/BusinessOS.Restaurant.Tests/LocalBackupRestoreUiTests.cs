using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LocalBackupRestoreUiTests
{
    [Fact]
    public void Maintenance_ui_preserves_offline_operations_until_explicit_relaunch()
    {
        var root = RepositoryRoot();
        var pages = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        Assert.Contains("await BackupRestorePanelAsync()", pages);
        Assert.Contains("await maintenance.CreateBackupAsync()", pages);
        Assert.Contains("await maintenance.StageRestoreAsync(picker.FileName)", pages);
        Assert.Contains("MessageBoxButton.YesNo", pages);
        Assert.Contains("Restart Restaurant Desktop", pages);
        Assert.Contains("diagnostics.PendingRestore", pages);
    }

    [Fact]
    public void Restore_marker_is_verified_and_prior_wal_sidecars_are_not_discarded()
    {
        var root = RepositoryRoot();
        var maintenance = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Persistence", "LocalMaintenanceService.cs"));
        Assert.Contains("Sha256Async(staged, token)", maintenance);
        Assert.Contains("The staged restaurant backup checksum does not match", maintenance);
        Assert.Contains("source.BackupDatabase(target)", maintenance);
        Assert.Contains("File.Move(current + suffix, safety + suffix", maintenance);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

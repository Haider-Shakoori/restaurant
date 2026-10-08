using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class Batch14MaintenanceTests
{
    [Fact]
    public async Task Backup_is_integrity_checked_and_restores_on_next_start()
    {
        var root=Path.Combine(Path.GetTempPath(),$"bos-b14-{Guid.NewGuid():N}");
        var factory=new LocalDatabaseFactory(root); var maintenance=new LocalMaintenanceService(factory,root);
        await factory.EnsureCreatedAsync();
        await using(var db=factory.Create())
        {
            db.PairedTerminals.Add(new LocalPairedTerminal { DeviceId="device-1",TenantId="tenant-1",DeviceSecretHash="a",AccessTokenHash="b",UserId=1,UserPublicId="u1",UserName="Waiter",UserRole="waiter",ValidatedAtUtc=DateTimeOffset.UtcNow,LastSeenAtUtc=DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var backup=await maintenance.CreateBackupAsync();
        Assert.True(File.Exists(backup.Path)); Assert.True(backup.Bytes>0); Assert.Equal(64,backup.Sha256.Length);
        await using(var db=factory.Create()){ db.PairedTerminals.RemoveRange(db.PairedTerminals); await db.SaveChangesAsync(); }
        await maintenance.StageRestoreAsync(backup.Path);
        Assert.True((await maintenance.GetDiagnosticsAsync()).PendingRestore);
        Assert.True(await maintenance.ApplyPendingRestoreAsync());
        await using(var db=factory.Create()) Assert.Equal(1,await db.PairedTerminals.CountAsync());
        Directory.Delete(root,true);
    }

    [Fact]
    public async Task Diagnostics_bundle_excludes_database_and_secrets()
    {
        var root=Path.Combine(Path.GetTempPath(),$"bos-b14-{Guid.NewGuid():N}");
        var factory=new LocalDatabaseFactory(root); var maintenance=new LocalMaintenanceService(factory,root);
        await factory.EnsureCreatedAsync();
        var bundle=await maintenance.CreateDiagnosticsBundleAsync();
        Assert.True(File.Exists(bundle));
        using (var zip=System.IO.Compression.ZipFile.OpenRead(bundle))
        {
            Assert.Single(zip.Entries); Assert.Equal("diagnostics.json",zip.Entries[0].FullName);
        }
        Directory.Delete(root,true);
    }

    [Fact]
    public async Task Tampered_pending_restore_never_overwrites_existing_restaurant_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bos-restore-hash-{Guid.NewGuid():N}");
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var maintenance = new LocalMaintenanceService(factory, root);
            await factory.EnsureCreatedAsync();
            await using (var db = factory.Create())
            {
                db.PairedTerminals.Add(new LocalPairedTerminal
                {
                    DeviceId = "device-untouched", TenantId = "tenant-1",
                    DeviceSecretHash = "a", AccessTokenHash = "b",
                    UserId = 1, UserPublicId = "u1", UserName = "Waiter",
                    UserRole = "waiter", ValidatedAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var original = await maintenance.CreateBackupAsync();
            await maintenance.StageRestoreAsync(original.Path);
            var staged = Path.Combine(root, "restore", "pending.db");
            // The modified staged DB must fail manifest checksum validation before
            // any live DB file is touched, even if the altered bytes were SQLite-valid.
            await using (var file = new FileStream(staged, FileMode.Append, FileAccess.Write))
                await file.WriteAsync(new byte[] { 0x20 });

            await Assert.ThrowsAsync<InvalidDataException>(
                () => maintenance.ApplyPendingRestoreAsync());

            await using var verify = factory.Create();
            Assert.Equal(1, await verify.PairedTerminals.CountAsync());
            Assert.True(File.Exists(Path.Combine(root, "restore", "pending.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_database_cannot_be_staged_for_restore()
    {
        var root=Path.Combine(Path.GetTempPath(),$"bos-b14-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var bad=Path.Combine(root,"bad.db"); await File.WriteAllTextAsync(bad,"not sqlite");
        var service=new LocalMaintenanceService(new LocalDatabaseFactory(root),root);
        await Assert.ThrowsAnyAsync<Exception>(()=>service.StageRestoreAsync(bad));
        Directory.Delete(root,true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Incomplete_restore_staging_fails_closed_and_preserves_live_database(bool removeMarker)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bos-incomplete-restore-{Guid.NewGuid():N}");
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var maintenance = new LocalMaintenanceService(factory, root);
            await factory.EnsureCreatedAsync();
            var backup = await maintenance.CreateBackupAsync();
            await maintenance.StageRestoreAsync(backup.Path);
            var liveBefore = await File.ReadAllBytesAsync(factory.DatabasePath);

            var incompletePath = Path.Combine(root, "restore", removeMarker ? "pending.json" : "pending.db");
            File.Delete(incompletePath);

            await Assert.ThrowsAsync<InvalidDataException>(() => maintenance.ApplyPendingRestoreAsync());
            Assert.Equal(liveBefore, await File.ReadAllBytesAsync(factory.DatabasePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_restore_preparation_leaves_live_sqlite_sidecars_untouched()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bos-restore-copy-failure-{Guid.NewGuid():N}");
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var maintenance = new LocalMaintenanceService(factory, root);
            await factory.EnsureCreatedAsync();
            var backup = await maintenance.CreateBackupAsync();
            await maintenance.StageRestoreAsync(backup.Path);

            // Block the destination copy without touching the live DB. The
            // sidecar is a sentinel: do not open SQLite while it is present.
            var sidecar = factory.DatabasePath + "-wal";
            var sentinel = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            await File.WriteAllBytesAsync(sidecar, sentinel);
            Directory.CreateDirectory(factory.DatabasePath + ".restore");

            await Assert.ThrowsAnyAsync<Exception>(() => maintenance.ApplyPendingRestoreAsync());
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(sidecar));
            Assert.True(File.Exists(Path.Combine(root, "restore", "pending.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

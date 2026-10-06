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
        using var zip=System.IO.Compression.ZipFile.OpenRead(bundle);
        Assert.Single(zip.Entries); Assert.Equal("diagnostics.json",zip.Entries[0].FullName);
        Directory.Delete(root,true);
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
}

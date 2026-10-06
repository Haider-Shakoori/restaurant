using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BusinessOS.Restaurant.Persistence;

public sealed record LocalBackupResult(string Path, long Bytes, string Sha256, DateTimeOffset CreatedAtUtc);
public sealed record LocalMaintenanceDiagnostics(string DatabasePath, long DatabaseBytes, string Integrity, int BackupCount, bool PendingRestore, long FreeDiskBytes);

public sealed class LocalMaintenanceService
{
    private readonly LocalDatabaseFactory _factory;
    private readonly string _root;
    public LocalMaintenanceService(LocalDatabaseFactory factory, string? rootDirectory = null)
    {
        _factory=factory;
        _root=rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BusinessOS","Restaurant");
    }

    public async Task<LocalBackupResult> CreateBackupAsync(string? destinationDirectory=null, CancellationToken token=default)
    {
        await _factory.EnsureCreatedAsync(token);
        var dir=destinationDirectory ?? Path.Combine(_root,"backups"); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,$"restaurant-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        await using var source=_factory.CreateConnection();
        await source.OpenAsync(token);
        await using var target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false }.ToString());
        await target.OpenAsync(token);
        source.BackupDatabase(target);
        await target.CloseAsync(); await source.CloseAsync();
        var hash=await Sha256Async(path,token);
        var info=new FileInfo(path);
        return new(path,info.Length,hash,DateTimeOffset.UtcNow);
    }

    public async Task StageRestoreAsync(string backupPath, CancellationToken token=default)
    {
        if(!File.Exists(backupPath)) throw new FileNotFoundException("Backup file was not found.",backupPath);
        await ValidateDatabaseAsync(backupPath,token);
        var restoreDir=Path.Combine(_root,"restore"); Directory.CreateDirectory(restoreDir);
        var staged=Path.Combine(restoreDir,"pending.db");
        File.Copy(backupPath,staged,true);
        await File.WriteAllTextAsync(Path.Combine(restoreDir,"pending.json"),JsonSerializer.Serialize(new { source=Path.GetFileName(backupPath), sha256=await Sha256Async(staged,token), stagedAtUtc=DateTimeOffset.UtcNow }),token);
    }

    public async Task<bool> ApplyPendingRestoreAsync(CancellationToken token=default)
    {
        var restoreDir=Path.Combine(_root,"restore"); var staged=Path.Combine(restoreDir,"pending.db"); var marker=Path.Combine(restoreDir,"pending.json");
        if(!File.Exists(staged) || !File.Exists(marker)) return false;
        await ValidateDatabaseAsync(staged,token);
        Directory.CreateDirectory(Path.GetDirectoryName(_factory.DatabasePath)!);
        var safety=Path.Combine(_root,"backups",$"pre-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
        if(File.Exists(_factory.DatabasePath)){ Directory.CreateDirectory(Path.GetDirectoryName(safety)!); File.Copy(_factory.DatabasePath,safety,true); }
        var temp=_factory.DatabasePath+".restore"; File.Copy(staged,temp,true); File.Move(temp,_factory.DatabasePath,true);
        File.Delete(staged); File.Delete(marker);
        return true;
    }

    public async Task<LocalMaintenanceDiagnostics> GetDiagnosticsAsync(CancellationToken token=default)
    {
        await _factory.EnsureCreatedAsync(token);
        var integrity=await IntegrityAsync(_factory.DatabasePath,token);
        var info=new FileInfo(_factory.DatabasePath);
        var backups=Path.Combine(_root,"backups");
        return new(_factory.DatabasePath,info.Exists?info.Length:0,integrity,Directory.Exists(backups)?Directory.GetFiles(backups,"*.db").Length:0,
            File.Exists(Path.Combine(_root,"restore","pending.json")),new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace);
    }

    public async Task<string> CreateDiagnosticsBundleAsync(string? destinationDirectory=null, CancellationToken token=default)
    {
        var d=await GetDiagnosticsAsync(token); var dir=destinationDirectory ?? Path.Combine(_root,"diagnostics"); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,$"diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        await using var fs=File.Create(path); using var zip=new ZipArchive(fs,ZipArchiveMode.Create);
        var entry=zip.CreateEntry("diagnostics.json"); await using var stream=entry.Open();
        await JsonSerializer.SerializeAsync(stream,new { generatedAtUtc=DateTimeOffset.UtcNow, databaseBytes=d.DatabaseBytes, d.Integrity, d.BackupCount, d.PendingRestore, d.FreeDiskBytes, os=Environment.OSVersion.ToString(), runtime=Environment.Version.ToString() },cancellationToken:token);
        return path;
    }

    public static async Task ValidateDatabaseAsync(string path,CancellationToken token=default)
    {
        var result=await IntegrityAsync(path,token);
        if(!string.Equals(result,"ok",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"SQLite integrity check failed: {result}");
    }
    private static async Task<string> IntegrityAsync(string path,CancellationToken token)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Mode=SqliteOpenMode.ReadOnly, Pooling=false }.ToString());
        await c.OpenAsync(token); await using var cmd=c.CreateCommand(); cmd.CommandText="PRAGMA integrity_check;";
        return Convert.ToString(await cmd.ExecuteScalarAsync(token)) ?? "unknown";
    }
    private static async Task<string> Sha256Async(string path,CancellationToken token)
    {
        await using var s=File.OpenRead(path); var hash=await SHA256.HashDataAsync(s,token); return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

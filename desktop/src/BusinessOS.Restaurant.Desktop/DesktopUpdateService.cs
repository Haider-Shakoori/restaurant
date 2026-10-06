using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.Desktop;

public sealed record DesktopUpdateManifest([property:JsonPropertyName("version")] string Version,[property:JsonPropertyName("url")] string Url,[property:JsonPropertyName("sha256")] string Sha256,[property:JsonPropertyName("notes")] string? Notes);
public sealed record DesktopUpdateStatus(bool Available,string CurrentVersion,string? LatestVersion,string? Notes,string? PackagePath);

public sealed class DesktopUpdateService(HttpClient httpClient, string? rootDirectory=null)
{
    private readonly string _root=rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BusinessOS","Restaurant");
    public async Task<DesktopUpdateStatus> CheckAsync(Uri manifestUri,CancellationToken token=default)
    {
        if(manifestUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Updater manifest must use HTTPS.");
        var manifest=await httpClient.GetFromJsonAsync<DesktopUpdateManifest>(manifestUri,token) ?? throw new InvalidDataException("Update manifest is empty.");
        if(!Version.TryParse(manifest.Version,out var latest)) throw new InvalidDataException("Update version is invalid.");
        var current=typeof(DesktopUpdateService).Assembly.GetName().Version ?? new Version(0,0);
        return new(latest>current,current.ToString(),latest.ToString(),manifest.Notes,null);
    }
    public async Task<string> DownloadAsync(DesktopUpdateManifest manifest,CancellationToken token=default)
    {
        if(!Uri.TryCreate(manifest.Url,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps) throw new InvalidDataException("Update package must use HTTPS.");
        if(string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length!=64) throw new InvalidDataException("Update SHA-256 is invalid.");
        var dir=Path.Combine(_root,"updates"); Directory.CreateDirectory(dir); var temp=Path.Combine(dir,"package.tmp");
        await using(var input=await httpClient.GetStreamAsync(uri,token)) await using(var output=File.Create(temp)) await input.CopyToAsync(output,token);
        await using var verify=File.OpenRead(temp); var actual=Convert.ToHexString(await SHA256.HashDataAsync(verify,token)).ToLowerInvariant();
        if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual),Convert.FromHexString(manifest.Sha256))) { File.Delete(temp); throw new InvalidDataException("Downloaded update checksum does not match the manifest."); }
        var final=Path.Combine(dir,$"BusinessOS-Restaurant-{manifest.Version}.exe"); File.Move(temp,final,true); return final;
    }
}

using System.Xml.Linq;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class Batch15ReleasePackagingTests
{
    [Fact]
    public void Desktop_project_contains_release_branding_and_single_file_contract()
    {
        var root=RepositoryRoot();
        var project=XDocument.Load(Path.Combine(root,"desktop","src","BusinessOS.Restaurant.Desktop","BusinessOS.Restaurant.Desktop.csproj"));
        string Value(string name) => project.Descendants().First(x=>x.Name.LocalName==name).Value;
        Assert.Equal("BusinessOS Restaurant",Value("Product"));
        Assert.Equal("BusinessOS.af",Value("Company"));
        Assert.Equal("win-x64",Value("RuntimeIdentifier"));
        Assert.Equal("true",Value("PublishSingleFile"));
        Assert.Equal("true",Value("SelfContained"));
    }

    [Fact]
    public void Firewall_script_never_enables_public_profile_by_default()
    {
        var script=File.ReadAllText(Path.Combine(RepositoryRoot(),"desktop","installer","Configure-LanFirewall.ps1"));
        Assert.Contains("-Profile Domain,Private",script,StringComparison.Ordinal);
        Assert.DoesNotContain("-Profile Public",script,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ValidateRange(1,65535)]",script,StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_requires_or_reuses_machine_activation_and_carries_restaurant_branding()
    {
        var root=RepositoryRoot();
        var installer=File.ReadAllText(Path.Combine(root,"desktop","installer","BusinessOS.Restaurant.iss"));
        var generator=File.ReadAllText(Path.Combine(root,"tools","generate_restaurant_icons.py"));
        var workflow=File.ReadAllText(Path.Combine(root,".github","workflows","restaurant-desktop-ci.yml"));
        var background=Path.Combine(root,"desktop","src","BusinessOS.Restaurant.Desktop","Assets","RestaurantGlassBackground.jpg");

        Assert.Contains("https://restaurant.businessos.af",installer,StringComparison.Ordinal);
        Assert.Contains("Start 7-Day Trial",installer,StringComparison.Ordinal);
        Assert.Contains("--installer-license-status",installer,StringComparison.Ordinal);
        Assert.Contains("--installer-activate-file",installer,StringComparison.Ordinal);
        Assert.Contains("days_remaining",installer,StringComparison.Ordinal);
        Assert.Contains("Already activated on this computer",installer,StringComparison.Ordinal);
        Assert.Contains("WizardBackImageFile=..\\src\\BusinessOS.Restaurant.Desktop\\Assets\\RestaurantInstallerBackground.png",installer,StringComparison.Ordinal);
        Assert.Contains("WizardBackImageFileDynamicDark=..\\src\\BusinessOS.Restaurant.Desktop\\Assets\\RestaurantInstallerBackground.png",installer,StringComparison.Ordinal);
        Assert.Contains("WizardStyle=modern dark", installer, StringComparison.Ordinal);
        Assert.Contains("WizardBackColor=#101C28", installer, StringComparison.Ordinal);
        Assert.Contains("WizardBackImageOpacity=225", installer, StringComparison.Ordinal);
        Assert.Contains("LicenseHelpLabel.Font.Color :=", installer, StringComparison.Ordinal);
        Assert.Contains("ImageEnhance.Brightness(artwork).enhance(0.65)", generator, StringComparison.Ordinal);
        Assert.Contains("Image.blend(artwork, dark_veil, alpha=0.42)", generator, StringComparison.Ordinal);
        Assert.Contains("Validate installer background contrast", workflow, StringComparison.Ordinal);
        Assert.Contains("check_restaurant_installer_contrast.py", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("WizardBackImageFile=..\\src\\BusinessOS.Restaurant.Desktop\\Assets\\RestaurantGlassBackground.jpg",installer,StringComparison.Ordinal);
        Assert.Contains("RestaurantInstallerBackground.png",generator,StringComparison.Ordinal);
        Assert.Contains("format=\"PNG\"",generator,StringComparison.Ordinal);
        Assert.Contains("Smoke-test branded installer startup",workflow,StringComparison.Ordinal);
        Assert.Contains("Bitmap image is not valid",workflow,StringComparison.Ordinal);
        Assert.Contains("Installer background must be PNG",workflow,StringComparison.Ordinal);

        Assert.True(File.Exists(background));
        Assert.True(new FileInfo(background).Length > 5_000);
    }

    [Fact]
    public void Ci_distinguishes_unsigned_artifacts_and_publishes_verifiable_release_hashes()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(),
            ".github", "workflows", "restaurant-desktop-ci.yml"));
        Assert.Contains("Generate release provenance and SHA-256 hashes", workflow);
        Assert.Contains("Get-FileHash $exe -Algorithm SHA256", workflow);
        Assert.Contains("Get-FileHash $setup.FullName -Algorithm SHA256", workflow);
        Assert.Contains("source_commit=$env:GITHUB_SHA", workflow);
        Assert.Contains("build_kind=unsigned_ci", workflow);
        Assert.Contains("BusinessOS-Restaurant-release-checksums-ci", workflow);
        Assert.Contains("if-no-files-found: error", workflow);
    }

    [Fact]
    public void Ci_independently_verifies_unsigned_release_manifest_and_both_hashes()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root,
            ".github", "workflows", "restaurant-desktop-ci.yml"));
        var releaseScript = File.ReadAllText(Path.Combine(root,
            "desktop", "installer", "Build-Release.ps1"));

        Assert.Contains("Verify release provenance against built artifacts", workflow);
        Assert.Contains("Get-AuthenticodeSignature -FilePath $exe", workflow);
        Assert.Contains("if ($signature.Status -ne \"NotSigned\")", workflow);
        Assert.Contains("$fields[\"source_commit\"] -ne $env:GITHUB_SHA", workflow);
        Assert.Contains("$fields[\"build_kind\"] -ne \"unsigned_ci\"", workflow);
        Assert.Contains("$fields[\"desktop_exe_sha256\"]", workflow);
        Assert.Contains("$fields[\"installer_sha256\"]", workflow);
        Assert.Contains("Get-FileHash $setup -Algorithm SHA256", workflow);
        Assert.Contains("Test-Path -LiteralPath $CertificatePath -PathType Leaf", releaseScript);
        Assert.Contains("IsNullOrWhiteSpace($CertificatePassword)", releaseScript);
        Assert.Contains("SIGNED_RELEASE=false", releaseScript);
        Assert.Contains("signtool verify /pa", releaseScript);
    }

    [Fact]
    public void Installer_has_no_destructive_uninstall_directives()
    {
        var installer=File.ReadAllText(Path.Combine(RepositoryRoot(),"desktop","installer","BusinessOS.Restaurant.iss"));
        Assert.DoesNotContain("[UninstallDelete]",installer,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleteafterinstall",installer,StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"desktop"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

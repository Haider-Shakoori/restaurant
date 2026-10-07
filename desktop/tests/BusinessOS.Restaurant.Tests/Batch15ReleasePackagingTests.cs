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
        var background=Path.Combine(root,"desktop","src","BusinessOS.Restaurant.Desktop","Assets","RestaurantGlassBackground.jpg");
        var appBackground=background;

        Assert.Contains("https://restaurant.businessos.af",installer,StringComparison.Ordinal);
        Assert.Contains("Start 7-Day Trial",installer,StringComparison.Ordinal);
        Assert.Contains("--installer-license-status",installer,StringComparison.Ordinal);
        Assert.Contains("--installer-activate-file",installer,StringComparison.Ordinal);
        Assert.Contains("days_remaining",installer,StringComparison.Ordinal);
        Assert.Contains("Already activated on this computer",installer,StringComparison.Ordinal);
        Assert.Contains("WizardBackImageFile=",installer,StringComparison.Ordinal);
        Assert.Contains("RestaurantGlassBackground.jpg",installer,StringComparison.Ordinal);

        Assert.True(File.Exists(background));
        Assert.True(new FileInfo(background).Length > 5_000);
        Assert.True(File.Exists(appBackground));
        Assert.True(new FileInfo(appBackground).Length > 5_000);
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

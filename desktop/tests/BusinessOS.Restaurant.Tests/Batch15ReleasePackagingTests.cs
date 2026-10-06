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
        var values=project.Descendants().ToDictionary(x=>x.Name.LocalName,x=>x.Value);
        Assert.Equal("BusinessOS Restaurant",values["Product"]);
        Assert.Equal("BusinessOS.af",values["Company"]);
        Assert.Equal("win-x64",values["RuntimeIdentifier"]);
        Assert.Equal("true",values["PublishSingleFile"]);
        Assert.Equal("true",values["SelfContained"]);
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
    public void Installer_does_not_delete_local_application_data()
    {
        var installer=File.ReadAllText(Path.Combine(RepositoryRoot(),"desktop","installer","BusinessOS.Restaurant.iss"));
        Assert.DoesNotContain("LocalAppData",installer,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[UninstallDelete]",installer,StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"desktop"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

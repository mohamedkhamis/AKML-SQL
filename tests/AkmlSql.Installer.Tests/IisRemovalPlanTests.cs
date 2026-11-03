using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class IisRemovalPlanTests
{
    [Theory]
    [InlineData(true, false, "WebAdministration")]
    [InlineData(false, true, "AppCmd")]
    [InlineData(false, false, "None")]
    public void Removal_uses_available_tools_and_always_removes_owned_binding_and_markers(bool module, bool appcmd, string method)
    {
        using var json = JsonDocument.Parse(HostingTestTools.DotSource("web-iis-remove.ps1",
            $"Get-IisRemovalPlan -WebAdministrationAvailable ${module} -AppCmdAvailable ${appcmd} -SiteListedAfter $true | ConvertTo-Json -Compress"));
        var plan = json.RootElement;
        Assert.Equal(method, plan.GetProperty("SiteMethod").GetString());
        Assert.Equal(method, plan.GetProperty("PoolMethod").GetString());
        Assert.True(plan.GetProperty("RemoveOwnedBinding").GetBoolean());
        Assert.False(plan.GetProperty("DeleteWebConfig").GetBoolean());
        Assert.True(plan.GetProperty("WriteFailure").GetBoolean());
        Assert.Contains("appcmd.exe delete site \"AkmlSqlWeb\"", plan.GetProperty("FailureReason").GetString());
        Assert.Equal(3, plan.GetProperty("Markers").GetArrayLength());
    }

    [Fact]
    public void Other_applications_keep_the_pool_and_only_confirmed_site_removal_allows_web_config_deletion()
    {
        var result = HostingTestTools.DotSource("web-iis-remove.ps1",
            "$p=Get-IisRemovalPlan -WebAdministrationAvailable $true -OtherPoolUsers @('OtherApp') -SiteListedAfter $false; \"$($p.PoolMethod)|$($p.DeleteWebConfig)|$($p.WriteFailure)\"");
        Assert.Equal("None|True|False", result);
        Assert.Equal("None", HostingTestTools.DotSource("web-iis-remove.ps1",
            "(Get-IisRemovalPlan -WebAdministrationAvailable $true -PoolUsageKnown $false).PoolMethod"));
    }
}

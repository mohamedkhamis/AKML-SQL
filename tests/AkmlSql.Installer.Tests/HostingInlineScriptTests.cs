using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class HostingInlineScriptTests
{
    [Fact]
    public void The_real_Pascal_generated_ACL_commands_parse_in_PowerShell_51()
    {
        // Compile the actual thin Code into an unrelated, lowest-privilege probe which
        // aborts InitializeSetup. Only the string builder runs; no ACL or registry change.
        foreach (var code in WebHostingProbeTests.ProbeThinCode(
            "Web_ExactAclCode('C:\\AKML Public', True)", "Web_ExactAclCode('C:\\AKML Private', False)"))
        {
            var result = HostingTestTools.PowerShell("[void][scriptblock]::Create('" + code.Replace("'", "''") + "'); 'valid'");
            Assert.Equal("valid", result);
            Assert.Contains("SetAccessRuleProtection($true,$false)", code);
            Assert.Contains("Reparse point refused", code);
        }
    }
}

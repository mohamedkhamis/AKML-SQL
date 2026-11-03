using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class HostingRepairGuardTests
{
    [Fact]
    public void The_default_Program_Files_ACL_has_no_effective_non_admin_write_grant()
        => Assert.Equal("0", HostingTestTools.DotSource("web-hosting-repair.ps1",
            "@(([IO.Directory]::GetAccessControl($env:ProgramFiles)).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object { Test-UnsafeHostingWriteAce $_.IdentityReference.Value $_.FileSystemRights $_.AccessControlType $_.PropagationFlags }).Count"));

    [Theory]
    [InlineData("S-1-5-11", "Modify", "None", true)]
    [InlineData("S-1-5-32-545", "Write", "None", true)]
    [InlineData("S-1-1-0", "Delete", "None", true)]
    [InlineData("S-1-5-32-544", "FullControl", "None", false)]
    [InlineData("S-1-5-18", "FullControl", "None", false)]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", "FullControl", "None", false)]
    [InlineData("S-1-3-0", "FullControl", "InheritOnly", false)]
    [InlineData("S-1-5-32-545", "ReadAndExecute", "None", false)]
    [InlineData("S-1-15-2-1", "ReadAndExecute", "None", false)]
    public void Only_effective_non_admin_write_grants_are_unsafe(string sid, string rights, string propagation, bool expected)
        => Assert.Equal(expected.ToString(), HostingTestTools.DotSource("web-hosting-repair.ps1",
            $"Test-UnsafeHostingWriteAce -Sid '{sid}' -Rights {rights} -Propagation {propagation}"));

    [Fact]
    public void A_deny_ACE_is_not_a_write_grant()
        => Assert.Equal("False", HostingTestTools.DotSource("web-hosting-repair.ps1",
            "Test-UnsafeHostingWriteAce -Sid 'S-1-5-32-545' -Rights FullControl -AccessType Deny"));
}

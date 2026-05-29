using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Engine.Pairing;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 026 (M4 closure) M1: <see cref="SecureDirectory.EnsureSecured"/> must create the web-state
/// directory with an Administrators+SYSTEM-only, inheritance-disabled ACL so the pairing PIN /
/// bearer-token store are never world-readable when the engine is the directory's creator
/// (developer runs, a manual <c>sc start</c>, or a deleted directory) -- the paths the installer's
/// icacls cannot cover. Closes the review's "no test asserts the pairing-pin.txt ACL" gap.
/// </summary>
public sealed class SecureDirectoryTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void EnsureSecured_creates_dir_admins_and_system_only_no_users()
    {
        if (!OperatingSystem.IsWindows()) return;   // ACL assertions are Windows-only

        var dir = Path.Combine(Path.GetTempPath(), "akml-securedir-" + Guid.NewGuid().ToString("N"));
        try
        {
            SecureDirectory.EnsureSecured(dir);
            Assert.True(Directory.Exists(dir));

            var security = new DirectoryInfo(dir).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected, "Inheritance must be disabled (protected ACL).");

            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            var hasAdminsFull = false;
            var hasSystemFull = false;
            foreach (FileSystemAccessRule rule in
                security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                Assert.NotEqual(users, sid);        // no standard-user ACE survives
                Assert.NotEqual(authUsers, sid);
                if (rule.AccessControlType == AccessControlType.Allow)
                {
                    if (sid == admins && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)) hasAdminsFull = true;
                    if (sid == system && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)) hasSystemFull = true;
                }
            }

            Assert.True(hasAdminsFull, "Administrators must have FullControl.");
            Assert.True(hasSystemFull, "SYSTEM must have FullControl.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureSecured_leaves_existing_directory_intact_and_never_throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akml-securedir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var marker = Path.Combine(dir, "marker.txt");
        File.WriteAllText(marker, "x");
        try
        {
            SecureDirectory.EnsureSecured(dir);   // must no-op on an existing dir
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(marker), "Existing contents must be preserved (no recreate).");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}

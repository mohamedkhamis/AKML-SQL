using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 026 (M4 closure) M1. Creates the web-edition shared-state directory
    /// (<c>%CommonAppData%\AKML SQL Web\</c>) with a restrictive ACL — Administrators + SYSTEM full
    /// control, inheritance disabled — so secrets written into it (<c>pairing-pin.txt</c>, the
    /// bearer-token store) are never readable by standard users. This closes the window in which a
    /// world-readable PIN (a leaked PIN allows local-operator impersonation onto the LAN bridge)
    /// could exist before the installer's <c>icacls</c> step runs, and protects the engine when it
    /// is started OUTSIDE the installer entirely (developer runs, a manual <c>sc start</c>, or after
    /// the directory was deleted) — paths the installer ACL cannot cover.
    ///
    /// <para>An already-existing directory is left intact: the installer owns the canonical ACL and a
    /// re-run must not clobber it (pairing-pin-file-contract C4). Best-effort — ACL failures are
    /// logged, never thrown (FR-013: the PIN is still served from memory and engine startup must not
    /// crash). On non-Windows hosts (tests on CI) it falls back to a plain create.</para>
    /// </summary>
    internal static class SecureDirectory
    {
        /// <summary>
        /// Ensure <paramref name="dir"/> exists. If it must be created, create it hardened
        /// (Windows); if it already exists, leave its ACL untouched.
        /// </summary>
        public static void EnsureSecured(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (Directory.Exists(dir)) return;   // installer (or a prior run) owns the ACL

                if (OperatingSystem.IsWindows())
                {
                    CreateHardenedWindows(dir!);
                }
                else
                {
                    Directory.CreateDirectory(dir!);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SecureDirectory: could not create hardened {Dir}; falling back to default ACL", dir);
                try { Directory.CreateDirectory(dir!); } catch { /* best effort -- never crash startup */ }
            }
        }

        [SupportedOSPlatform("windows")]
        private static void CreateHardenedWindows(string dir)
        {
            var security = new DirectorySecurity();

            // Disable inheritance and drop inherited ACEs (e.g. ProgramData's BUILTIN\Users:(RX)),
            // so only the explicit ACEs added below apply.
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // SIDs (not localized names): S-1-5-32-544 = Administrators, S-1-5-18 = LocalSystem.
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            security.AddAccessRule(new FileSystemAccessRule(
                admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));

            // Create the directory WITH this descriptor in one step -- there is never a moment where
            // the directory exists under the default (Users-readable) ACL. FileSystemAclExtensions
            // (System.IO) is the .NET (Core) replacement for the removed DirectoryInfo.Create(security).
            security.CreateDirectory(dir);
        }
    }
}

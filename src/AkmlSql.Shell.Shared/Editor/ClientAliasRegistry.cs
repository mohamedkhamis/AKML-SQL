#nullable enable
using System;
using AkmlSql.Core.Models.Connections;
using Microsoft.Win32;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Reads SQL Server client aliases (<c>HKLM\…\MSSQLServer\Client\ConnectTo</c>, 64-bit then
    /// 32-bit) so AKML SQL can say where an alias points. The engine resolves the same aliases for
    /// its connections (<c>SqlAliasResolution</c>); this is for display in the shell.
    /// </summary>
    internal static class ClientAliasRegistry
    {
        /// <summary>The alias's registry value (e.g. <c>DBMSSOCN,192.168.4.5,1433</c>), or null.</summary>
        public static string? Lookup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = machine.OpenSubKey(SqlServerAliases.RegistryKey))
                    {
                        if (key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
                {
                    Log.Debug(ex, "ClientAliasRegistry: lookup of '{Name}' failed in the {View} registry", name, view);
                }
            }
            return null;
        }
    }
}

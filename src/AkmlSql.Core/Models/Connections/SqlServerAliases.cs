#nullable enable
using System;

namespace AkmlSql.Core.Models.Connections
{
    /// <summary>
    /// SQL Server client aliases (SQL Server Configuration Manager / cliconfg): a name such as
    /// <c>ServerDemo</c> stored under <c>HKLM\SOFTWARE\Microsoft\MSSQLServer\Client\ConnectTo</c>
    /// (64-bit) or <c>…\WOW6432Node\…</c> (32-bit) with a value such as
    /// <c>DBMSSOCN,192.168.4.5,1433</c>. SSMS resolves them through its client stack; the
    /// Microsoft.Data.SqlClient build the engine uses does not, so a schema connection to
    /// <c>ServerDemo</c> failed with "server not found". AKML resolves the alias itself.
    /// </summary>
    /// <remarks>Pure logic: the registry read is passed in, so the shell (net472) and the engine
    /// (.NET 10, Windows) share it and it is testable.</remarks>
    public static class SqlServerAliases
    {
        /// <summary>The 64-bit registry key holding client aliases.</summary>
        public const string RegistryKey = @"SOFTWARE\Microsoft\MSSQLServer\Client\ConnectTo";

        /// <summary>The 32-bit (WOW64) registry key holding client aliases.</summary>
        public const string RegistryKey32 = @"SOFTWARE\WOW6432Node\Microsoft\MSSQLServer\Client\ConnectTo";

        /// <summary>
        /// The data source an alias value points at: <c>DBMSSOCN,host,1433</c> → <c>tcp:host,1433</c>,
        /// <c>DBMSSOCN,host\inst</c> → <c>tcp:host\inst</c>, <c>DBNMPNTW,\\host\pipe\sql\query</c> →
        /// <c>np:\\host\pipe\sql\query</c>, <c>DBMSLPCN,host</c> → <c>lpc:host</c>. Null when the value
        /// is empty or the protocol is unknown.
        /// </summary>
        public static string? ParseTarget(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var parts = value!.Split(',');
            if (parts.Length < 2) return null;

            var protocol = parts[0].Trim();
            var server = parts[1].Trim();
            if (server.Length == 0) return null;
            var port = parts.Length > 2 ? parts[2].Trim() : string.Empty;

            switch (protocol.ToUpperInvariant())
            {
                case "DBMSSOCN": // TCP/IP
                    return port.Length > 0 ? $"tcp:{server},{port}" : $"tcp:{server}";
                case "DBNMPNTW": // named pipes
                    return server.StartsWith(@"\\", StringComparison.Ordinal) ? $"np:{server}" : $@"np:\\{server}\pipe\sql\query";
                case "DBMSLPCN": // shared memory
                    return $"lpc:{server}";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The server to connect to for <paramref name="serverName"/>: the alias's target when
        /// <paramref name="lookup"/> (alias name → registry value, or null) knows it, otherwise the
        /// name unchanged. Names that already carry a protocol (<c>tcp:</c>, <c>np:</c>, <c>lpc:</c>,
        /// <c>admin:</c>) or a port (<c>host,1433</c>) are never aliases.
        /// </summary>
        public static string Resolve(string? serverName, Func<string, string?> lookup)
        {
            if (string.IsNullOrWhiteSpace(serverName) || lookup == null) return serverName ?? string.Empty;
            var name = serverName!.Trim();
            if (name.IndexOf(':') >= 0 || name.IndexOf(',') >= 0) return serverName;

            string? value;
            try { value = lookup(name); }
            catch (Exception) { return serverName; }

            return ParseTarget(value) ?? serverName;
        }

        /// <summary>True when <paramref name="resolved"/> differs from what the user typed (an alias was applied).</summary>
        public static bool IsAlias(string? serverName, string? resolved) =>
            !string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(resolved)
            && !string.Equals(serverName!.Trim(), resolved!.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

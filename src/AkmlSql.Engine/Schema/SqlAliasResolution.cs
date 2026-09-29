using AkmlSql.Core.Models.Connections;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using Serilog;

namespace AkmlSql.Engine.Schema;

/// <summary>
/// SQL Server client aliases for the engine's own connections. SSMS resolves an alias such as
/// <c>ServerDemo</c> through its client stack and puts the alias name in the query window; the
/// Microsoft.Data.SqlClient build the engine uses does not read the alias registry, so schema
/// loading, Test connection and the database list failed with "server not found". Every
/// connection string the engine receives passes through <see cref="Apply"/> first — before the
/// bridge guard, which must judge the server the alias really points at.
/// </summary>
internal static class SqlAliasResolution
{
    /// <summary>Alias name → registry value (<c>DBMSSOCN,host,port</c>), or null. Replaceable in tests.</summary>
    internal static Func<string, string?> Lookup { get; set; } = RegistryLookup;

    /// <summary>
    /// <paramref name="connectionString"/> with its Data Source replaced by the alias's target
    /// when the Data Source is a client alias on this machine; otherwise unchanged.
    /// </summary>
    public static string Apply(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return connectionString ?? string.Empty;

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return connectionString; // the handlers report an invalid string themselves
        }

        var alias = builder.DataSource;
        var target = SqlServerAliases.Resolve(alias, Lookup);
        if (!SqlServerAliases.IsAlias(alias, target)) return connectionString;

        Log.Information("SQL Server client alias '{Alias}' resolved to '{Target}'", alias, target);
        builder.DataSource = target;
        return builder.ConnectionString;
    }

    /// <summary>
    /// The alias's value from <c>HKLM\…\MSSQLServer\Client\ConnectTo</c>: the 64-bit key first,
    /// then the 32-bit one (SQL Server Configuration Manager writes both; older tools only one).
    /// </summary>
    internal static string? RegistryLookup(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(SqlServerAliases.RegistryKey);
                if (key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value)) return value;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Debug(ex, "SQL Server alias lookup for '{Name}' failed in the {View} registry", name, view);
            }
        }
        return null;
    }
}

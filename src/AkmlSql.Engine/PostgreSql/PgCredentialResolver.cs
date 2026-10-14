using AkmlSql.Core.Config;
using AkmlSql.Core.Connections;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-104 to FR-106, research R14) - the remembered PostgreSQL password. It lives only in the engine's protected
/// store (<see cref="SqlCredentialStore"/>: DPAPI for the account the engine runs as, atomic writes, the same file as the
/// SQL Server logins), under the server key <c>pg://host:port</c> and the user name. A connection that arrives without a
/// password gets it filled in here; one that arrives with a password and "remember" saves it after the open succeeds. It is
/// never returned to a client and never logged.
///
/// <para>
/// Honest scope: on the web engine service the account is LocalSystem, so any administrator of that machine can read the
/// store. That is why the web edition makes remembering an opt-in per connection and says so in the opt-in text.
/// </para>
/// </summary>
public sealed class PgCredentialResolver
{
    /// <summary>The connection string to open, and whether a remembered password was put into it.</summary>
    public sealed record Resolution(string ConnectionString, bool PasswordInjected);

    /// <summary>
    /// Fills in the remembered password when <paramref name="connectionString"/> has none. A string that does not parse, or has
    /// no host or user, comes back unchanged (the open reports why).
    /// </summary>
    public Resolution Resolve(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { return new Resolution(connectionString, false); }

        if (!string.IsNullOrEmpty(builder.Password) || string.IsNullOrEmpty(builder.Host) || string.IsNullOrEmpty(builder.Username))
            return new Resolution(connectionString, false);

        if (!SqlCredentialStore.TryGet(PgConnectionString.ServerKey(builder.Host, builder.Port), builder.Username, out var password))
            return new Resolution(connectionString, false);

        builder.Password = password;
        return new Resolution(builder.ConnectionString, true);
    }

    /// <summary>Saves the string's password after a successful open, when the user asked to remember it and typed one.</summary>
    public void RememberIfRequested(string connectionString, bool remember)
    {
        if (!remember) return;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(builder.Password) || string.IsNullOrEmpty(builder.Host) || string.IsNullOrEmpty(builder.Username)) return;
            Save(builder.Host, builder.Port, builder.Username, builder.Password);
        }
        catch (ArgumentException)
        {
            // Nothing sensible to remember from a string that does not parse.
        }
    }

    public void Save(string host, int port, string login, string password)
    {
        SqlCredentialStore.Save(PgConnectionString.ServerKey(host, port), login, password);
        Log.Information("PostgreSQL password remembered for {Login}@{Server}", login, PgConnectionString.ServerKey(host, port));
    }

    public bool Has(string host, int? port, string login) =>
        SqlCredentialStore.Has(PgConnectionString.ServerKey(host, port), login);

    /// <summary>Removes the remembered password; true when there was one.</summary>
    public bool Forget(string host, int? port, string login)
    {
        var key = PgConnectionString.ServerKey(host, port);
        if (!SqlCredentialStore.Has(key, login)) return false;
        SqlCredentialStore.Remove(key, login);
        Log.Information("PostgreSQL password forgotten for {Login}@{Server}", login, key);
        return true;
    }
}

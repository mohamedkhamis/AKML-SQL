using AkmlSql.Core.Config;
using AkmlSql.Core.Connections;
using AkmlSql.Engine.Pairing;
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

    // What writes and removes an entry (true when the store file was written; see SqlCredentialStore). A test makes them fail or
    // throw to check that a store failure is an answer, never a throw and never a false "remembered" or "forgotten".
    private readonly Func<string, string, string, bool> _saveToStore;
    private readonly Func<string, string, bool> _removeFromStore;

    public PgCredentialResolver() : this(SqlCredentialStore.Save, SqlCredentialStore.Remove) { }

    internal PgCredentialResolver(Func<string, string, string, bool> saveToStore, Func<string, string, bool> removeFromStore)
    {
        _saveToStore = saveToStore ?? throw new ArgumentNullException(nameof(saveToStore));
        _removeFromStore = removeFromStore ?? throw new ArgumentNullException(nameof(removeFromStore));
    }

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

    /// <summary>
    /// Saves the string's password after a successful open, when the user asked to remember it and typed one; true when it is now
    /// in the store. Never throws (spec 043 follow-up, review): its caller is the background work of <c>ConnectionChanged</c>, and
    /// a store that cannot keep the password must not stop the schema from loading.
    /// </summary>
    public bool RememberIfRequested(string connectionString, bool remember)
    {
        if (!remember) return false;
        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return false;   // nothing sensible to remember from a string that does not parse
        }
        if (string.IsNullOrEmpty(builder.Password) || string.IsNullOrEmpty(builder.Host) || string.IsNullOrEmpty(builder.Username)) return false;
        return Save(builder.Host, builder.Port, builder.Username, builder.Password);
    }

    /// <summary>
    /// Saves the password for the server and login; true when it is now in the store. Spec 043 follow-up (review): a store that
    /// cannot be written, or cannot encrypt it (DPAPI), is logged — the login, the server and the failure's type, never the
    /// password — and answered false, never thrown; "remembered" is logged only for a password the store really holds.
    /// </summary>
    public bool Save(string host, int port, string login, string password)
    {
        var key = PgConnectionString.ServerKey(host, port);
        try
        {
            if (!_saveToStore(key, login, password))
            {
                Log.Warning("PostgreSQL password not remembered for {Login}@{Server}: the credential store could not be written", login, key);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("PostgreSQL password not remembered for {Login}@{Server}: {Type}", login, key, ex.GetType().Name);
            return false;
        }
        Log.Information("PostgreSQL password remembered for {Login}@{Server}", login, key);
        return true;
    }

    public bool Has(string host, int? port, string login) =>
        SqlCredentialStore.Has(PgConnectionString.ServerKey(host, port), login);

    /// <summary>
    /// Removes the remembered password; true when there was one. Throws <see cref="CredentialStoreWriteException"/> when the store
    /// could not be written (spec 043 follow-up, review): the password is still there and must not be reported as forgotten.
    /// </summary>
    public bool Forget(string host, int? port, string login)
    {
        var key = PgConnectionString.ServerKey(host, port);
        // Spec 043 follow-up (review, round 3): the removal runs even when Has says no. A store that cannot be read answers Has
        // "no", so skipping the removal answered it "nothing to forget" while the password stayed in the file; the removal
        // refuses an unreadable store (false). A readable store without the entry removes nothing and says so.
        var had = SqlCredentialStore.Has(key, login);
        if (!_removeFromStore(key, login))
        {
            Log.Warning("PostgreSQL password not forgotten for {Login}@{Server}: the credential store could not be read or written", login, key);
            throw new CredentialStoreWriteException();
        }
        if (had) Log.Information("PostgreSQL password forgotten for {Login}@{Server}", login, key);
        return had;
    }
}

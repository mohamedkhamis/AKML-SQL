using AkmlSql.Core.Connections;
using Npgsql;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>What the engine learns about a PostgreSQL server when a session connects.</summary>
/// <param name="Database">The database the connection is in (<c>current_database()</c>).</param>
/// <param name="User">The role the connection runs as (<c>current_user</c>).</param>
/// <param name="VersionBanner">The server's <c>version()</c> text.</param>
/// <param name="ServerVersionText">The short version for the status bar, for example <c>18.1</c>.</param>
/// <param name="ServerVersionNumber"><c>server_version_num</c>, for example 180001.</param>
/// <param name="SearchPath"><c>current_schemas(false)</c>: the schemas unqualified names resolve in, in order.</param>
public sealed record PgConnectionFacts(string Database, string User, string VersionBanner, string ServerVersionText,
    int ServerVersionNumber, string[] SearchPath);

/// <summary>
/// Spec 042 (task T052, contracts/catalog-queries.md "Connect-time facts") - the one place a PostgreSQL connection is
/// built. Every connection the engine opens goes through <see cref="Create"/> so they all carry the same engine-side
/// settings: the product's application name, a bounded connect timeout and <c>Include Error Detail</c>.
///
/// <para>
/// <c>Include Error Detail</c> is on here and nowhere else because the driver leaves the server's DETAIL line out of an
/// error unless asked, and DETAIL is what names the offending key or value. It can echo data; it is shown to the user who
/// ran the statement and written to no log (FR-103).
/// </para>
/// </summary>
public static class PgConnectionFactory
{
    /// <summary>The connection string the driver should open: the caller's, plus the engine-side settings it did not give.</summary>
    public static string Normalize(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = true,
        };
        if (string.IsNullOrWhiteSpace(builder.ApplicationName)) builder.ApplicationName = PgConnectionString.ApplicationName;
        return builder.ConnectionString;
    }

    /// <summary>A new, unopened connection. Throws <see cref="ArgumentException"/> for a string the driver cannot parse.</summary>
    public static NpgsqlConnection Create(string connectionString) => new(Normalize(connectionString));

    /// <summary>
    /// Opens a connection, asks the server who and what it is, and closes it. A failure to open throws the driver's
    /// exception unchanged, so the caller can word it (wrong port, bad password, untrusted certificate).
    /// </summary>
    public static async Task<PgConnectionFacts> ReadFactsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = Create(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return await ReadFactsAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>The facts of an already open connection.</summary>
    public static async Task<PgConnectionFacts> ReadFactsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT current_database(), current_user, version(), current_setting('server_version_num')::int, current_schemas(false)",
            connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The server returned no connection facts.");

        var number = reader.GetInt32(3);
        var searchPath = reader.IsDBNull(4) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(4);
        return new PgConnectionFacts(
            Database: reader.GetString(0),
            User: reader.GetString(1),
            VersionBanner: reader.GetString(2),
            ServerVersionText: VersionText(number),
            ServerVersionNumber: number,
            SearchPath: searchPath);
    }

    /// <summary>
    /// <c>server_version_num</c> as the short text PostgreSQL itself uses: 180001 is <c>18.1</c> (since version 10 the
    /// number is major * 10000 + minor), and 90624 is <c>9.6.24</c> (before it: major, minor and patch in two digits each).
    /// </summary>
    public static string VersionText(int serverVersionNumber)
    {
        if (serverVersionNumber >= 100000)
            return $"{serverVersionNumber / 10000}.{serverVersionNumber % 10000}";
        return $"{serverVersionNumber / 10000}.{serverVersionNumber / 100 % 100}.{serverVersionNumber % 100}";
    }
}

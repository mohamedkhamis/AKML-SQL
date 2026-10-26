using System.Net.Sockets;
using System.Security.Authentication;
using AkmlSql.Core.Ipc.Messages;
using Npgsql;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>What to tell the person who tried to connect, and the stable id when the failure has one.</summary>
/// <param name="Message">The server's own words where it spoke, the driver's where it did not, and a one-line hint.</param>
/// <param name="ErrorCode">A <see cref="DialectErrors"/> id (a password is needed, the password was refused), otherwise null.</param>
public sealed record ConnectionFailure(string Message, string? ErrorCode);

/// <summary>
/// Spec 042 (task T055, FR-002, FR-007, FR-008) - words for the ways a PostgreSQL connection fails. A failure is reported
/// with the server's message and a one-line hint, never in SQL Server terms, and never with the connection string (it can
/// hold the password). When the other end does not speak PostgreSQL at all, the message says to check the database kind.
///
/// <para>
/// The driver has no typed signal for most of these, so the classification reads the exception types it does expose
/// (a server error with its SQLSTATE, a socket error, a TLS error) and, for the password case, the one message it gives.
/// </para>
///
/// <para>
/// Spec 043 follow-up (review): a password the server refused (SQLSTATE 28P01) carries <see cref="DialectErrors.PgLoginFailed"/>,
/// so the web client knows a remembered or copied password is wrong or out of date without reading the server's words, which
/// vary with its language. The message is unchanged.
/// </para>
/// </summary>
public static class PgConnectionFailure
{
    /// <summary>The driver's text when the server wants a password the connection string did not have.</summary>
    private const string NoPasswordSignature = "No password has been provided";

    /// <summary>Describes why opening a PostgreSQL connection to <paramref name="host"/>:<paramref name="port"/> failed.</summary>
    public static ConnectionFailure Describe(Exception ex, string host, int port)
    {
        var target = host + ":" + port;

        switch (ex)
        {
            case PostgresException pg:
                return new ConnectionFailure(WithHint($"{pg.MessageText} (SQLSTATE {pg.SqlState})", ServerHint(pg)),
                    pg.SqlState == PostgresErrorCodes.InvalidPassword ? DialectErrors.PgLoginFailed : null);

            case NpgsqlException when ex.Message.Contains(NoPasswordSignature, StringComparison.Ordinal):
                return new ConnectionFailure("The server asked for a password and none was supplied. Enter the password for this user.",
                    DialectErrors.PasswordRequired);

            case NpgsqlException when IsTlsFailure(ex):
                return new ConnectionFailure(
                    WithHint($"The TLS connection to {target} failed: {Innermost(ex).Message.TrimEnd('.', ' ')}",
                        "Use TLS mode require or prefer, or give the root certificate for verify-ca and verify-full."),
                    null);

            case NpgsqlException when ex.InnerException is SocketException socket:
                return new ConnectionFailure(
                    WithHint($"Could not reach {target}: {socket.Message}",
                        "Check the host name and port, and that the server accepts TCP connections."),
                    null);

            case NpgsqlException when ex.InnerException is TimeoutException:
            case TimeoutException:
                return new ConnectionFailure(
                    WithHint($"Timed out connecting to {target}.", "Check that the server is running and reachable."), null);

            case NpgsqlException when ex.InnerException is EndOfStreamException or IOException:
                return new ConnectionFailure(WrongKind(target, "PostgreSQL"), null);

            case ArgumentException:
                // The driver's text can quote the setting, which could be the password.
                return new ConnectionFailure("The PostgreSQL connection settings are not valid.", null);

            default:
                return new ConnectionFailure(ex.Message, null);
        }
    }

    /// <summary>
    /// <see cref="Describe"/>, and for a certificate the verifying TLS modes refused, the specific reason (FR-101): the driver says the same sentence for an
    /// authority it does not know, an expired certificate and a name the certificate does not carry, so the handshake is looked at once more
    /// (<see cref="PgTlsDiagnosis"/>). When that cannot be done the driver's sentence stands, so a diagnosis never turns a refusal into a different outcome.
    /// </summary>
    /// <param name="connectionString">The string that failed. Only its host, port, TLS mode and root certificate are read, and it is never logged or returned.</param>
    public static Task<ConnectionFailure> DescribeAsync(Exception ex, string connectionString, string host, int port, CancellationToken ct) =>
        DescribeAsync(ex, connectionString, host, port, PgTlsDiagnosis.ExplainAsync, ct);

    /// <param name="explain">What looks at the handshake again; a seam so a test can make it fail.</param>
    internal static async Task<ConnectionFailure> DescribeAsync(Exception ex, string connectionString, string host, int port,
        Func<string, CancellationToken, Task<string?>> explain, CancellationToken ct)
    {
        var generic = Describe(ex, host, port);
        try
        {
            if (ex is NpgsqlException && ex.InnerException is AuthenticationException)
            {
                var reason = await explain(connectionString, ct).ConfigureAwait(false);
                if (reason is not null) return new ConnectionFailure($"The TLS connection to {host}:{port} failed: {reason}", null);
            }
            else if (PgTlsDiagnosis.UnreadableRoot(ex, connectionString) is { } unreadable)
            {
                return new ConnectionFailure($"The TLS connection to {host}:{port} failed: {unreadable}", null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A diagnosis only words a refusal that has already happened; whatever goes wrong inside it leaves the driver's sentence, never a thrown error.
        }
        return generic;
    }

    /// <summary>
    /// FR-008 for a SQL Server connection: its driver reports a server that answers in another protocol as a plain network
    /// error, so the only reliable sign is the port. A failure of that kind against 5432, PostgreSQL's usual port, is
    /// reworded; anything else returns null and the driver's message stands.
    /// </summary>
    /// <param name="sqlErrorNumber">The driver's error number (258 and 53 are its network errors, 64 and 233 its handshake errors).</param>
    /// <param name="dataSource">The connection string's data source, for example <c>db1,5432</c> or <c>tcp:db1,5432</c>.</param>
    public static string? WrongKindForSqlServer(int sqlErrorNumber, string? dataSource)
    {
        if (sqlErrorNumber is not (258 or 53 or 2 or 64 or 233 or -1 or 10054)) return null;
        if (string.IsNullOrWhiteSpace(dataSource)) return null;

        var comma = dataSource.LastIndexOf(',');
        if (comma < 0 || !int.TryParse(dataSource.AsSpan(comma + 1).Trim(), out var port) || port != 5432) return null;

        var host = dataSource.Substring(0, comma).Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);
        return WrongKind(host + ":" + port, "SQL Server") + " Port 5432 is the usual port of PostgreSQL.";
    }

    /// <summary>FR-008: "The server at host:port did not answer as a PostgreSQL server. Check the database kind."</summary>
    public static string WrongKind(string target, string kindName) =>
        $"The server at {target} did not answer as a {kindName} server. Check the database kind.";

    private static string WithHint(string message, string? hint) => string.IsNullOrEmpty(hint) ? message + "." : message + ". " + hint;

    // One line about what usually fixes the error. Only the ones a person can act on; the rest stand on the server's text.
    private static string? ServerHint(PostgresException pg) => pg.SqlState switch
    {
        "28P01" => "Check the user name and password.",
        "28000" => "The server's access rules may not allow this user, host or TLS setting (see pg_hba.conf).",
        "3D000" => "Check the database name; connect to postgres to list the databases.",
        "57P03" => "The server is starting up; try again in a moment.",
        "53300" => "The server has no free connection slots; close another client or raise max_connections.",
        "42501" => "The user lacks permission to connect to this database.",
        _ => null,
    };

    private static bool IsTlsFailure(Exception ex) =>
        ex.InnerException is AuthenticationException
        || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase);

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }
}

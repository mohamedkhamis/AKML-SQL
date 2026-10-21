using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Tests.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (task T045, FR-002, FR-007, FR-008): Test connection by kind. The wording of each way a PostgreSQL
/// connection can fail is checked with failures built by hand, so these run anywhere; the live ones run against the
/// throwaway server (<c>AKML_PG_TEST_CONNECTION</c>) and skip without it.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
[Trait("Category", "PostgreSql")]
public class TestSqlConnectionHandlerPgTests
{
    // ── The words (no server) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_server_error_keeps_its_own_text_and_gains_a_one_line_hint()
    {
        var wrongPassword = new PostgresException("password authentication failed for user \"app\"", "FATAL", "FATAL", "28P01");
        var unknownDatabase = new PostgresException("database \"nodb\" does not exist", "FATAL", "FATAL", "3D000");

        var password = PgConnectionFailure.Describe(wrongPassword, "db1", 5432);
        var database = PgConnectionFailure.Describe(unknownDatabase, "db1", 5432);

        Assert.StartsWith("password authentication failed for user \"app\" (SQLSTATE 28P01).", password.Message);
        Assert.Contains("Check the user name and password.", password.Message);
        Assert.Null(password.ErrorCode);
        Assert.Contains("database \"nodb\" does not exist (SQLSTATE 3D000)", database.Message);
        Assert.Contains("list the databases", database.Message);
    }

    [Fact]
    public void A_server_that_wants_a_password_the_connection_does_not_have_has_its_own_id()
    {
        var ex = new NpgsqlException("No password has been provided but the backend requires one (in SASL/SCRAM-SHA-256)");

        var failure = PgConnectionFailure.Describe(ex, "db1", 5432);

        Assert.Equal(DialectErrors.PasswordRequired, failure.ErrorCode);
        Assert.Contains("password", failure.Message);
    }

    [Fact]
    public void A_server_that_does_not_speak_the_protocol_is_reported_as_the_wrong_kind()
    {
        var ex = new NpgsqlException("Exception while reading from stream", new EndOfStreamException("Attempted to read past the end of the stream."));

        var failure = PgConnectionFailure.Describe(ex, "db1", 1433);

        Assert.Equal("The server at db1:1433 did not answer as a PostgreSQL server. Check the database kind.", failure.Message);
    }

    [Fact]
    public void An_unreachable_host_a_timeout_and_a_rejected_certificate_each_say_what_to_check()
    {
        var refused = PgConnectionFailure.Describe(
            new NpgsqlException("Failed to connect to 10.0.0.9:5432", new SocketException((int)SocketError.ConnectionRefused)), "10.0.0.9", 5432);
        var timeout = PgConnectionFailure.Describe(
            new NpgsqlException("Exception while connecting", new TimeoutException()), "10.0.0.9", 5432);
        var tls = PgConnectionFailure.Describe(
            new NpgsqlException("Exception while performing SSL handshake",
                new AuthenticationException("The remote certificate was rejected by the provided RemoteCertificateValidationCallback.")),
            "10.0.0.9", 5432);

        Assert.Contains("Could not reach 10.0.0.9:5432", refused.Message);
        Assert.Contains("Check the host name and port", refused.Message);
        Assert.Contains("Timed out connecting to 10.0.0.9:5432", timeout.Message);
        Assert.Contains("The TLS connection to 10.0.0.9:5432 failed", tls.Message);
        Assert.Contains("root certificate", tls.Message);
    }

    [Fact]
    public void A_setting_the_driver_rejects_is_not_quoted_back()
    {
        // The driver's own text can include the offending setting, which might be the password.
        var failure = PgConnectionFailure.Describe(new ArgumentException("Couldn't set Password to 'hunter2'"), "db1", 5432);

        Assert.DoesNotContain("hunter2", failure.Message);
    }

    [Fact]
    public void No_failure_of_a_PostgreSQL_connection_is_worded_as_a_SQL_Server_one()
    {
        var failures = new[]
        {
            PgConnectionFailure.Describe(new PostgresException("boom", "ERROR", "ERROR", "XX000"), "h", 5432),
            PgConnectionFailure.Describe(new NpgsqlException("No password has been provided"), "h", 5432),
            PgConnectionFailure.Describe(new NpgsqlException("x", new EndOfStreamException()), "h", 1433),
            PgConnectionFailure.Describe(new NpgsqlException("x", new SocketException()), "h", 5432),
            PgConnectionFailure.Describe(new NpgsqlException("x", new TimeoutException()), "h", 5432),
            PgConnectionFailure.Describe(new NpgsqlException("SSL"), "h", 5432),
            PgConnectionFailure.Describe(new ArgumentException("x"), "h", 5432),
        };

        // Ordinal on purpose: "PostgreSQL server" is fine, the product name "SQL Server" is not.
        Assert.All(failures, f => Assert.False(
            f.Message.Contains("SQL Server", StringComparison.Ordinal), "Worded as SQL Server: " + f.Message));
    }

    [Fact]
    public void A_SQL_Server_network_failure_against_the_PostgreSQL_port_is_reworded_and_others_are_not()
    {
        var reworded = PgConnectionFailure.WrongKindForSqlServer(258, "db1,5432");
        Assert.NotNull(reworded);
        Assert.Contains("The server at db1:5432 did not answer as a SQL Server server. Check the database kind.", reworded);

        Assert.Contains("db1:5432", PgConnectionFailure.WrongKindForSqlServer(53, "tcp:db1,5432"));
        Assert.Null(PgConnectionFailure.WrongKindForSqlServer(258, "db1,1433"));
        Assert.Null(PgConnectionFailure.WrongKindForSqlServer(258, "db1"));
        Assert.Null(PgConnectionFailure.WrongKindForSqlServer(18456, "db1,5432"));   // a login failure is the server's answer, not a wrong kind
        Assert.Null(PgConnectionFailure.WrongKindForSqlServer(258, null));
    }

    // ── The handler (no server for the first two) ───────────────────────────────────────────────

    private static Task<TestSqlConnectionResponse> TestAsync(string connectionString, DatabaseKind kind = DatabaseKind.PostgreSql) =>
        new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest { ConnectionString = connectionString, Kind = (int)kind }, null!, default);

    [Fact]
    public async Task A_bridge_request_with_integrated_security_is_refused_with_its_id_before_any_connection()
    {
        using var _ = BridgeSourceIp.Set(IPAddress.Loopback);

        var response = await TestAsync("Host=db1;Username=u;Integrated Security=true");

        Assert.False(response.Ok);
        Assert.Equal(DialectErrors.CredentialsRequired, response.ErrorCode);
        Assert.Contains("explicit user name and password", response.ErrorMessage);
    }

    [Fact]
    public async Task A_closed_port_says_it_could_not_reach_the_server()
    {
        var response = await TestAsync(PgConnectionString.Build("localhost", 55999, "postgres", "u", "p", "disable", null));

        Assert.False(response.Ok);
        Assert.Contains("Could not reach localhost:55999", response.ErrorMessage);
        Assert.DoesNotContain("SQL Server", response.ErrorMessage);
        Assert.DoesNotContain("Password", response.ErrorMessage);
    }

    // ── Live ────────────────────────────────────────────────────────────────────────────────────

    private static (string Host, int Port, string User, string Password) Admin()
    {
        var b = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        return (b.Host!, b.Port, b.Username!, b.Password!);
    }

    [SkippableFact]
    public async Task A_good_connection_reports_the_product_and_version()
    {
        PgTestServer.SkipUnlessAvailable();
        var a = Admin();

        var response = await TestAsync(PgConnectionString.Build(a.Host, a.Port, "postgres", a.User, a.Password, "prefer", null));

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.Matches(@"^PostgreSQL \d+\.\d+", response.ServerVersion);
        Assert.Null(response.ErrorMessage);
    }

    [SkippableFact]
    public async Task Wrong_password_unknown_database_and_missing_password_are_told_apart()
    {
        PgTestServer.SkipUnlessAvailable();
        var a = Admin();

        var wrongPassword = await TestAsync(PgConnectionString.Build(a.Host, a.Port, "postgres", a.User, "not-the-password", "prefer", null));
        var unknownDatabase = await TestAsync(PgConnectionString.Build(a.Host, a.Port, "no_such_db", a.User, a.Password, "prefer", null));
        var noPassword = await TestAsync(PgConnectionString.Build(a.Host, a.Port, "postgres", a.User, null, "prefer", null));

        Assert.False(wrongPassword.Ok);
        Assert.Contains("password authentication failed", wrongPassword.ErrorMessage);
        Assert.Contains("28P01", wrongPassword.ErrorMessage);
        Assert.Null(wrongPassword.ErrorCode);

        Assert.False(unknownDatabase.Ok);
        Assert.Contains("3D000", unknownDatabase.ErrorMessage);

        Assert.False(noPassword.Ok);
        Assert.Equal(DialectErrors.PasswordRequired, noPassword.ErrorCode);
    }

    [SkippableFact]
    public async Task A_remembered_password_is_used_when_the_string_has_none()
    {
        PgTestServer.SkipUnlessAvailable();
        var a = Admin();
        var resolver = new PgCredentialResolver();
        resolver.Save(a.Host, a.Port, a.User, a.Password);
        try
        {
            var response = await TestAsync(PgConnectionString.Build(a.Host, a.Port, "postgres", a.User, null, "prefer", null));

            Assert.True(response.Ok, response.ErrorMessage);
        }
        finally
        {
            resolver.Forget(a.Host, a.Port, a.User);
        }
    }

    [SkippableFact]
    public async Task A_PostgreSQL_string_aimed_at_a_SQL_Server_port_asks_to_check_the_kind()
    {
        Skip.IfNot(PortListens(1433), "No SQL Server listening on localhost:1433.");

        var response = await TestAsync(PgConnectionString.Build("localhost", 1433, "postgres", "u", "p", "disable", null));

        Assert.False(response.Ok);
        Assert.Equal("The server at localhost:1433 did not answer as a PostgreSQL server. Check the database kind.", response.ErrorMessage);
    }

    [SkippableFact]
    public async Task A_SQL_Server_string_still_works_and_reports_its_version()
    {
        const string cs = "Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";
        Skip.IfNot(CanOpenSqlServer(cs), "No SQL Server reachable with Windows authentication on localhost.");

        var response = await TestAsync(cs, DatabaseKind.SqlServer);

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.StartsWith("SQL Server ", response.ServerVersion);
    }

    private static bool PortListens(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("localhost", port).Wait(TimeSpan.FromSeconds(2)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanOpenSqlServer(string connectionString)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

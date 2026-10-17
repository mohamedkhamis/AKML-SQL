using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-100 to FR-103, contracts/ipc.md "Engine-side guard for bridge clients", task T041): a request that arrives over
/// the WebSocket bridge may make the engine open only a plain, explicit-credentials PostgreSQL connection. Nothing that signs
/// in as the engine's own identity, and nothing that makes the engine read a file the browser names, gets through. The named
/// pipe (the SSMS extension, running as the signed-in user) is not checked.
/// </summary>
public class PgTargetGuardTests
{
    private static string Good(string host = "db.example.com", string? password = "s3cret", string? rootCertificate = null, string? sslMode = null) =>
        PgConnectionString.Build(host, 5432, "northwind", "alice", password, sslMode, rootCertificate);

    private static string With(string connectionString, string keyValue) => connectionString + ";" + keyValue;

    private static PgTargetRefusal? Bridge(string connectionString) => PgTargetGuard.Check(connectionString, fromBridge: true);

    [Fact]
    public void Requests_that_do_not_come_over_the_bridge_are_not_checked()
    {
        Assert.Null(PgTargetGuard.Check(With(Good(), "Integrated Security=true"), fromBridge: false));
        Assert.Null(PgTargetGuard.Check("Host=h;Username=u;Kerberos Service Name=postgres", fromBridge: false));
    }

    [Fact]
    public void A_plain_connection_with_credentials_is_allowed_even_to_a_remote_host()
    {
        Assert.Null(Bridge(Good()));
        Assert.Null(Bridge(Good("localhost")));
        Assert.Null(Bridge(Good("10.0.0.5")));
        Assert.Null(Bridge(Good("[::1]")));
    }

    [Fact]
    public void A_connection_without_a_password_is_allowed_so_the_engine_can_supply_a_remembered_one()
    {
        Assert.Null(Bridge(Good(password: null)));
    }

    [Theory]
    [InlineData("Host=h;Database=d;Password=p")]                       // no Username
    [InlineData("Host=h;Database=d;Username=;Password=p")]             // empty Username
    public void A_missing_user_name_is_refused_with_the_credentials_code(string connectionString)
    {
        var refusal = Bridge(connectionString);

        Assert.NotNull(refusal);
        Assert.Equal(DialectErrors.CredentialsRequired, refusal!.Code);
    }

    [Theory]
    [InlineData("Integrated Security=true")]
    [InlineData("Integrated Security=True")]
    [InlineData("Kerberos Service Name=postgres")]
    [InlineData("Include Realm=true")]
    [InlineData("GSS Encryption Mode=Require")]
    public void Authentication_with_the_engines_own_identity_is_refused(string setting)
    {
        var refusal = Bridge(With(Good(), setting));

        Assert.NotNull(refusal);
        Assert.Equal(DialectErrors.CredentialsRequired, refusal!.Code);
    }

    [Theory]
    [InlineData("SSL Certificate=C:\\certs\\client.crt")]
    [InlineData("SSL Key=C:\\certs\\client.key")]
    [InlineData("SSL Password=x")]
    [InlineData("Passfile=C:\\Users\\svc\\pgpass.conf")]
    [InlineData("Options=-c search_path=evil")]
    public void Settings_that_make_the_engine_read_files_or_change_the_session_are_refused(string setting)
    {
        Assert.NotNull(Bridge(With(Good(), setting)));
    }

    [Fact]
    public void A_root_certificate_must_be_an_absolute_canonical_path()
    {
        Assert.Null(Bridge(Good(rootCertificate: @"C:\certs\root.crt", sslMode: "verify-full")));

        foreach (var path in new[] { @"root.crt", @"certs\root.crt", @"..\root.crt", @"C:\certs\..\..\root.crt", @"C:\certs\.\root.crt", @"\\attacker\share\root.crt" })
        {
            var refusal = Bridge(Good(rootCertificate: path, sslMode: "verify-full"));
            Assert.True(refusal != null, "expected a refusal for " + path);
            Assert.Equal(DialectErrors.CredentialsRequired, refusal!.Code);
        }
    }

    [Theory]
    [InlineData("/var/run/postgresql")]   // a Unix-domain socket directory
    [InlineData("\\\\server\\pipe")]      // a UNC path
    [InlineData("")]
    public void A_host_must_be_a_name_or_an_address(string host)
    {
        Assert.NotNull(Bridge("Host=" + host + ";Username=u;Password=p;Database=d"));
    }

    [Theory]
    [InlineData("Search Path=public")]
    [InlineData("Max Pool Size=1000")]
    [InlineData("Some Unknown Key=1")]
    public void Anything_the_web_edition_does_not_send_is_refused(string setting)
    {
        Assert.NotNull(Bridge(With(Good(), setting)));
    }

    [Fact]
    public void A_string_that_does_not_parse_is_refused_without_echoing_it()
    {
        var refusal = Bridge("Host=h;Password=hunter2;=;;;'");

        Assert.NotNull(refusal);
        Assert.DoesNotContain("hunter2", refusal!.Message);
    }

    [Fact]
    public void A_refusal_never_contains_the_password()
    {
        var refusal = Bridge(With(Good(password: "top-secret-pw"), "Integrated Security=true"));

        Assert.NotNull(refusal);
        Assert.DoesNotContain("top-secret-pw", refusal!.Message);
    }

    [Fact]
    public void The_bridge_check_is_not_a_SQL_Server_check()
    {
        // A SQL Server string handed to the PostgreSQL guard is not a PostgreSQL connection.
        Assert.NotNull(Bridge("Server=tcp:sql01;Database=master;User ID=sa;Password=x"));
    }
}

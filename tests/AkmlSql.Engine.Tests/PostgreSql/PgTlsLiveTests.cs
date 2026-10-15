using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-101, quickstart US1 step 1, section 0 item 1): TLS modes behave as PostgreSQL clients define them, and the verifying modes
/// reject an untrusted certificate or a host name the certificate does not name, with a message that says so. Every check goes through the
/// product's own path: <see cref="PgConnectionString.Build"/> spells the string, <see cref="TestSqlConnectionHandler"/> opens it and words the failure.
///
/// <para>
/// What the modes mean (libpq): <c>disable</c> never uses TLS; <c>prefer</c> uses it when the server offers it, without checking the certificate;
/// <c>require</c> insists on TLS and still does not check the certificate; <c>verify-ca</c> also requires a certificate that chains to the given root;
/// <c>verify-full</c> also requires the host name to be one the certificate names.
/// </para>
///
/// <para>
/// The server must have TLS on with a certificate for <c>localhost</c> and <c>127.0.0.1</c> (<c>scripts/pg-test-server.ps1</c> makes one, self-signed,
/// so the certificate is its own root). The certificate file is found through <c>AKML_PG_TEST_ROOT_CERT</c>, or in the script's default data folder.
/// Without a server, or without the file, the tests are skipped.
/// </para>
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgTlsLiveTests
{
    private readonly ITestOutputHelper _output;

    public PgTlsLiveTests(ITestOutputHelper output) => _output = output;

    private static string? ServerCertificatePath()
    {
        var configured = Environment.GetEnvironmentVariable("AKML_PG_TEST_ROOT_CERT");
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AKML SQL", "pg-test", "data", "server.crt");
        return File.Exists(fallback) ? fallback : null;
    }

    private (string Host, int Port, string User, string Password, string Root) Target()
    {
        PgTestServer.SkipUnlessAvailable();
        var root = ServerCertificatePath();
        Skip.If(root is null, "No server certificate: set AKML_PG_TEST_ROOT_CERT to the server's certificate file (scripts/pg-test-server.ps1 creates one).");

        var builder = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        using var connection = new NpgsqlConnection(PgTestServer.AdminConnectionString);
        connection.Open();
        using var ssl = new NpgsqlCommand("SHOW ssl", connection);
        Skip.If(!string.Equals((string?)ssl.ExecuteScalar(), "on", StringComparison.OrdinalIgnoreCase), "The test server has TLS off (ssl = on in postgresql.conf).");
        return ("localhost", builder.Port, builder.Username!, builder.Password!, root!);
    }

    private static string Spell(string host, int port, string user, string password, string mode, string? rootCertificate) =>
        PgConnectionString.Build(host, port, "postgres", user, password, mode, rootCertificate);

    private async Task<TestSqlConnectionResponse> TestAsync(string connectionString)
    {
        var response = await new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest { ConnectionString = connectionString, Kind = (int)AkmlSql.Core.DatabaseKind.PostgreSql }, default!, CancellationToken.None);
        _output.WriteLine($"{PgConnectionString.Describe(connectionString)} -> {(response.Ok ? "ok: " + response.ServerVersion : "refused: " + response.ErrorMessage)}");
        return response;
    }

    /// <summary>Whether the server says this session is encrypted (pg_stat_ssl); the answer of the server, not of the client.</summary>
    private static async Task<bool> UsesTlsAsync(string connectionString)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    // ── the three modes that do not check the certificate ────────────────────────────────────────────

    [SkippableFact]
    public async Task Disable_never_uses_TLS()
    {
        var t = Target();
        var connection = Spell(t.Host, t.Port, t.User, t.Password, "disable", null);

        Assert.True((await TestAsync(connection)).Ok);
        Assert.False(await UsesTlsAsync(connection));
    }

    [SkippableFact]
    public async Task Prefer_uses_TLS_when_the_server_offers_it_without_checking_the_certificate()
    {
        var t = Target();
        var connection = Spell(t.Host, t.Port, t.User, t.Password, "prefer", null);      // no root certificate: the certificate is self-signed and unknown to this machine

        Assert.True((await TestAsync(connection)).Ok);
        Assert.True(await UsesTlsAsync(connection));
    }

    [SkippableFact]
    public async Task An_empty_mode_is_prefer()
    {
        var t = Target();
        var connection = Spell(t.Host, t.Port, t.User, t.Password, string.Empty, null);

        Assert.True((await TestAsync(connection)).Ok);
        Assert.True(await UsesTlsAsync(connection));
    }

    [SkippableFact]
    public async Task Require_insists_on_TLS_and_does_not_check_the_certificate_as_libpq_does()
    {
        var t = Target();
        var connection = Spell(t.Host, t.Port, t.User, t.Password, "require", null);     // an untrusted, self-signed certificate must still be accepted

        var response = await TestAsync(connection);

        Assert.True(response.Ok, "libpq's sslmode=require encrypts without verifying the certificate, so a self-signed server must be accepted: " + response.ErrorMessage);
        Assert.True(await UsesTlsAsync(connection));
    }

    // ── the verifying modes ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Verify_ca_with_the_servers_root_certificate_connects_over_TLS()
    {
        var t = Target();
        var connection = Spell(t.Host, t.Port, t.User, t.Password, "verify-ca", t.Root);

        Assert.True((await TestAsync(connection)).Ok);
        Assert.True(await UsesTlsAsync(connection));
    }

    [SkippableFact]
    public async Task Verify_ca_without_a_root_certificate_rejects_an_untrusted_certificate_and_says_what_to_do()
    {
        var t = Target();

        var response = await TestAsync(Spell(t.Host, t.Port, t.User, t.Password, "verify-ca", null));

        Assert.False(response.Ok);
        AssertRefused(response, t.Host, t.Port, "is not signed by a root this machine trusts", "TLS mode require");
    }

    [SkippableFact]
    public async Task Verify_ca_with_another_authoritys_certificate_rejects_the_servers_certificate()
    {
        var t = Target();
        using var other = TemporaryRoot();

        var response = await TestAsync(Spell(t.Host, t.Port, t.User, t.Password, "verify-ca", other.Path));

        Assert.False(response.Ok);
        AssertRefused(response, t.Host, t.Port, "does not chain to the root certificate you gave", "Give the root certificate that signed it");
    }

    [SkippableFact]
    public async Task Verify_full_connects_when_the_host_is_a_name_the_certificate_has()
    {
        var t = Target();

        foreach (var host in new[] { "localhost", "127.0.0.1" })
        {
            var connection = Spell(host, t.Port, t.User, t.Password, "verify-full", t.Root);
            Assert.True((await TestAsync(connection)).Ok, host);
            Assert.True(await UsesTlsAsync(connection), host);
        }
    }

    [SkippableFact]
    public async Task Verify_full_rejects_a_host_name_the_certificate_does_not_name()
    {
        var t = Target();

        // The certificate names localhost and 127.0.0.1; the IPv6 loopback is the same machine under a name it does not carry.
        var connection = Spell("::1", t.Port, t.User, t.Password, "verify-full", t.Root);
        var response = await TestAsync(connection);

        Skip.If(!response.Ok && response.ErrorMessage!.Contains("Could not reach", StringComparison.Ordinal), "The test server does not listen on ::1.");
        Assert.False(response.Ok, "verify-full must refuse a host the certificate does not name");
        AssertRefused(response, "::1", t.Port, "is not valid for the host name \"::1\"", "localhost", "127.0.0.1", "verify-ca");

        // The same host passes verify-ca, which checks the chain and not the name.
        Assert.True((await TestAsync(Spell("::1", t.Port, t.User, t.Password, "verify-ca", t.Root))).Ok);
    }

    // ── the wording and what it must not contain ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_refusal_names_the_server_the_reason_and_the_remedy_and_never_the_password()
    {
        var t = Target();

        var response = await TestAsync(Spell(t.Host, t.Port, t.User, t.Password, "verify-full", null));

        Assert.False(response.Ok);
        Assert.DoesNotContain(t.Password, response.ErrorMessage);
        Assert.DoesNotContain("Password=", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        AssertRefused(response, t.Host, t.Port, "is not signed by a root this machine trusts");
    }

    // The refusal starts with where it happened, then says which thing was wrong and what to do (FR-101), in one clean sentence each.
    private static void AssertRefused(TestSqlConnectionResponse response, string host, int port, params string[] expected)
    {
        var message = response.ErrorMessage ?? string.Empty;
        Assert.StartsWith($"The TLS connection to {host}:{port} failed: ", message);
        foreach (var part in expected) Assert.Contains(part, message);
        Assert.DoesNotContain("RemoteCertificateValidationCallback", message);        // the driver's one sentence for every refusal
        Assert.DoesNotContain("..", message);
    }

    // A self-signed certificate from nobody this server knows, written where the engine can read it as a root.
    private static TemporaryFile TemporaryRoot()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=some other authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var file = new TemporaryFile(".crt");
        File.WriteAllText(file.Path, certificate.ExportCertificatePem());
        return file;
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string extension) => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "akml-tls-" + Guid.NewGuid().ToString("N") + extension);

        public string Path { get; }

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* a leftover temporary certificate is harmless */ }
        }
    }
}

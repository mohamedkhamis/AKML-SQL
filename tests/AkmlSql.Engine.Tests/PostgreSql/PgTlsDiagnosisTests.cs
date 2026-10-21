using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AkmlSql.Core.Connections;
using AkmlSql.Engine.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-101). The driver refuses an unacceptable server certificate with one sentence whatever is wrong with it; the engine looks at the handshake
/// once more and says which thing it was. No PostgreSQL server is needed: a small endpoint answers the SSLRequest and completes a TLS handshake with
/// certificates made here - from a private authority, from another one, through an intermediate, expired, not yet valid, for other host names - and the
/// diagnosis is checked against each, together with every case in which it must say nothing and leave the driver's sentence alone, and with what the
/// endpoint saw on the wire (the SSLRequest and nothing after the handshake).
///
/// <para>
/// In the live collection because some cases set the environment variables the driver reads for its root certificate, which would otherwise reach
/// tests running at the same time.
/// </para>
/// </summary>
[Collection("PostgreSqlLive")]
public sealed class PgTlsDiagnosisTests : IDisposable
{
    private readonly PgTlsFixtures _fixtures = new();

    public void Dispose() => _fixtures.Dispose();

    private PgTlsFixtures.Authority Root(string name = "Test Root") => _fixtures.NewAuthority(name);

    private X509Certificate2 Leaf(PgTlsFixtures.Authority issuer, string[]? dns = null, string[]? addresses = null, int notBeforeDays = -1, int notAfterDays = 30) =>
        _fixtures.NewServerCertificate(issuer, dns ?? new[] { "localhost" }, addresses ?? new[] { "127.0.0.1" }, notBeforeDays, notAfterDays);

    private static string Spell(int port, string mode, string? root, string host = "127.0.0.1") =>
        PgConnectionString.Build(host, port, "postgres", "postgres", "never-sent", mode, root);

    private static string Day(DateTime when) => when.ToString("yyyy-MM-dd");

    // ── what it says about the server's own certificate ──────────────────────────────────────────────

    [Fact]
    public async Task A_certificate_from_an_authority_the_machine_does_not_know_is_called_untrusted_and_the_remedy_is_given()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", null), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("is not signed by a root this machine trusts", reason);
        Assert.Contains("for \"localhost\"", reason);
        Assert.Contains($"issued by \"{authority.Name}\"", reason);
        Assert.Contains("TLS mode require", reason);
    }

    [Fact]
    public async Task An_expired_certificate_says_when_it_expired()
    {
        var authority = Root();
        var certificate = Leaf(authority, notBeforeDays: -20, notAfterDays: -2);
        var server = _fixtures.Serve(certificate);

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"The server's certificate (for \"localhost\") is not valid now: it expired on {Day(certificate.NotAfter)}", reason);
        Assert.Contains("Ask the server's administrator for a current certificate", reason);
    }

    [Fact]
    public async Task A_certificate_that_is_not_valid_yet_says_from_when()
    {
        var authority = Root();
        var certificate = Leaf(authority, notBeforeDays: 3, notAfterDays: 30);
        var server = _fixtures.Serve(certificate);

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", authority.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"it is not valid until {Day(certificate.NotBefore)}", reason);
    }

    [Fact]
    public async Task An_expired_certificate_is_reported_even_when_the_authority_is_also_unknown()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, notBeforeDays: -20, notAfterDays: -2));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", null), CancellationToken.None);

        Assert.Contains("is not valid now: it expired on", reason);        // the dates come first: a renewed certificate would still be untrusted, but that is the next thing to fix
    }

    // ── an authority outside its dates is not the server certificate's fault ─────────────────────────

    [Fact]
    public async Task A_root_certificate_given_that_has_expired_says_so_and_asks_for_a_current_one_not_for_a_new_server_certificate()
    {
        var oldRoot = _fixtures.NewAuthority("Old Root", notBeforeDays: -60, notAfterDays: -5);
        var server = _fixtures.Serve(Leaf(oldRoot));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", oldRoot.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"The root certificate you gave ({Path.GetFileName(oldRoot.PemPath)}, for \"{oldRoot.Name}\") is not valid now: it expired on {Day(oldRoot.Certificate.NotAfter)}", reason);
        Assert.Contains("Give a current root certificate", reason);
        Assert.DoesNotContain("administrator for a current certificate", reason);
    }

    [Fact]
    public async Task An_expired_intermediate_is_named_with_its_own_date()
    {
        var root = Root();
        var intermediate = _fixtures.NewIntermediate(root, "Old Intermediate", notBeforeDays: -60, notAfterDays: -5);
        var server = _fixtures.Serve(Leaf(intermediate), intermediate.Certificate);

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", root.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"The certificate \"{intermediate.Name}\" in the server's chain is not valid now: it expired on {Day(intermediate.Certificate.NotAfter)}", reason);
        Assert.Contains("a current certificate chain", reason);
        Assert.DoesNotContain("The server's certificate (for", reason);
    }

    [Fact]
    public async Task An_intermediate_that_is_not_valid_yet_says_from_when()
    {
        var root = Root();
        var intermediate = _fixtures.NewIntermediate(root, "Future Intermediate", notBeforeDays: 10, notAfterDays: 400);
        var server = _fixtures.Serve(Leaf(intermediate), intermediate.Certificate);

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", root.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"The certificate \"{intermediate.Name}\" in the server's chain is not valid now: it is not valid until {Day(intermediate.Certificate.NotBefore)}", reason);
    }

    // ── chains: the server sends its intermediate, the person gives the root ─────────────────────────

    [Fact]
    public async Task A_chain_through_an_intermediate_that_leads_to_the_root_given_is_acceptable_and_says_nothing()
    {
        var root = Root();
        var intermediate = _fixtures.NewIntermediate(root);
        var server = _fixtures.Serve(Leaf(intermediate), intermediate.Certificate);

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", root.PemPath), CancellationToken.None));
    }

    [Fact]
    public async Task A_certificate_that_does_not_chain_to_the_root_given_says_so_and_names_the_file()
    {
        var served = Root("Served Authority");
        var other = Root("Other Authority");
        var server = _fixtures.Serve(Leaf(served), served.Certificate);          // the server sends its authority, which is not the one given

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", other.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("does not chain to the root certificate you gave", reason);
        Assert.Contains(Path.GetFileName(other.PemPath), reason);
        Assert.DoesNotContain(Path.GetDirectoryName(other.PemPath)!, reason);       // the file name, not the path
    }

    [Fact]
    public async Task When_the_authority_is_in_nobodys_hands_the_sentence_offers_both_the_right_root_and_a_server_that_sends_the_intermediate()
    {
        var served = Root("Served Authority");
        var other = Root("Other Authority");
        var server = _fixtures.Serve(Leaf(served));                              // only the leaf: the authority is neither sent nor in the file given

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", other.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"chains up to \"{served.Name}\", which the root certificate you gave", reason);
        Assert.Contains("does not hold", reason);
        Assert.Contains($"Give the root certificate file that holds \"{served.Name}\"", reason);
        Assert.Contains("if that is an intermediate authority, ask the server's administrator to send it", reason);
    }

    [Fact]
    public async Task A_server_that_leaves_out_its_intermediate_is_told_apart_from_a_root_that_is_wrong()
    {
        var root = Root();
        var intermediate = _fixtures.NewIntermediate(root, "Issuing Authority");
        var server = _fixtures.Serve(Leaf(intermediate));                        // the right root is given, but the intermediate between it and the leaf is missing

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", root.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains($"chains up to \"{intermediate.Name}\"", reason);
        Assert.Contains("ask the server's administrator to send it with the certificate", reason);
        Assert.DoesNotContain("does not chain to the root certificate you gave", reason);
    }

    // ── names ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_host_name_the_certificate_does_not_name_lists_the_names_it_has_and_offers_verify_ca()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com", "reports.example.com" }, new[] { "10.1.2.3" }));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("is not valid for the host name \"127.0.0.1\"", reason);
        Assert.Contains("it names db.example.com, reports.example.com, 10.1.2.3", reason);
        Assert.Contains("verify-ca", reason);
    }

    [Fact]
    public async Task A_certificate_with_no_alternative_names_is_described_by_its_subject()
    {
        var authority = Root();
        var server = _fixtures.Serve(_fixtures.NewServerCertificate(authority, new[] { "db.example.com" }, withoutNames: true));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("is not valid for the host name \"127.0.0.1\"; it names \"db.example.com\"", reason);
    }

    [Fact]
    public async Task A_certificate_whose_name_list_cannot_be_read_is_still_described_and_nothing_is_thrown()
    {
        var authority = Root();
        var server = _fixtures.Serve(_fixtures.NewServerCertificate(authority, new[] { "db.example.com" }, rawNames: new byte[] { 0x30, 0x03, 0x01, 0x01, 0xFF }));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None);

        // Whatever the platform makes of the broken extension, the diagnosis returns a sentence or nothing; it never throws.
        Assert.True(reason is null || reason.Length > 0);
    }

    [Fact]
    public async Task An_untrusted_chain_is_reported_before_the_name()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));        // wrong name AND no root given

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", null), CancellationToken.None);

        Assert.Contains("is not signed by a root this machine trusts", reason);
    }

    [Fact]
    public async Task Verify_ca_does_not_check_the_name_so_a_wrong_name_alone_is_not_a_reason()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", authority.PemPath), CancellationToken.None));
    }

    [Fact]
    public async Task A_certificate_that_is_acceptable_leaves_the_refusal_to_whatever_else_caused_it()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority));

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None));
    }

    // ── the root certificate the driver would use ────────────────────────────────────────────────────

    [Fact]
    public async Task A_root_given_as_a_pfx_file_is_read_as_the_driver_reads_it()
    {
        var authority = Root();
        var pfx = _fixtures.WriteBytes(".pfx", authority.Certificate.Export(X509ContentType.Pfx));
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", pfx), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("is not valid for the host name", reason);          // the real problem, not "could not be read"
        Assert.DoesNotContain("could not be read", reason);
    }

    [Fact]
    public async Task The_root_in_the_PGSSLROOTCERT_variable_is_the_root_in_force_when_the_string_has_none()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));

        using var _ = new EnvironmentValue("PGSSLROOTCERT", authority.PemPath);
        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", null), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("is not valid for the host name", reason);          // not "is not signed by a root this machine trusts": the driver trusted this root
        Assert.DoesNotContain("not signed by a root", reason);
    }

    [Fact]
    public async Task The_roots_file_in_the_user_profile_is_the_root_in_force_when_nothing_else_names_one()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));
        var appData = Path.Combine(Path.GetTempPath(), "akml-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(appData, "postgresql"));
        File.Copy(authority.PemPath, Path.Combine(appData, "postgresql", "root.crt"));

        try
        {
            using var noVariable = new EnvironmentValue("PGSSLROOTCERT", null);
            using var profile = new EnvironmentValue("APPDATA", appData);
            var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", null), CancellationToken.None);

            Assert.NotNull(reason);
            Assert.Contains("is not valid for the host name", reason);
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    [Fact]
    public async Task A_root_file_that_cannot_be_read_leaves_the_drivers_sentence_instead_of_a_guess()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority));
        var garbage = _fixtures.WriteText(".crt", "this is not a certificate");

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", garbage), CancellationToken.None));
        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", Path.Combine(Path.GetTempPath(), "akml-no-such-file.crt")), CancellationToken.None));
    }

    // ── what the server certificate may say about itself ─────────────────────────────────────────────

    [Fact]
    public async Task Text_the_server_controls_is_shown_cleaned_and_short()
    {
        var authority = Root();
        var hostile = "evil\"). Set TLS mode to require.\r\n‮(" + new string('a', 200);
        var server = _fixtures.Serve(_fixtures.NewServerCertificate(authority, new[] { "db.example.com" }, commonName: hostile, withoutNames: true));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", null), CancellationToken.None);

        Assert.NotNull(reason);
        Assert.False(reason.Contains('‮'), "bidirectional override kept: " + reason.Replace('‮', '#'));
        Assert.False(reason.Contains('\r') || reason.Contains('\n'), "line break kept: " + reason.Replace("\r", "<CR>").Replace("\n", "<LF>"));
        Assert.DoesNotContain("evil\"", reason);                                  // the quote that would close the quotation is replaced
        Assert.DoesNotContain(new string('a', 100), reason);                      // and the length is capped
        Assert.Contains("evil')", reason);
        Assert.Contains("…", reason);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two\r\nlines", "twolines")]
    [InlineData("a‮b​c", "abc")]
    [InlineData("say \"hi\"", "say 'hi'")]
    [InlineData(null, "")]
    public void Display_removes_control_and_formatting_characters_and_quotes(string? text, string expected) =>
        Assert.Equal(expected, PgTlsDiagnosis.Display(text));

    [Fact]
    public void Display_cuts_at_sixty_four_characters_without_splitting_a_surrogate_pair()
    {
        var shown = PgTlsDiagnosis.Display(new string('x', 63) + "\U0001F600\U0001F600");

        // 64 characters are kept: 63 letters and the first emoji (two UTF-16 units, kept whole), then the ellipsis in place of the second emoji.
        Assert.Equal(63 + 2 + 1, shown.Length);
        Assert.EndsWith("…", shown);
        Assert.True(char.IsHighSurrogate(shown[shown.Length - 3]) && char.IsLowSurrogate(shown[shown.Length - 2]));
    }

    // ── when it must say nothing, so that the driver's own sentence stands, and not even connect ─────

    [Theory]
    [InlineData("disable")]
    [InlineData("prefer")]
    [InlineData("require")]
    public async Task The_modes_that_do_not_check_the_certificate_are_not_diagnosed_and_the_server_is_not_contacted(string mode)
    {
        var server = _fixtures.Serve(Leaf(Root()));                             // an untrusted certificate: a probe would have something to say

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, mode, null), CancellationToken.None));
        Assert.Equal(0, server.Accepted);
    }

    [Fact]
    public async Task A_server_that_does_not_offer_TLS_is_asked_once_and_not_diagnosed()
    {
        var server = _fixtures.Serve(null);

        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", null), CancellationToken.None));
        var session = await server.NextSessionAsync();
        Assert.Equal(PgTlsDiagnosis.SslRequest, session.Request);
        Assert.False(session.Handshook);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_not_diagnosed()
    {
        Assert.Null(await PgTlsDiagnosis.ExplainAsync(Spell(PgTlsFixtures.ClosedPort(), "verify-full", null), CancellationToken.None));
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on_at_the_time_limit()
    {
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            var watch = Stopwatch.StartNew();
            var reason = await PgTlsDiagnosis.ExplainAsync(Spell(((IPEndPoint)silent.LocalEndpoint).Port, "verify-full", null), TimeSpan.FromMilliseconds(400), CancellationToken.None);

            Assert.Null(reason);
            Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(4));
        }
        finally
        {
            silent.Stop();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/var/run/postgresql")]
    public async Task A_host_with_no_certificate_to_look_at_is_not_diagnosed(string host)
    {
        Assert.Null(await PgTlsDiagnosis.ExplainAsync($"Host={host};Port=5432;SSL Mode=VerifyFull", CancellationToken.None));
    }

    [Fact]
    public async Task A_string_the_driver_cannot_read_is_not_diagnosed()
    {
        Assert.Null(await PgTlsDiagnosis.ExplainAsync("this is not a connection string", CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_request_is_cancelled_not_swallowed()
    {
        var server = _fixtures.Serve(Leaf(Root()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-ca", null), cancelled.Token));
    }

    // ── what goes over the wire ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_diagnosis_sends_the_SSLRequest_and_nothing_after_the_handshake_so_no_password_leaves()
    {
        var authority = Root();
        var server = _fixtures.Serve(Leaf(authority, new[] { "db.example.com" }, Array.Empty<string>()));

        var reason = await PgTlsDiagnosis.ExplainAsync(Spell(server.Port, "verify-full", authority.PemPath), CancellationToken.None);
        var session = await server.NextSessionAsync();

        Assert.NotNull(reason);
        Assert.DoesNotContain("never-sent", reason);
        Assert.Equal(1, server.Accepted);
        Assert.Equal(PgTlsDiagnosis.SslRequest, session.Request);
        Assert.True(session.Handshook);
        Assert.Equal(0, session.BytesAfterHandshake);                          // no startup message, no password message
    }

    // ── the wording around it, with the exception the driver really throws ───────────────────────────

    private static async Task<NpgsqlException> RefusedAsync(string connectionString)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(connectionString);
        return await Assert.ThrowsAnyAsync<NpgsqlException>(() => connection.OpenAsync());
    }

    [Fact]
    public async Task The_driver_refuses_an_untrusted_certificate_as_a_TLS_error_and_the_refusal_puts_the_specific_reason_after_where_it_happened()
    {
        var server = _fixtures.Serve(Leaf(Root()));
        var connection = Spell(server.Port, "verify-ca", null);

        var refused = await RefusedAsync(connection);
        Assert.IsAssignableFrom<AuthenticationException>(refused.InnerException);        // the shape DescribeAsync keys on: pinned against the real driver

        var failure = await PgConnectionFailure.DescribeAsync(refused, connection, "127.0.0.1", server.Port, CancellationToken.None);

        Assert.StartsWith($"The TLS connection to 127.0.0.1:{server.Port} failed: The server's certificate", failure.Message);
        Assert.Contains("is not signed by a root this machine trusts", failure.Message);
        Assert.Null(failure.ErrorCode);
        Assert.DoesNotContain("RemoteCertificateValidationCallback", failure.Message);
        Assert.DoesNotContain("..", failure.Message);
        Assert.DoesNotContain("never-sent", failure.Message);
    }

    [Fact]
    public async Task When_the_handshake_cannot_be_repeated_the_drivers_sentence_stands_without_a_doubled_full_stop()
    {
        var port = PgTlsFixtures.ClosedPort();
        var driver = new NpgsqlException("handshake", new AuthenticationException("The remote certificate was rejected by the provided RemoteCertificateValidationCallback."));

        var failure = await PgConnectionFailure.DescribeAsync(driver, Spell(port, "verify-full", null), "127.0.0.1", port, CancellationToken.None);

        Assert.Equal(PgConnectionFailure.Describe(driver, "127.0.0.1", port).Message, failure.Message);
        Assert.Contains("rejected by the provided RemoteCertificateValidationCallback. Use TLS mode require", failure.Message);
        Assert.DoesNotContain("..", failure.Message);
    }

    [Fact]
    public async Task A_failure_that_is_not_about_a_certificate_is_worded_as_before_and_nothing_is_probed()
    {
        var server = _fixtures.Serve(Leaf(Root()));
        var timeout = new NpgsqlException("connect", new TimeoutException());

        var failure = await PgConnectionFailure.DescribeAsync(timeout, Spell(server.Port, "verify-full", null), "127.0.0.1", server.Port, CancellationToken.None);

        Assert.Equal(PgConnectionFailure.Describe(timeout, "127.0.0.1", server.Port), failure);
        Assert.Equal(0, server.Accepted);
    }

    [Fact]
    public async Task A_diagnosis_that_fails_unexpectedly_leaves_the_drivers_sentence_and_throws_nothing()
    {
        var driver = new NpgsqlException("handshake", new AuthenticationException("The remote certificate was rejected."));

        var failure = await PgConnectionFailure.DescribeAsync(driver, "Host=x", "x", 5432,
            (_, _) => throw new InvalidOperationException("the diagnosis broke"), CancellationToken.None);

        Assert.Equal(PgConnectionFailure.Describe(driver, "x", 5432), failure);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_not_hidden_by_the_diagnosis_wording()
    {
        var driver = new NpgsqlException("handshake", new AuthenticationException("The remote certificate was rejected."));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PgConnectionFailure.DescribeAsync(driver, "Host=x", "x", 5432,
            (_, ct) => Task.FromCanceled<string?>(ct), cancelled.Token));
    }

    // ── a root certificate file that cannot be read never reaches the diagnosis; the refusal says which file and why ──

    public static IEnumerable<object[]> UnreadableRoots => new[]
    {
        new object[] { "missing", "was not found. Check the path." },
        new object[] { "garbage", "could not be read as a certificate" },
        new object[] { "empty", "could not be read as a certificate" },
    };

    [Theory]
    [MemberData(nameof(UnreadableRoots))]
    public async Task A_root_certificate_file_that_cannot_be_read_is_named_in_the_refusal(string kind, string expected)
    {
        var server = _fixtures.Serve(Leaf(Root()));
        var path = kind switch
        {
            "missing" => Path.Combine(Path.GetTempPath(), "akml-no-such-root-" + Guid.NewGuid().ToString("N") + ".crt"),
            "garbage" => _fixtures.WriteText(".crt", "this is not a certificate"),
            _ => _fixtures.WriteText(".crt", string.Empty),
        };
        var connection = Spell(server.Port, "verify-ca", path);

        var refused = await RefusedAsync(connection);
        Assert.IsNotType<AuthenticationException>(refused.InnerException);                // the driver fails inside the handshake, not as a refused certificate
        var connectionsByTheDriver = server.Accepted;

        var failure = await PgConnectionFailure.DescribeAsync(refused, connection, "127.0.0.1", server.Port, CancellationToken.None);

        Assert.StartsWith($"The TLS connection to 127.0.0.1:{server.Port} failed: The root certificate file {Path.GetFileName(path)} ", failure.Message);
        Assert.Contains(expected, failure.Message);
        Assert.DoesNotContain(Path.GetDirectoryName(path)!, failure.Message);
        Assert.Equal(connectionsByTheDriver, server.Accepted);                             // wording a file problem does not connect to the server again
    }

    private sealed class EnvironmentValue : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentValue(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}

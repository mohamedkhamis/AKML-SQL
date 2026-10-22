using System.Net;
using System.Security.Cryptography;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.PostgreSql;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (reported 2026-10-22): the web edition's remembered SQL Server password. It is filled in only for a
/// bridge request on a plain SQL login that arrives without one, saved only on request and only for a bridge request, kept
/// apart from the logins the SSMS extension saves in the same store, and asked about or forgotten through CredentialRequest.
/// The review bound it to the certificate checking it was given with, and made a store failure an answer instead of a throw.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
public class SqlServerCredentialResolverTests : IDisposable
{
    // A server unique to this class, so the process-wide store never mixes with another test.
    private readonly string _server = "sqlcred-" + Guid.NewGuid().ToString("N")[..8] + ".test";

    public void Dispose()
    {
        // The seams first: a test that left the removal failing must not make the clean-up throw.
        SqlServerCredentialResolver.SaveToStore = SqlCredentialStore.Save;
        SqlServerCredentialResolver.RemoveFromStore = SqlCredentialStore.Remove;
        SqlServerCredentialResolver.Forget(_server, "alice");
        SqlCredentialStore.Remove(_server, "alice");
    }

    private string Cs(string? password, string user = "alice", bool windows = false, bool trust = false, string? extra = null)
    {
        var b = new SqlConnectionStringBuilder { DataSource = _server, InitialCatalog = "Northwind", Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = trust };
        if (windows) b.IntegratedSecurity = true;
        else { b.UserID = user; b.Password = password ?? string.Empty; }
        return extra is null ? b.ConnectionString : b.ConnectionString + ";" + extra;
    }

    private static string PasswordOf(string cs) => new SqlConnectionStringBuilder(cs).Password;

    private static IDisposable FromBridge() => BridgeSourceIp.Set(IPAddress.Loopback);

    private string Resolve(string cs) => SqlServerCredentialResolver.Resolve(cs, fromBridge: true);

    private bool Remember(string cs) => SqlServerCredentialResolver.RememberIfRequested(cs, remember: true, fromBridge: true);

    [Fact]
    public void A_bridge_request_without_a_password_gets_the_remembered_one()
    {
        using (FromBridge()) Assert.True(SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true));

        Assert.Equal("stored-pw", PasswordOf(Resolve(Cs(null))));
    }

    [Fact]
    public void A_typed_password_windows_authentication_and_other_logins_are_left_alone()
    {
        Remember(Cs("stored-pw"));

        Assert.Equal("typed-pw", PasswordOf(Resolve(Cs("typed-pw"))));
        Assert.Equal(Cs(null, windows: true), Resolve(Cs(null, windows: true)));
        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, user: "bob"))));
    }

    [Fact]
    public void A_named_pipe_request_never_gets_a_web_remembered_password()
    {
        Remember(Cs("stored-pw"));

        Assert.Equal(string.Empty, PasswordOf(SqlServerCredentialResolver.Resolve(Cs(null), fromBridge: false)));
    }

    [Fact]
    public void An_ssms_saved_login_is_never_used_for_a_bridge_request()
    {
        SqlCredentialStore.Save(_server, "alice", "ssms-pw");   // what the SSMS extension stores (spec 029)

        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null))));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void Nothing_is_saved_without_the_request_or_outside_the_bridge()
    {
        Assert.False(SqlServerCredentialResolver.RememberIfRequested(Cs("pw"), remember: false, fromBridge: true));
        Assert.False(SqlServerCredentialResolver.RememberIfRequested(Cs("pw"), remember: true));   // the named pipe

        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void The_server_is_matched_case_insensitively_and_forget_removes_it()
    {
        Remember(Cs("stored-pw"));

        Assert.True(SqlServerCredentialResolver.Has(_server.ToUpperInvariant(), "alice"));
        Assert.True(SqlServerCredentialResolver.Forget(_server, "alice"));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
        Assert.False(SqlServerCredentialResolver.Forget(_server, "alice"));
    }

    // ---- review: only a plain SQL login, never a request that could send the password elsewhere ---------------------------

    [Theory]
    [InlineData("Encrypt=Optional")]
    [InlineData("Encrypt=false")]
    [InlineData("HostNameInCertificate=evil.example")]
    [InlineData("Server Certificate=C:\\certs\\evil.cer")]
    [InlineData("Failover Partner=evil.example")]
    [InlineData("Server SPN=MSSQLSvc/evil.example")]
    [InlineData("Authentication=ActiveDirectoryPassword")]
    public void A_request_that_is_not_a_plain_encrypted_SQL_login_gets_nothing_and_saves_nothing(string option)
    {
        _ = new SqlConnectionStringBuilder(Cs(null, extra: option));   // SqlClient knows the keyword: the refusal is ours, not a parse error
        Remember(Cs("stored-pw"));

        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, extra: option))));
        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, trust: true, extra: option))));

        SqlServerCredentialResolver.Forget(_server, "alice");
        Assert.False(Remember(Cs("typed-pw", extra: option)));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    // ---- review: the password is bound to the certificate checking it was given with ---------------------------------------

    [Fact]
    public void A_password_given_to_a_validated_server_never_goes_to_a_request_that_trusts_any_certificate()
    {
        Remember(Cs("validated-pw", trust: false));

        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, trust: true))));
        Assert.Equal("validated-pw", PasswordOf(Resolve(Cs(null, trust: false))));
    }

    [Fact]
    public void A_password_already_sent_without_validation_may_go_to_a_validated_request()
    {
        Remember(Cs("trusted-pw", trust: true));

        Assert.Equal("trusted-pw", PasswordOf(Resolve(Cs(null, trust: true))));
        Assert.Equal("trusted-pw", PasswordOf(Resolve(Cs(null, trust: false))));
    }

    [Fact]
    public void Saving_under_the_other_certificate_checking_leaves_one_entry_and_has_and_forget_see_both()
    {
        Remember(Cs("first-pw", trust: false));
        Remember(Cs("second-pw", trust: true));

        Assert.False(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server), "alice"));
        Assert.True(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true), "alice"));
        Assert.Equal("second-pw", PasswordOf(Resolve(Cs(null, trust: false))));
        Assert.True(SqlServerCredentialResolver.Has(_server, "alice"));

        Remember(Cs("third-pw", trust: false));
        Assert.True(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server), "alice"));
        Assert.False(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true), "alice"));
        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, trust: true))));

        // Has and Forget answer for either key: an entry written by an earlier build, and one with each setting at once.
        SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true), "alice", "x");
        Assert.True(SqlServerCredentialResolver.Forget(_server, "alice"));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, trust: false))));
    }

    [Fact]
    public void The_validated_key_is_the_one_earlier_builds_wrote()
    {
        Assert.Equal("mssql-web://" + _server, SqlServerCredentialResolver.ServerKey(_server.ToUpperInvariant()));
        Assert.Equal("mssql-web-trust://" + _server, SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true));
    }

    // ---- review round 2: the released builds wrote every web password under mssql-web:// -------------------------------------

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData(".")]
    [InlineData("localhost\\SQLEXPRESS")]
    [InlineData("tcp:127.0.0.1,1433")]
    public void A_trusting_request_for_the_engines_own_machine_reads_the_entry_a_released_build_wrote(string loopback)
    {
        // A login unique to this case: the server is not the class's, so Dispose would not remove it.
        var login = "legacy-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey(loopback), login, "legacy-pw");   // as 1.26.1022.0927/1042 did

            var request = new SqlConnectionStringBuilder
            {
                DataSource = loopback, UserID = login, Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            }.ConnectionString;
            Assert.Equal("legacy-pw", PasswordOf(Resolve(request)));

            // Read, never moved: nothing is written on a read.
            Assert.True(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(loopback), login));
            Assert.False(SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(loopback, trustServerCertificate: true), login));
        }
        finally
        {
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey(loopback), login);
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey(loopback, trustServerCertificate: true), login);
        }
    }

    [Fact]
    public void A_trusting_request_for_the_engines_own_machine_prefers_its_own_key()
    {
        var login = "legacy-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey("localhost"), login, "legacy-pw");
            SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey("localhost", trustServerCertificate: true), login, "trust-pw");

            var request = new SqlConnectionStringBuilder
            {
                DataSource = "localhost", UserID = login, Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            }.ConnectionString;
            Assert.Equal("trust-pw", PasswordOf(Resolve(request)));
        }
        finally
        {
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey("localhost"), login);
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey("localhost", trustServerCertificate: true), login);
        }
    }

    [Fact]
    public void A_trusting_request_for_a_remote_server_never_reads_the_entry_a_released_build_wrote()
    {
        SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey(_server), "alice", "legacy-pw");   // _server is remote

        Assert.Equal(string.Empty, PasswordOf(Resolve(Cs(null, trust: true))));
        Assert.Equal("legacy-pw", PasswordOf(Resolve(Cs(null, trust: false))));
    }

    // ---- review: a store that cannot be written is an answer, never a throw -------------------------------------------------

    [Fact]
    public void A_store_that_cannot_be_written_reports_false_and_keeps_nothing()
    {
        SqlServerCredentialResolver.SaveToStore = (_, _, _, _) => false;

        Assert.False(Remember(Cs("pw")));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void An_encryption_failure_is_caught_and_reported_false()
    {
        SqlServerCredentialResolver.SaveToStore = (_, _, _, _) => throw new CryptographicException("DPAPI is not available");

        Assert.False(Remember(Cs("pw")));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void A_failed_save_keeps_the_password_saved_under_the_other_setting()
    {
        Remember(Cs("old-pw", trust: true));
        SqlServerCredentialResolver.SaveToStore = (_, _, _, _) => false;

        Assert.False(Remember(Cs("new-pw", trust: false)));
        Assert.Equal("old-pw", PasswordOf(Resolve(Cs(null, trust: true))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Remembering_saves_its_key_and_removes_the_other_in_one_store_write(bool trust)
    {
        // Review round 2: two separate calls let two remembers with different settings each delete the other's fresh entry.
        var calls = new List<(string Key, string Login, string[] Removed)>();
        SqlServerCredentialResolver.SaveToStore = (key, login, _, removed) => { calls.Add((key, login, removed)); return true; };

        Assert.True(Remember(Cs("pw", trust: trust)));

        var call = Assert.Single(calls);
        Assert.Equal(SqlServerCredentialResolver.ServerKey(_server, trust), call.Key);
        Assert.Equal("alice", call.Login);
        Assert.Equal(new[] { SqlServerCredentialResolver.ServerKey(_server, !trust) }, call.Removed);
    }

    [Fact]
    public void Concurrent_remembers_with_different_settings_leave_exactly_one_password()
    {
        // Each save writes its key and removes the other in one write under the store's lock, so whichever ran last is the
        // only entry — never both, never neither (Dispose forgets either key).
        Parallel.For(0, 20, i => Assert.True(Remember(Cs("pw" + i, trust: i % 2 == 0))));

        var validated = SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server), "alice");
        var trusted = SqlCredentialStore.Has(SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true), "alice");
        Assert.True(validated ^ trusted);
    }

    // ---- review round 2: forget is truthful about the store ---------------------------------------------------------------------

    [Fact]
    public void A_forget_the_store_cannot_write_throws_and_the_password_is_still_remembered()
    {
        Remember(Cs("stored-pw"));
        SqlServerCredentialResolver.RemoveFromStore = (_, _) => false;

        Assert.Throws<CredentialStoreWriteException>(() => SqlServerCredentialResolver.Forget(_server, "alice"));
        Assert.True(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public async Task CredentialRequest_forget_that_cannot_be_written_is_answered_not_ok_and_never_forgotten()
    {
        Remember(Cs("stored-pw"));
        SqlServerCredentialResolver.RemoveFromStore = (_, _) => false;
        var handler = new CredentialHandler(new PgCredentialResolver());

        var reply = await handler.HandleAsync(
            new CredentialRequest { Action = CredentialAction.Forget, Kind = 0, Server = _server, Login = "alice" }, null!, CancellationToken.None);

        Assert.False(reply.Ok);
        Assert.False(reply.Has);
        Assert.Equal("The credential store could not be written.", reply.Error);
        Assert.True(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    // ---- review round 3: Has agrees with the fill --------------------------------------------------------------------------------

    [Theory]
    // local, saved with trust, request's trust, expected
    [InlineData(false, false, null, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]    // a validated password never goes to a trusting request for a remote server …
    [InlineData(false, true, null, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, null, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]      // … but does on the engine's own machine (what the released builds wrote)
    [InlineData(true, true, null, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void Has_agrees_with_the_fill_for_the_connections_certificate_checking(bool local, bool savedTrusted, bool? requestTrust, bool expected)
    {
        // Review round 3: Has said yes when either key held a password, but a trusting request for a remote server is filled only
        // from mssql-web-trust:// — the browser skipped the password prompt and the connect then failed "password required".
        var dataSource = local ? "localhost" : _server;
        var login = "has-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Assert.True(SqlCredentialStore.Save(SqlServerCredentialResolver.ServerKey(dataSource, savedTrusted), login, "pw"));

            Assert.Equal(expected, SqlServerCredentialResolver.Has(dataSource, login, requestTrust));
            if (requestTrust is { } trust)
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = dataSource, UserID = login, Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = trust,
                };
                Assert.Equal(expected, SqlServerCredentialResolver.TryFill(builder));
                Assert.Equal(expected ? "pw" : string.Empty, builder.Password);
            }
        }
        finally
        {
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey(dataSource), login);
            SqlCredentialStore.Remove(SqlServerCredentialResolver.ServerKey(dataSource, trustServerCertificate: true), login);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void The_keys_read_follow_the_certificate_checking_and_the_engines_own_machine(bool? trust)
    {
        foreach (var dataSource in new[] { _server, "localhost", "127.0.0.1,1433" })
        {
            var keys = SqlServerCredentialResolver.FillKeys(dataSource, trust);
            var validated = SqlServerCredentialResolver.ServerKey(dataSource);
            var trusted = SqlServerCredentialResolver.ServerKey(dataSource, trustServerCertificate: true);

            if (trust != true) Assert.Equal(new[] { validated, trusted }, keys);
            else if (dataSource == _server) Assert.Equal(new[] { trusted }, keys);
            else Assert.Equal(new[] { trusted, validated }, keys);
        }
    }

    [Fact]
    public async Task CredentialRequest_has_honours_the_requests_trust_server_certificate()
    {
        Remember(Cs("validated-pw", trust: false));
        var handler = new CredentialHandler(new PgCredentialResolver());
        CredentialRequest Ask(bool? trust) => new() { Action = CredentialAction.Has, Kind = 0, Server = _server, Login = "alice", TrustServerCertificate = trust };

        var trusting = await handler.HandleAsync(Ask(true), null!, CancellationToken.None);
        var validating = await handler.HandleAsync(Ask(false), null!, CancellationToken.None);
        var unsaid = await handler.HandleAsync(Ask(null), null!, CancellationToken.None);

        Assert.True(trusting.Ok);
        Assert.False(trusting.Has);              // the connect would not be given it: the browser must ask for the password
        Assert.True(validating.Ok && validating.Has);
        Assert.True(unsaid.Ok && unsaid.Has);    // an older client: any remembered password

        // Forget ignores the flag and removes the password whatever it was saved with.
        var forgot = await handler.HandleAsync(
            new CredentialRequest { Action = CredentialAction.Forget, Kind = 0, Server = _server, Login = "alice", TrustServerCertificate = true }, null!, CancellationToken.None);
        Assert.True(forgot.Ok && forgot.Has);
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    // ---- review round 3: a store that cannot be read is never "forgotten" ------------------------------------------------------

    [Fact]
    public async Task A_forget_while_the_store_cannot_be_read_is_answered_not_ok_and_the_password_stays()
    {
        Assert.True(Remember(Cs("stored-pw")));
        var handler = new CredentialHandler(new PgCredentialResolver());

        CredentialResponse reply;
        using (LockStore())
        {
            Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));   // unreadable: "not found" …
            Assert.Throws<CredentialStoreWriteException>(() => SqlServerCredentialResolver.Forget(_server, "alice"));   // … never "forgotten"
            reply = await handler.HandleAsync(
                new CredentialRequest { Action = CredentialAction.Forget, Kind = 0, Server = _server, Login = "alice" }, null!, CancellationToken.None);
        }

        Assert.False(reply.Ok);
        Assert.Equal("The credential store could not be written.", reply.Error);
        Assert.True(SqlServerCredentialResolver.Has(_server, "alice"));
        Assert.Equal("stored-pw", PasswordOf(Resolve(Cs(null))));
    }

    [Fact]
    public void A_forget_with_nothing_remembered_still_answers_no()
    {
        // The removal now runs even when Has says no: a readable store without the entry removes nothing and fails nothing.
        var removed = new List<string>();
        SqlServerCredentialResolver.RemoveFromStore = (key, login) => { removed.Add(key); return SqlCredentialStore.Remove(key, login); };

        Assert.False(SqlServerCredentialResolver.Forget(_server, "alice"));
        Assert.Equal(new[] { SqlServerCredentialResolver.ServerKey(_server), SqlServerCredentialResolver.ServerKey(_server, trustServerCertificate: true) }, removed);
    }

    /// <summary>Holds the engine's credential store file open with no sharing, as another process might: it cannot be read.</summary>
    internal static IDisposable LockStore()
    {
        var path = Path.Combine(Path.GetDirectoryName(AkmlSql.Core.Constants.ConfigFilePath)!, "sql-credentials.json");
        return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    // ---- CredentialRequest ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CredentialRequest_with_the_SQL_Server_kind_asks_about_and_forgets_the_remembered_login()
    {
        Remember(Cs("stored-pw"));
        var handler = new CredentialHandler(new PgCredentialResolver());
        CredentialRequest Ask(int action) => new() { Action = action, Kind = 0, Server = _server, Login = "alice" };

        var has = await handler.HandleAsync(Ask(CredentialAction.Has), null!, CancellationToken.None);
        var forgot = await handler.HandleAsync(Ask(CredentialAction.Forget), null!, CancellationToken.None);
        var after = await handler.HandleAsync(Ask(CredentialAction.Has), null!, CancellationToken.None);

        Assert.True(has.Ok && has.Has);
        Assert.True(forgot.Ok && forgot.Has);
        Assert.True(after.Ok && !after.Has);
    }

    [Fact]
    public async Task With_PostgreSQL_off_the_SQL_Server_kind_is_still_served_and_the_PostgreSQL_kind_is_answered_feature_off()
    {
        Remember(Cs("stored-pw", trust: true));
        var handler = new CredentialHandler(new PgCredentialResolver(), postgresEnabled: false);

        var sql = await handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Has, Kind = 0, Server = _server, Login = "alice" }, null!, CancellationToken.None);
        var pg = await handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Forget, Kind = 1, Server = _server, Port = 5432, Login = "alice" }, null!, CancellationToken.None);

        Assert.True(sql.Ok && sql.Has);
        Assert.False(pg.Ok);
        Assert.False(pg.Has);
        Assert.Equal(DialectErrors.FeatureOffMessage, pg.Error);
    }
}

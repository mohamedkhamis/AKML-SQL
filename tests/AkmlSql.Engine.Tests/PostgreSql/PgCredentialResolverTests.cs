using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-104 to FR-106, task T042): a remembered PostgreSQL password lives only in the engine's protected store, under
/// the server key <c>pg://host:port</c> and the user name. It is filled in when a connection arrives without one, saved only
/// on request, forgotten on request, and never returned to a client or written to a log.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
public class PgCredentialResolverTests : IDisposable
{
    // A host unique to this class, so the process-wide store never mixes with another test.
    private readonly string _host = "cred-" + Guid.NewGuid().ToString("N")[..8] + ".test";
    private readonly PgCredentialResolver _resolver = new();

    public void Dispose() => _resolver.Forget(_host, 5432, "alice");

    private string Cs(string? password, int port = 5432, string user = "alice") =>
        PgConnectionString.Build(_host, port, "northwind", user, password, "prefer", null);

    private static string? PasswordOf(string connectionString) =>
        new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Password;

    [Fact]
    public void A_connection_that_carries_its_password_is_left_alone()
    {
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        var resolved = _resolver.Resolve(Cs("typed-pw"));

        Assert.Equal("typed-pw", PasswordOf(resolved.ConnectionString));
        Assert.False(resolved.PasswordInjected);
    }

    [Fact]
    public void A_connection_without_a_password_gets_the_remembered_one()
    {
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        var resolved = _resolver.Resolve(Cs(null));

        Assert.Equal("stored-pw", PasswordOf(resolved.ConnectionString));
        Assert.True(resolved.PasswordInjected);
    }

    [Fact]
    public void Without_a_remembered_password_the_string_is_unchanged_and_nothing_is_injected()
    {
        var resolved = _resolver.Resolve(Cs(null));

        Assert.True(string.IsNullOrEmpty(PasswordOf(resolved.ConnectionString)));
        Assert.False(resolved.PasswordInjected);
    }

    [Fact]
    public void The_key_is_the_host_port_and_user_so_other_servers_and_users_do_not_match()
    {
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        Assert.True(string.IsNullOrEmpty(PasswordOf(_resolver.Resolve(Cs(null, port: 5433)).ConnectionString)));
        Assert.True(string.IsNullOrEmpty(PasswordOf(_resolver.Resolve(Cs(null, user: "bob")).ConnectionString)));
        Assert.Equal("stored-pw", PasswordOf(_resolver.Resolve(Cs(null)).ConnectionString));
    }

    [Fact]
    public void The_host_is_matched_case_insensitively()
    {
        _resolver.Save(_host.ToUpperInvariant(), 5432, "alice", "stored-pw");

        Assert.True(_resolver.Has(_host, 5432, "ALICE"));
        Assert.Equal("stored-pw", PasswordOf(_resolver.Resolve(Cs(null)).ConnectionString));
    }

    [Fact]
    public void A_password_is_saved_after_a_successful_open_only_when_asked()
    {
        _resolver.RememberIfRequested(Cs("typed-pw"), remember: false);
        Assert.False(_resolver.Has(_host, 5432, "alice"));

        _resolver.RememberIfRequested(Cs("typed-pw"), remember: true);
        Assert.True(_resolver.Has(_host, 5432, "alice"));
        Assert.Equal("typed-pw", PasswordOf(_resolver.Resolve(Cs(null)).ConnectionString));
    }

    [Fact]
    public void Remembering_a_connection_without_a_password_stores_nothing()
    {
        _resolver.RememberIfRequested(Cs(null), remember: true);

        Assert.False(_resolver.Has(_host, 5432, "alice"));
    }

    [Fact]
    public void Forget_removes_it_and_reports_whether_there_was_one()
    {
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        Assert.True(_resolver.Forget(_host, 5432, "alice"));
        Assert.False(_resolver.Has(_host, 5432, "alice"));
        Assert.False(_resolver.Forget(_host, 5432, "alice"));
    }

    [Fact]
    public void The_stored_file_holds_no_clear_text_password()
    {
        _resolver.Save(_host, 5432, "alice", "clear-text-marker-9f3a");

        var path = Path.Combine(Path.GetDirectoryName(Constants.ConfigFilePath)!, "sql-credentials.json");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("clear-text-marker-9f3a", text);
        Assert.Contains(PgConnectionString.ServerKey(_host, 5432), text);
    }

    [Fact]
    public void The_credential_messages_have_no_password_in_either_direction()
    {
        var handler = new CredentialHandler(_resolver);
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        var has = handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Has, Kind = 1, Server = _host, Port = 5432, Login = "alice" }, null!, default).GetAwaiter().GetResult();
        Assert.True(has.Ok);
        Assert.True(has.Has);
        Assert.DoesNotContain("stored-pw", MessagePackSerializer.SerializeToJson(has));

        var forget = handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Forget, Kind = 1, Server = _host, Port = 5432, Login = "alice" }, null!, default).GetAwaiter().GetResult();
        Assert.True(forget.Ok);
        Assert.True(forget.Has);   // it existed before being removed
        Assert.False(_resolver.Has(_host, 5432, "alice"));
    }

    [Fact]
    public void A_request_for_SQL_Server_or_with_no_login_is_answered_not_found_without_touching_the_store()
    {
        var handler = new CredentialHandler(_resolver);
        _resolver.Save(_host, 5432, "alice", "stored-pw");

        var ms = handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Has, Kind = 0, Server = _host, Port = 5432, Login = "alice" }, null!, default).GetAwaiter().GetResult();
        var empty = handler.HandleAsync(new CredentialRequest { Action = CredentialAction.Has, Kind = 1, Server = _host, Port = 5432, Login = "" }, null!, default).GetAwaiter().GetResult();

        Assert.False(ms.Has);
        Assert.False(empty.Has);
        Assert.True(_resolver.Has(_host, 5432, "alice"));
    }

    [Fact]
    public void The_handler_serves_the_221_to_222_pair()
    {
        var handler = new CredentialHandler(_resolver);

        Assert.Equal(MessageTypes.CredentialRequest, handler.RequestMessageType);
        Assert.Equal(MessageTypes.CredentialResponse, handler.ResponseMessageType);
    }

    // ---- spec 043 follow-up (review): a store failure is an answer, never a throw, never a false "remembered"/"forgotten" ----

    [Fact]
    public void Save_and_remember_report_whether_the_password_is_now_kept()
    {
        Assert.True(_resolver.Save(_host, 5432, "alice", "stored-pw"));
        Assert.True(_resolver.RememberIfRequested(Cs("typed-pw"), remember: true));
        Assert.False(_resolver.RememberIfRequested(Cs("typed-pw"), remember: false));
        Assert.False(_resolver.RememberIfRequested(Cs(null), remember: true));
        Assert.False(_resolver.RememberIfRequested("Host=;=;", remember: true));   // a string that does not parse
    }

    [Fact]
    public void A_store_that_cannot_encrypt_or_write_the_password_is_reported_false_and_never_thrown()
    {
        // ConnectionChanged remembers in its background work before it loads the schema: a DPAPI failure used to throw there
        // and skip the schema load.
        var unencrypted = new PgCredentialResolver((_, _, _) => throw new System.Security.Cryptography.CryptographicException("DPAPI is not available"), SqlCredentialStore.Remove);
        var unwritten = new PgCredentialResolver((_, _, _) => false, SqlCredentialStore.Remove);

        Assert.False(unencrypted.RememberIfRequested(Cs("typed-pw"), remember: true));
        Assert.False(unencrypted.Save(_host, 5432, "alice", "typed-pw"));
        Assert.False(unwritten.RememberIfRequested(Cs("typed-pw"), remember: true));
        Assert.False(_resolver.Has(_host, 5432, "alice"));
    }

    [Fact]
    public void A_forget_the_store_cannot_write_throws_and_the_password_is_still_remembered()
    {
        var resolver = new PgCredentialResolver(SqlCredentialStore.Save, (_, _) => false);
        resolver.Save(_host, 5432, "alice", "stored-pw");

        Assert.Throws<AkmlSql.Engine.Pairing.CredentialStoreWriteException>(() => resolver.Forget(_host, 5432, "alice"));
        Assert.True(_resolver.Has(_host, 5432, "alice"));
        Assert.False(_resolver.Forget(_host, 5433, "alice"));   // nothing there in a store that works: nothing to write, no failure
    }

    [Fact]
    public void A_forget_with_nothing_remembered_still_runs_the_removal_and_answers_no()
    {
        // Review round 3: the removal runs even when Has says no, so a store that cannot be read is reported, not "nothing to forget".
        var removed = new List<string>();
        var resolver = new PgCredentialResolver(SqlCredentialStore.Save, (key, login) => { removed.Add(key); return SqlCredentialStore.Remove(key, login); });

        Assert.False(resolver.Forget(_host, 5433, "alice"));
        Assert.Equal(new[] { PgConnectionString.ServerKey(_host, 5433) }, removed);
    }

    [Fact]
    public async Task A_forget_while_the_store_cannot_be_read_is_answered_not_ok_and_the_password_stays()
    {
        Assert.True(_resolver.Save(_host, 5432, "alice", "stored-pw"));
        var handler = new CredentialHandler(_resolver);

        CredentialResponse reply;
        using (AkmlSql.Engine.Tests.Pairing.SqlServerCredentialResolverTests.LockStore())
        {
            Assert.False(_resolver.Has(_host, 5432, "alice"));   // unreadable: "not found" …
            Assert.Throws<AkmlSql.Engine.Pairing.CredentialStoreWriteException>(() => _resolver.Forget(_host, 5432, "alice"));   // … never "forgotten"
            reply = await handler.HandleAsync(
                new CredentialRequest { Action = CredentialAction.Forget, Kind = 1, Server = _host, Port = 5432, Login = "alice" }, null!, default);
        }

        Assert.False(reply.Ok);
        Assert.Equal("The credential store could not be written.", reply.Error);
        Assert.True(_resolver.Has(_host, 5432, "alice"));
    }

    [Fact]
    public async Task A_forget_the_store_cannot_write_is_answered_not_ok_and_never_forgotten()
    {
        var resolver = new PgCredentialResolver(SqlCredentialStore.Save, (_, _) => false);
        resolver.Save(_host, 5432, "alice", "stored-pw");
        var handler = new CredentialHandler(resolver);

        var reply = await handler.HandleAsync(
            new CredentialRequest { Action = CredentialAction.Forget, Kind = 1, Server = _host, Port = 5432, Login = "alice" }, null!, default);

        Assert.False(reply.Ok);
        Assert.False(reply.Has);
        Assert.Equal("The credential store could not be written.", reply.Error);
        Assert.True(_resolver.Has(_host, 5432, "alice"));
    }
}

[CollectionDefinition("PostgreSqlCredentialStore", DisableParallelization = true)]
public sealed class PostgreSqlCredentialStoreCollection { }

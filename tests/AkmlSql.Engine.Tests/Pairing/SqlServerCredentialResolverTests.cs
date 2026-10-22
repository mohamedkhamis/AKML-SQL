using System.Net;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.PostgreSql;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (reported 2026-10-22): the web edition's remembered SQL Server password. It is filled in only for a
/// bridge request on SQL authentication that arrives without one, saved only on request and only for a bridge request, kept
/// apart from the logins the SSMS extension saves in the same store, and asked about or forgotten through CredentialRequest.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
public class SqlServerCredentialResolverTests : IDisposable
{
    // A server unique to this class, so the process-wide store never mixes with another test.
    private readonly string _server = "sqlcred-" + Guid.NewGuid().ToString("N")[..8] + ".test";

    public void Dispose()
    {
        SqlServerCredentialResolver.Forget(_server, "alice");
        SqlCredentialStore.Remove(_server, "alice");
    }

    private string Cs(string? password, string user = "alice", bool windows = false)
    {
        var b = new SqlConnectionStringBuilder { DataSource = _server, InitialCatalog = "Northwind" };
        if (windows) b.IntegratedSecurity = true;
        else { b.UserID = user; b.Password = password ?? string.Empty; }
        return b.ConnectionString;
    }

    private static string PasswordOf(string cs) => new SqlConnectionStringBuilder(cs).Password;

    private static IDisposable FromBridge() => BridgeSourceIp.Set(IPAddress.Loopback);

    [Fact]
    public void A_bridge_request_without_a_password_gets_the_remembered_one()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true);

        Assert.Equal("stored-pw", PasswordOf(SqlServerCredentialResolver.Resolve(Cs(null), fromBridge: true)));
    }

    [Fact]
    public void A_typed_password_windows_authentication_and_other_logins_are_left_alone()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true);

        Assert.Equal("typed-pw", PasswordOf(SqlServerCredentialResolver.Resolve(Cs("typed-pw"), fromBridge: true)));
        Assert.Equal(Cs(null, windows: true), SqlServerCredentialResolver.Resolve(Cs(null, windows: true), fromBridge: true));
        Assert.Equal(string.Empty, PasswordOf(SqlServerCredentialResolver.Resolve(Cs(null, user: "bob"), fromBridge: true)));
    }

    [Fact]
    public void A_named_pipe_request_never_gets_a_web_remembered_password()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true);

        Assert.Equal(string.Empty, PasswordOf(SqlServerCredentialResolver.Resolve(Cs(null), fromBridge: false)));
    }

    [Fact]
    public void An_ssms_saved_login_is_never_used_for_a_bridge_request()
    {
        SqlCredentialStore.Save(_server, "alice", "ssms-pw");   // what the SSMS extension stores (spec 029)

        Assert.Equal(string.Empty, PasswordOf(SqlServerCredentialResolver.Resolve(Cs(null), fromBridge: true)));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void Nothing_is_saved_without_the_request_or_outside_the_bridge()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("pw"), remember: false);
        SqlServerCredentialResolver.RememberIfRequested(Cs("pw"), remember: true);   // named pipe

        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public void The_server_is_matched_case_insensitively_and_forget_removes_it()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true);

        Assert.True(SqlServerCredentialResolver.Has(_server.ToUpperInvariant(), "alice"));
        Assert.True(SqlServerCredentialResolver.Forget(_server, "alice"));
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
        Assert.False(SqlServerCredentialResolver.Forget(_server, "alice"));
    }

    [Fact]
    public async Task CredentialRequest_with_the_SQL_Server_kind_asks_about_and_forgets_the_remembered_login()
    {
        using (FromBridge()) SqlServerCredentialResolver.RememberIfRequested(Cs("stored-pw"), remember: true);
        var handler = new CredentialHandler(new PgCredentialResolver());
        CredentialRequest Ask(int action) => new() { Action = action, Kind = 0, Server = _server, Login = "alice" };

        var has = await handler.HandleAsync(Ask(CredentialAction.Has), null!, CancellationToken.None);
        var forgot = await handler.HandleAsync(Ask(CredentialAction.Forget), null!, CancellationToken.None);
        var after = await handler.HandleAsync(Ask(CredentialAction.Has), null!, CancellationToken.None);

        Assert.True(has.Ok && has.Has);
        Assert.True(forgot.Ok && forgot.Has);
        Assert.True(after.Ok && !after.Has);
    }
}

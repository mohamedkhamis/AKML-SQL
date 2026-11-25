using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 047 (WEB-002, research R4): Save with Remember reuses the engine's existing test-and-remember round trip
/// (TestSqlConnectionRequest.RememberPassword, key 3; Response.Remembered, key 4). No new IPC message.
/// </summary>
public sealed class TestAndRememberTests
{
    private static (SqlConnectionService Service, FakeEngineBridge Bridge) Create(params string[] capabilities)
    {
        var bridge = new FakeEngineBridge { EngineCapabilities = capabilities };
        bridge.SetState(BridgeState.Open);
        return (new SqlConnectionService(bridge, new FakeDiagnosticsRingBuffer()), bridge);
    }

    private static ConnectionTarget SaLogin(string password = "Secret#1") =>
        new(DatabaseKind.SqlServer, "localhost", "master", WindowsAuth: false, User: "sa", Password: password, RememberPassword: true);

    [Fact]
    public async Task The_test_asks_the_engine_to_keep_the_typed_password()
    {
        var (service, bridge) = Create(SqlConnectionService.CapabilitySqlServerCredentials, SqlConnectionService.CapabilitySqlServerCredentialsV2);
        bridge.TestSqlReply = new TestSqlConnectionResponse { Ok = true, Remembered = true, ServerVersion = "SQL Server 16.0" };

        var result = await service.TestAndRememberAsync(SaLogin(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(result.Remembered);
        var (type, sent) = Assert.Single(bridge.Requests);
        Assert.Equal(MessageTypes.TestSqlConnection, type);
        var request = Assert.IsType<TestSqlConnectionRequest>(sent);
        Assert.True(request.RememberPassword);
        Assert.Contains("Secret#1", request.ConnectionString);
    }

    [Fact]
    public async Task An_engine_that_could_not_store_it_says_so()
    {
        var (service, bridge) = Create(SqlConnectionService.CapabilitySqlServerCredentials, SqlConnectionService.CapabilitySqlServerCredentialsV2);
        bridge.TestSqlReply = new TestSqlConnectionResponse { Ok = true, Remembered = false };

        var result = await service.TestAndRememberAsync(SaLogin(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(result.Remembered);
    }

    [Fact]
    public async Task An_engine_before_v2_cannot_confirm_and_is_taken_at_its_word()
    {
        var (service, bridge) = Create(SqlConnectionService.CapabilitySqlServerCredentials);
        bridge.TestSqlReply = new TestSqlConnectionResponse { Ok = true };   // v1 never sends key 4

        var result = await service.TestAndRememberAsync(SaLogin(), CancellationToken.None);

        Assert.True(result.Remembered);
    }

    [Fact]
    public async Task A_refused_login_keeps_nothing_and_reports_the_server_message()
    {
        var (service, bridge) = Create(SqlConnectionService.CapabilitySqlServerCredentials, SqlConnectionService.CapabilitySqlServerCredentialsV2);
        bridge.TestSqlReply = new TestSqlConnectionResponse
        {
            Ok = false, ErrorMessage = "Login failed for user 'sa'.", ErrorCode = DialectErrors.LoginFailed,
        };

        var result = await service.TestAndRememberAsync(SaLogin("wrong"), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(result.Remembered);
        Assert.Equal("Login failed for user 'sa'.", result.Error);
    }

    [Fact]
    public async Task Without_an_open_bridge_nothing_is_sent()
    {
        var bridge = new FakeEngineBridge();   // Disconnected
        var service = new SqlConnectionService(bridge, new FakeDiagnosticsRingBuffer());

        var result = await service.TestAndRememberAsync(SaLogin(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Empty(bridge.Requests);
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

/// <summary>
/// Who may use the WebSocket bridge, and what it may make the engine connect to.
/// <list type="bullet">
///   <item><description>Localhost mode has no PIN, so a browser page from anywhere else must be
///   refused by its Origin -- otherwise any website the user visits could drive the
///   engine.</description></item>
///   <item><description>A browser opening the bridge address directly gets a readable page, which
///   is how a LAN engine's self-signed certificate is accepted.</description></item>
///   <item><description>A bridge request may not make the engine sign in as itself anywhere but its
///   own machine (<see cref="BridgeSqlTargetGuard"/>).</description></item>
/// </list>
/// </summary>
public sealed class BridgeAccessTests
{
    // --- Origin (localhost mode) ------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:59416")]
    [InlineData("https://localhost:5001")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void APageFromThisMachine_OrNoBrowserAtAll_MayConnect(string? origin)
    {
        Assert.True(WebSocketTransport.IsAllowedLocalOrigin(origin));
    }

    [Fact]
    public void APageServedUnderThisMachinesName_MayConnect()
    {
        Assert.True(WebSocketTransport.IsAllowedLocalOrigin($"http://{Environment.MachineName.ToLowerInvariant()}:59416"));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://127.0.0.1.evil.example")]
    [InlineData("http://10.0.0.5")]
    [InlineData("null")]
    [InlineData("file://")]
    [InlineData("chrome-extension://abcdef")]
    [InlineData("not a url")]
    public void AnyOtherPage_IsRefused(string origin)
    {
        Assert.False(WebSocketTransport.IsAllowedLocalOrigin(origin));
    }

    [Fact]
    public async Task AForeignOrigin_IsRefusedAtTheUpgrade_AndALocalOneIsNot()
    {
        var (transport, port) = await StartLocalhostAsync();
        try
        {
            using var foreign = new ClientWebSocket();
            foreign.Options.SetRequestHeader("Origin", "https://evil.example");
            var refused = await Assert.ThrowsAsync<WebSocketException>(
                () => foreign.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None));
            Assert.Contains("403", refused.Message + refused.InnerException?.Message, StringComparison.Ordinal);

            using var local = new ClientWebSocket();
            local.Options.SetRequestHeader("Origin", $"http://localhost:{port}");
            await local.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
            Assert.Equal(WebSocketState.Open, local.State);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    // --- a browser opening the bridge address -----------------------------------------------

    [Fact]
    public async Task OpeningTheBridgeAddressInABrowser_ShowsAPage_NotAnError()
    {
        var (transport, port) = await StartLocalhostAsync();
        try
        {
            using var http = new HttpClient();

            var response = await http.GetAsync($"http://127.0.0.1:{port}/akmlsql");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("AKML SQL engine: reachable", body, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task AnythingButGet_IsNotServed()
    {
        var (transport, port) = await StartLocalhostAsync();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync($"http://127.0.0.1:{port}/akmlsql", new StringContent("x"));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    // --- SQL targets a bridge request may open ----------------------------------------------

    [Theory]
    [InlineData("Server=localhost;Database=master;Integrated Security=true")]
    [InlineData("Server=.;Database=master;Integrated Security=true")]
    [InlineData("Server=(local)\\SQLEXPRESS;Database=master;Integrated Security=true")]
    [InlineData("Server=127.0.0.1,1433;Database=master;Integrated Security=true")]
    [InlineData("Server=tcp:localhost,1433;Database=master;Integrated Security=true")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true")]
    [InlineData("Server=np:\\\\.\\pipe\\sql\\query;Database=master;Integrated Security=true")]
    public void TheEnginesOwnMachine_IsAllowedAnyAuthentication(string connectionString)
    {
        Assert.Null(BridgeSqlTargetGuard.Check(connectionString, fromBridge: true));
    }

    [Theory]
    [InlineData("Server=162.220.54.191,1433;Database=master;User ID=sa;Password=pw")]
    [InlineData("Server=tcp:db.example.com\\PROD;Database=sales;User ID=app;Password=pw")]
    [InlineData("Server=myserver.database.windows.net;Database=db;Authentication=Active Directory Password;User ID=u@x.com;Password=pw")]
    public void ARemoteServer_WithTypedCredentials_IsAllowed(string connectionString)
    {
        Assert.Null(BridgeSqlTargetGuard.Check(connectionString, fromBridge: true));
    }

    [Theory]
    [InlineData("Server=162.220.54.191;Database=master;Integrated Security=true")]
    [InlineData("Server=db.example.com;Database=master;Integrated Security=SSPI")]
    [InlineData("Server=127.0.0.1.evil.example;Database=master;Integrated Security=true")]
    [InlineData("Server=myserver.database.windows.net;Database=db;Authentication=Active Directory Integrated")]
    [InlineData("Server=myserver.database.windows.net;Database=db;Authentication=Active Directory Default")]
    [InlineData("Server=myserver.database.windows.net;Database=db;Authentication=Active Directory Managed Identity")]
    public void ARemoteServer_WithTheEnginesOwnIdentity_IsRefused(string connectionString)
    {
        Assert.Equal(BridgeSqlTargetGuard.AmbientIdentityRefused, BridgeSqlTargetGuard.Check(connectionString, fromBridge: true));
    }

    [Theory]
    [InlineData("Server=np:\\\\fileserver\\pipe\\sql\\query;Database=master;User ID=sa;Password=pw")]
    [InlineData("Server=\\\\fileserver\\pipe\\sql\\query;Database=master;User ID=sa;Password=pw")]
    [InlineData("Server=lpc:remote;Database=master;User ID=sa;Password=pw")]
    public void ARemoteNamedPipe_IsRefused_WhateverTheLogin(string connectionString)
    {
        // A named pipe is SMB: opening it signs in as the engine's account before SQL auth starts.
        Assert.Equal(BridgeSqlTargetGuard.NonTcpRefused, BridgeSqlTargetGuard.Check(connectionString, fromBridge: true));
    }

    [Fact]
    public void TheShellOverTheNamedPipe_IsNotRestricted()
    {
        // The SSMS / VS extension runs as the signed-in user; Windows auth to a remote server is its
        // everyday case.
        Assert.Null(BridgeSqlTargetGuard.Check(
            "Server=db.example.com;Database=master;Integrated Security=true", fromBridge: false));
    }

    [Fact]
    public void AnUnparseableConnectionString_IsRefused()
    {
        Assert.NotNull(BridgeSqlTargetGuard.Check("Server=x;Nonsense Keyword=1", fromBridge: true));
    }

    [Fact]
    public void WithoutABridgeConnection_TheAmbientCheckAllows()
    {
        // Outside a bridge connection there is no source address: the named-pipe case.
        Assert.Null(BridgeSqlTargetGuard.Check("Server=db.example.com;Database=master;Integrated Security=true"));

        using (BridgeSourceIp.Set(IPAddress.Loopback))
        {
            Assert.Equal(BridgeSqlTargetGuard.AmbientIdentityRefused,
                BridgeSqlTargetGuard.Check("Server=db.example.com;Database=master;Integrated Security=true"));
        }
    }

    private static async Task<(WebSocketTransport, int)> StartLocalhostAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = 47291 + Random.Shared.Next(1000, 9000);
            var transport = new WebSocketTransport(new WebSocketTransportOptions { BindAddress = "127.0.0.1", Port = port });
            transport.RequestReceived += (_, _) => Task.FromResult<AkmlSql.Core.Ipc.RpcMessage?>(null);
            try
            {
                await transport.StartAsync(CancellationToken.None);
                return (transport, port);
            }
            catch (InvalidOperationException)
            {
                await transport.DisposeAsync();
            }
        }

        throw new InvalidOperationException("Could not bind a free localhost port for the test.");
    }
}

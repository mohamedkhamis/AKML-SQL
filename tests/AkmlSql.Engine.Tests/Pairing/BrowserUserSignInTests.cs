using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Handlers.Handshake;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): with Windows authentication, the web edition signed in to SQL Server
/// as the engine service's account (NT AUTHORITY\SYSTEM), which in the owner's database <c>aqmar</c> could see none of its 4
/// tables. A browser on the engine's computer now signs in as its Windows user: the engine finds the process at the browser's
/// end of the connection in Windows' TCP table and uses the token of the user signed in to that session.
/// </summary>
public sealed class BrowserUserSignInTests
{
    private static IPEndPoint Ep(string address, int port) => new(IPAddress.Parse(address), port);

    // ── the browser's process ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_browsers_end_of_the_connection_names_its_process_not_the_engines()
    {
        var client = Ep("127.0.0.1", 50123);
        var server = Ep("127.0.0.1", 59417);
        var rows = new[]
        {
            new LoopbackCaller.TcpRow(server, client, 4242),                       // the engine's end
            new LoopbackCaller.TcpRow(Ep("127.0.0.1", 50124), server, 777),        // another browser connection
            new LoopbackCaller.TcpRow(client, Ep("127.0.0.1", 1433), 999),         // the same port to another server
            new LoopbackCaller.TcpRow(client, server, 1234),                       // the browser's end
        };

        Assert.Equal(1234, LoopbackCaller.MatchOwningProcess(rows, client, server));
    }

    [Fact]
    public void IPv6_loopback_and_an_IPv4_mapped_client_are_matched()
    {
        var rows6 = new[] { new LoopbackCaller.TcpRow(Ep("::1", 50200), Ep("::1", 59417), 88) };
        Assert.Equal(88, LoopbackCaller.MatchOwningProcess(rows6, Ep("::1", 50200), Ep("::1", 59417)));

        var rows4 = new[] { new LoopbackCaller.TcpRow(Ep("127.0.0.1", 50300), Ep("127.0.0.1", 59417), 99) };
        Assert.Equal(99, LoopbackCaller.MatchOwningProcess(rows4, Ep("::ffff:127.0.0.1", 50300), Ep("::ffff:127.0.0.1", 59417)));
    }

    [Fact]
    public void No_matching_row_is_no_process()
        => Assert.Equal(0, LoopbackCaller.MatchOwningProcess(
            new[] { new LoopbackCaller.TcpRow(Ep("127.0.0.1", 1), Ep("127.0.0.1", 2), 5) }, Ep("127.0.0.1", 3), Ep("127.0.0.1", 2)));

    [Theory]
    [InlineData(0x19E8, 59417)]   // the port's bytes in network order, read as a little-endian DWORD
    [InlineData(0x5000, 80)]
    public void A_port_is_read_from_network_byte_order(int stored, int port) => Assert.Equal(port, LoopbackCaller.Port(stored));

    [Fact]
    public void A_browser_on_another_computer_signs_in_as_the_engines_account()
    {
        var resolution = LoopbackCaller.Resolve(Ep("192.168.1.20", 50000), Ep("192.168.1.10", 59417));

        Assert.Null(resolution.Identity);
        Assert.Equal("the browser is on another computer", resolution.Reason);
    }

    [Fact]
    public async Task A_live_loopback_connection_is_traced_to_the_process_that_opened_it()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = (IPEndPoint)listener.LocalEndpoint;
        using var client = new TcpClient();
        await client.ConnectAsync(server.Address, server.Port);
        using var accepted = await listener.AcceptTcpClientAsync();

        var resolution = LoopbackCaller.Resolve((IPEndPoint)accepted.Client.RemoteEndPoint!, server);

        Assert.Equal(Environment.ProcessId, resolution.ProcessId);
        // A test process cannot read session tokens (no SeTcbPrivilege), and the "browser" here is itself: no identity is
        // needed, as for a Debug engine whose browser runs as the same user. The installed service (LocalSystem) can.
        Assert.Null(resolution.Identity);
        Assert.Equal("the browser runs as the engine's own user", resolution.Reason);
    }

    // ── signing in ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Data Source=.;Integrated Security=true", true)]
    [InlineData("Server=localhost;Trusted_Connection=yes;Database=aqmar", true)]
    [InlineData("Server=localhost;User ID=sa;Password=x", false)]
    [InlineData("", false)]
    [InlineData("not a connection string", false)]
    public void Only_Windows_authentication_signs_in_as_the_browser_user(string connectionString, bool expected)
        => Assert.Equal(expected, SqlOpen.UsesWindowsIdentity(connectionString));

    private const string LocalWindows = "Data Source=(local);Integrated Security=true;TrustServerCertificate=true;Database=master;Connect Timeout=5";

    private static bool LocalSqlAnswers()
    {
        try
        {
            using var conn = new SqlConnection(LocalWindows);
            conn.Open();
            return true;
        }
        catch (Exception) { return false; }
    }

    [SkippableFact]
    public async Task A_Windows_connection_opened_for_a_browser_user_signs_in_as_that_user()
    {
        Skip.IfNot(LocalSqlAnswers(), "No local SQL Server answering with Windows authentication.");
        // The only other identity a test can obtain without a password is its own, so the impersonated path runs as it.
        using var me = WindowsIdentity.GetCurrent();
        using (BridgeCaller.Set(me))
        {
            await using var conn = new SqlConnection(LocalWindows);
            await SqlOpen.OpenAsync(conn, CancellationToken.None);
            await using var cmd = new SqlCommand("SELECT SUSER_SNAME()", conn);
            Assert.Equal(me.Name, (string?)await cmd.ExecuteScalarAsync(), ignoreCase: true);
        }
    }

    [Fact]
    public void A_registered_refresh_keeps_the_browser_user_and_forgets_it_with_its_session()
    {
        using var cache = new SchemaCacheManager();
        using var me = WindowsIdentity.GetCurrent();
        const string cs = "Data Source=.;Integrated Security=true";

        using (BridgeCaller.Set(me)) cache.RegisterConnectionString("session-1", "aqmar", cs);
        cache.RegisterConnectionString("session-2", "aqmar", cs);   // the SSMS pipe, or a browser the engine could not place

        Assert.Same(me, cache.CallerFor("session-1", "aqmar"));
        Assert.Null(cache.CallerFor("session-2", "aqmar"));
        cache.RemoveSession("session-1");
        Assert.Null(cache.CallerFor("session-1", "aqmar"));
    }

    [Fact]
    public async Task The_handshake_names_the_browser_user_and_otherwise_the_engines_account()
    {
        var runsAs = EngineIdentity.RunsAs;
        try
        {
            EngineIdentity.RunsAs = @"NT AUTHORITY\SYSTEM";
            using var me = WindowsIdentity.GetCurrent();
            var request = new HandshakeRequest
            {
                WebVersion = "1.0.0", ProtocolVersionMin = Capabilities.ProtocolVersionMin,
                ProtocolVersionMax = Capabilities.ProtocolVersionMax, BrowserLabel = "test-browser",
            };
            RpcContext Context() => new()
            {
                Sessions = new SessionManager(), SchemaCache = new SchemaCacheManager(), Logger = Serilog.Log.Logger,
                SettingsLoader = () => new AkmlSql.Core.Config.AppSettings(),
            };

            HandshakeResponse asBrowserUser;
            using (BridgeCaller.Set(me))
                asBrowserUser = await new HandshakeHandler().HandleAsync(request, Context(), CancellationToken.None);
            var asEngine = await new HandshakeHandler().HandleAsync(request, Context(), CancellationToken.None);

            Assert.Equal(me.Name, asBrowserUser.RunsAs);
            Assert.Equal(@"NT AUTHORITY\SYSTEM", asEngine.RunsAs);
        }
        finally
        {
            EngineIdentity.RunsAs = runsAs;
        }
    }
}

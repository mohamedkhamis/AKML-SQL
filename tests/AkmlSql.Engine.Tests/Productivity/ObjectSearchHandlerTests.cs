using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Productivity;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Productivity;

/// <summary>
/// Spec 030 T085 / FR-045. Exercises <see cref="ObjectSearchHandler"/> directly via
/// <see cref="ObjectSearchHandler.HandleAsync"/> and once through the
/// <see cref="InProcessTransport"/> + <see cref="RpcRouter"/> path (which validates the
/// 62 → 162 request/response message-code wiring).
/// </summary>
public sealed class ObjectSearchHandlerTests
{
    private const string SessionId = "sess-1";
    private const string DatabaseName = "TestDb";

    /// <summary>
    /// Builds an <see cref="RpcContext"/> with a connected session and a populated
    /// <see cref="DatabaseCache"/> keyed by (sessionId, databaseName) — matching the slot the
    /// handler uses (<c>ctx.SchemaCache.GetCache(sessionId, db)</c>).
    /// </summary>
    private static RpcContext CreateContext(bool connected = true, params (string Schema, string Name, DbObjectType Type)[] objects)
    {
        var sessions = new SessionManager();
        var schemaCache = new SchemaCacheManager();

        if (connected)
        {
            sessions.UpdateSession(new ConnectionInfo
            {
                SessionId = SessionId,
                DatabaseName = DatabaseName,
                ConnectionString = "Server=.;Database=" + DatabaseName + ";Integrated Security=true"
            });

            var cache = schemaCache.GetOrCreateCache(SessionId, DatabaseName);
            foreach (var (schemaName, name, type) in objects)
            {
                if (!cache.Schemas.TryGetValue(schemaName, out var schema))
                {
                    schema = new SchemaEntry { SchemaName = schemaName };
                    cache.Schemas[schemaName] = schema;
                }
                schema.Objects.Add(new DatabaseObject
                {
                    SchemaName = schemaName,
                    ObjectName = name,
                    ObjectType = type
                });
            }
            cache.Phase = PopulationPhase.PhaseA;
        }

        return new RpcContext
        {
            Sessions = sessions,
            SchemaCache = schemaCache,
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
        };
    }

    [Fact]
    public async Task Returns_objects_matching_the_query()
    {
        var ctx = CreateContext(true,
            ("dbo", "Customers", DbObjectType.Table),
            ("dbo", "Orders", DbObjectType.Table),
            ("dbo", "GetAllUsers", DbObjectType.Procedure));
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "Cust", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.Results);
        Assert.Equal("Customers", response.Results[0].ObjectName);
        Assert.Equal("dbo", response.Results[0].SchemaName);
        Assert.Equal("Table", response.Results[0].ObjectType);
    }

    [Fact]
    public async Task Ranks_exact_and_prefix_matches_above_substring_matches()
    {
        var ctx = CreateContext(true,
            ("dbo", "Order", DbObjectType.Table),          // exact   → 100
            ("dbo", "OrderDetail", DbObjectType.Table),    // prefix  → 80
            ("dbo", "CustomerOrders", DbObjectType.Table)); // contains → 50
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "Order", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(3, response.Results.Length);
        // Exact match first, then prefix, then substring.
        Assert.Equal("Order", response.Results[0].ObjectName);
        Assert.Equal("OrderDetail", response.Results[1].ObjectName);
        Assert.Equal("CustomerOrders", response.Results[2].ObjectName);
    }

    [Fact]
    public async Task Respects_the_MaxResults_cap()
    {
        var ctx = CreateContext(true,
            ("dbo", "Order1", DbObjectType.Table),
            ("dbo", "Order2", DbObjectType.Table),
            ("dbo", "Order3", DbObjectType.Table),
            ("dbo", "Order4", DbObjectType.Table));
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "Order", MaxResults = 2 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Results.Length);
    }

    [Fact]
    public async Task Returns_empty_when_nothing_matches()
    {
        var ctx = CreateContext(true,
            ("dbo", "Customers", DbObjectType.Table));
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "zzz_no_match", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Whitespace_query_returns_empty_results()
    {
        var ctx = CreateContext(true,
            ("dbo", "Customers", DbObjectType.Table));
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "   ", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Disconnected_session_fails_with_no_connection_error()
    {
        var ctx = CreateContext(connected: false);
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "Cust", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("No active database connection for this session", response.Error);
    }

    [Fact]
    public async Task Connected_session_without_populated_cache_succeeds_with_empty_results()
    {
        // Connected session is registered but no cache was created for it.
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = SessionId,
            DatabaseName = DatabaseName,
            ConnectionString = "Server=.;Database=" + DatabaseName
        });
        var ctx = new RpcContext
        {
            Sessions = sessions,
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
        };
        var handler = new ObjectSearchHandler();

        var response = await handler.HandleAsync(
            new ObjectSearchRequest { SessionId = SessionId, SearchText = "Cust", MaxResults = 50 },
            ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Through_InProcessTransport_round_trips_via_RpcRouter()
    {
        var ctx = CreateContext(true,
            ("dbo", "Customers", DbObjectType.Table));
        var handler = new ObjectSearchHandler();

        var router = new RpcRouter();
        router.Register(handler);

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new ObjectSearchRequest { SessionId = SessionId, SearchText = "Cust", MaxResults = 50 };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.ObjectSearch,
            RequestId = 42,
            Payload = MessagePackSerializer.Serialize(request),
        };

        var response = await transport.SendAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.ObjectSearchResult, response!.MessageType);
        Assert.Equal(42, response.RequestId);
        var typed = MessagePackSerializer.Deserialize<ObjectSearchResponse>(response.Payload!);
        Assert.True(typed.Success);
        Assert.Single(typed.Results);
        Assert.Equal("Customers", typed.Results[0].ObjectName);
    }
}

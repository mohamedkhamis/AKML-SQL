using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Re-review REV-01 (from the reviewer's acceptance probe, assertions kept): a SQL Server or PostgreSQL address is relative to
/// the engine that reaches it, so two paired engines can each have their own <c>localhost/Sales</c>. Their schemas must never
/// share a cache entry, and the AI must never be given the other engine's schema — the real connection service, cache and
/// provider over a scripted bridge, with a sentinel table name and nothing sent to a provider.
/// </summary>
public sealed class AiSchemaEngineScopeTests
{
    private sealed class TwoEngines : IEngineBridge
    {
        public BridgeState State => BridgeState.Open;
        public string? ConnectedUrl { get; private set; } = "wss://engine-a.example:47291/";
        public string[] EngineCapabilities => new[] { SqlConnectionService.CapabilityPostgreSql };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged;
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }
        public void Switch() { ConnectedUrl = "wss://engine-b.example:47291/"; StateChanged?.Invoke(BridgeState.Open); }
        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());
        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
            => Task.FromResult((TResponse)(object)new TestSqlConnectionResponse { Ok = true, ServerVersion = "PostgreSQL 18.1" });
        public Task SendNotificationAsync<T>(int type, T payload, CancellationToken ct) where T : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static byte[] Sentinel(string table) => MessagePackSerializer.Serialize(new SchemaPhasePayload
    {
        Phase = (int)PopulationPhase.PhaseB,
        Schemas = [new SchemaPhaseSchema { Name = "dbo", Objects = [new SchemaPhaseObject {
            SchemaName = "dbo", ObjectName = table, ObjectType = (int)DbObjectType.Table,
            Columns = [new SchemaPhaseColumn { Name = "Id", TypeName = "int" }],
        }] }],
    });

    private static async Task<(SchemaCacheStore Cache, AiFeatureSettingsStore Settings)> StoresAsync()
    {
        var db = new InMemoryIndexedDbAdapter();
        var settings = new AiFeatureSettingsStore(db);
        await settings.SetAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.SchemaNamesOnly });
        return (new SchemaCacheStore(db), settings);
    }

    [Fact]
    public async Task Localhost_on_another_engine_must_not_disclose_the_previous_engines_schema()
    {
        var (cache, settings) = await StoresAsync();
        var bridge = new TwoEngines();
        var sql = new SqlConnectionService(bridge, new FakeDiagnosticsRingBuffer());
        Assert.True((await sql.ConnectAsync("localhost", "Sales", true, null, null, CancellationToken.None)).Ok);
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = sql.SchemaIdentity!, DatabaseName = "Sales", SourceConnectionId = "engine-a",
            PhaseB = Sentinel("EngineASecretPayroll"),
        });
        var provider = new AiSchemaContextProvider(cache, settings, sql);
        Assert.Contains("EngineASecretPayroll", await provider.GetSchemaTextAsync("explain", "SELECT 1", CancellationToken.None));

        bridge.Switch();
        Assert.True((await sql.ConnectAsync("localhost", "Sales", true, null, null, CancellationToken.None)).Ok);

        Assert.DoesNotContain("EngineASecretPayroll", await provider.GetSchemaTextAsync("explain", "SELECT 1", CancellationToken.None));
    }

    [Fact]
    public async Task PostgreSQL_localhost_on_another_engine_is_another_cache_entry_too()
    {
        var (cache, settings) = await StoresAsync();
        var bridge = new TwoEngines();
        var sql = new SqlConnectionService(bridge, new FakeDiagnosticsRingBuffer());
        var target = new ConnectionTarget(DatabaseKind.PostgreSql, "localhost", "sales", User: "app", Password: "synthetic");
        Assert.True((await sql.ConnectAsync(target, CancellationToken.None)).Ok);
        var engineA = sql.SchemaIdentity!;
        Assert.StartsWith("pg://localhost:5432", engineA);     // the kind is still told apart by its prefix
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = engineA, DatabaseName = "sales", PhaseB = Sentinel("EngineAPgSecret") });
        var provider = new AiSchemaContextProvider(cache, settings, sql);

        bridge.Switch();
        Assert.True((await sql.ConnectAsync(target, CancellationToken.None)).Ok);

        Assert.NotEqual(engineA, sql.SchemaIdentity);
        Assert.DoesNotContain("EngineAPgSecret", await provider.GetSchemaTextAsync("explain", "SELECT 1", CancellationToken.None));
    }

    [Fact]
    public void An_entry_cached_before_the_scope_never_matches_a_scoped_key() =>
        Assert.NotEqual("localhost", SqlConnectionService.ScopeToEngine("localhost", "ws://127.0.0.1:47291/"));
}

using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Cache;

/// <summary>
/// Spec 021 (web edition) -- M5 task T108. SchemaSync surface tests:
/// Start/Stop lifecycle, idle-clock reporting, and the SINGLE-POLL behaviour
/// driven through the internal PollNowAsync test hook.
///
/// The 30 s timer cadence + 5 min idle suspend are driven by background timers
/// we don't synchronously inspect -- those are covered by the Playwright spec
/// in the next interactive session (T113).
/// </summary>
public sealed class SchemaSyncTests
{
    private static (ISchemaSync sync, SchemaSync raw, ISchemaCacheStore cache) Build(IEngineBridge? bridge = null)
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var diag = new DiagnosticsRingBuffer(db);
        bridge ??= new EngineBridge(
            () => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));
        var sync = new SchemaSync(cache, bridge, diag);
        return (sync, sync, cache);
    }

    [Fact]
    public async Task StartAsync_marks_the_sync_as_running()
    {
        var (sync, raw, _) = Build();
        Assert.False(raw.IsRunning);
        await sync.StartAsync("session-1", "server", "db");
        Assert.True(raw.IsRunning);
        await sync.StopAsync();
        Assert.False(raw.IsRunning);
    }

    [Fact]
    public async Task ReportEditorActive_updates_the_last_activity_timestamp()
    {
        var (sync, raw, _) = Build();
        await sync.StartAsync("session-1", "s", "db");

        var before = raw.LastEditorActivity;
        await Task.Delay(5);
        sync.ReportEditorActive();
        var after = raw.LastEditorActivity;

        Assert.True(after > before);
        await sync.StopAsync();
    }

    [Fact]
    public async Task StartAsync_is_idempotent()
    {
        var (sync, raw, _) = Build();
        await sync.StartAsync("session-1", "s", "db");
        await sync.StartAsync("session-1", "s", "db");   // second start should not throw
        Assert.True(raw.IsRunning);
        await sync.StopAsync();
    }

    [Fact]
    public async Task PollNowAsync_with_closed_bridge_touches_LastUsedAt_only()
    {
        var (_, raw, cache) = Build();
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s", DatabaseName = "db", Checksum = "original",
        });
        var beforeUsedAt = (await cache.GetAsync("s", "db"))!.LastUsedAt;
        await Task.Delay(5);

        await raw.PollNowAsync("session-1", "s", "db");

        var after = await cache.GetAsync("s", "db");
        Assert.NotNull(after);
        Assert.True(after!.LastUsedAt > beforeUsedAt);
        Assert.Equal("original", after.Checksum);   // unchanged -- bridge was closed
    }

    /// <summary>
    /// T109 follow-up Issue 2b: StartAsync should fire an immediate first poll
    /// instead of waiting the full 30 s polling interval. On a fresh connect the
    /// user starts typing right away — we don't want IntelliSense empty for the
    /// first 30 s while the loop sleeps.
    /// </summary>
    [Fact]
    public async Task StartAsync_fires_an_immediate_initial_poll()
    {
        // Closed bridge: PollOnceAsync should still execute and call TouchAsync
        // on the cache; the LastUsedAt timestamp moves.
        var (sync, _, cache) = Build();
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s", DatabaseName = "db", Checksum = "x",
        });
        var beforeUsedAt = (await cache.GetAsync("s", "db"))!.LastUsedAt;
        await Task.Delay(5);

        await sync.StartAsync("session-1", "s", "db");

        // The initial poll is fire-and-forget; give it a moment to settle.
        for (int i = 0; i < 20; i++)
        {
            var after = (await cache.GetAsync("s", "db"))!;
            if (after.LastUsedAt > beforeUsedAt) break;
            await Task.Delay(10);
        }

        var final = await cache.GetAsync("s", "db");
        Assert.NotNull(final);
        Assert.True(final!.LastUsedAt > beforeUsedAt,
            "Initial poll never ran — LastUsedAt should have moved forward by now.");
        await sync.StopAsync();
    }

    /// <summary>
    /// T109 wire-up test: on checksum drift the sync fetches Phase A through the
    /// open bridge and persists the returned bytes into <c>SchemaSnapshot.PhaseA</c>.
    /// Catches regressions where the message-type ids or DTO field names drift
    /// (the offline-completion tests seed the cache directly so they wouldn't).
    /// </summary>
    [Fact]
    public async Task PollNowAsync_on_drift_fetches_PhaseA_and_persists_to_cache()
    {
        var phaseABlob = MessagePackSerializer.Serialize(new SchemaPhasePayload
        {
            DatabaseName = "db",
            Phase = 1,
            Checksum = "PhaseA:1",
            Schemas = new[]
            {
                new SchemaPhaseSchema
                {
                    Name = "dbo",
                    Objects = new[]
                    {
                        new SchemaPhaseObject { SchemaName = "dbo", ObjectName = "Customers", ObjectType = 0 },
                    },
                },
            },
        });
        var phaseBBlob = MessagePackSerializer.Serialize(new SchemaPhasePayload
        {
            DatabaseName = "db", Phase = 2, Checksum = "PhaseB:1",
        });

        var socket = new FakeBridgeWebSocket(frame =>
        {
            var envelope = MessagePackSerializer.Deserialize<RpcMessage>(frame);
            return envelope.MessageType switch
            {
                MessageTypes.HandshakeRequest => MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = MessageTypes.HandshakeResponse,
                    RequestId = envelope.RequestId,
                    Payload = MessagePackSerializer.Serialize(new HandshakeResponse
                    {
                        Status = HandshakeStatus.Ok, EngineVersion = "1.0.0", ChosenProtocolVersion = 1,
                    }),
                }),
                MessageTypes.SchemaChecksumRequest => MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = MessageTypes.SchemaChecksumResponse,
                    RequestId = envelope.RequestId,
                    Payload = MessagePackSerializer.Serialize(new SchemaChecksumResponse
                    {
                        HasConnection = true, Checksum = "PhaseA:1",
                        SessionId = "s1", ServerCanonicalIdentity = "s", DatabaseName = "db",
                    }),
                }),
                MessageTypes.SchemaPhaseARequest => MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = MessageTypes.SchemaPhaseAResponse,
                    RequestId = envelope.RequestId,
                    Payload = MessagePackSerializer.Serialize(new SchemaPhaseAResponse
                    {
                        HasConnection = true, PhaseA = phaseABlob, Checksum = "PhaseA:1",
                        SessionId = "s1", DatabaseName = "db",
                    }),
                }),
                MessageTypes.SchemaPhaseBRequest => MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = MessageTypes.SchemaPhaseBResponse,
                    RequestId = envelope.RequestId,
                    Payload = MessagePackSerializer.Serialize(new SchemaPhaseBResponse
                    {
                        HasConnection = true, PhaseB = phaseBBlob, Checksum = "PhaseB:1",
                        SessionId = "s1", DatabaseName = "db",
                    }),
                }),
                _ => MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = 0, RequestId = envelope.RequestId,
                }),
            };
        });

        var bridge = new EngineBridge(() => socket,
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));
        await bridge.ConnectAsync(
            new EngineConnection { Id = "c1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true },
            bearerToken: null, pairingPin: null, ct: CancellationToken.None);
        Assert.Equal(BridgeState.Open, bridge.State);

        var cache = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s", DatabaseName = "db", Checksum = "stale-checksum",
        });
        var sync = new SchemaSync(cache, bridge,
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

        await sync.PollNowAsync("s1", "s", "db");

        // PhaseB is fire-and-forget on a Task.Run; give it a moment to settle.
        for (int i = 0; i < 30; i++)
        {
            var probe = await cache.GetAsync("s", "db");
            if (probe?.PhaseA != null && probe.PhaseB != null) break;
            await Task.Delay(20);
        }

        var after = await cache.GetAsync("s", "db");
        Assert.NotNull(after);
        Assert.NotNull(after!.PhaseA);
        Assert.Equal(phaseABlob, after.PhaseA);
        Assert.NotNull(after.PhaseB);
        Assert.Equal(phaseBBlob, after.PhaseB);
        Assert.Equal("PhaseB:1", after.Checksum);
    }
}

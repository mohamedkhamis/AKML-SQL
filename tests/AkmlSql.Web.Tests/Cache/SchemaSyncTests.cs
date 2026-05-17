using System;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
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
}

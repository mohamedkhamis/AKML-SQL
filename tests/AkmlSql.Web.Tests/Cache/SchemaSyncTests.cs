using System;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Cache;

/// <summary>
/// Spec 021 (web edition) -- M5 task T108. SchemaSync surface:
/// StartAsync transitions to running, StopAsync stops the timer, ReportEditorActive
/// resets the idle clock. The poll cadence (30 s) and the idle threshold (5 min) are
/// driven by background timers we don't synchronously inspect -- the contract test
/// covers shape, the cadence test runs as a deferred Playwright check.
/// </summary>
public sealed class SchemaSyncTests
{
    private static (ISchemaSync sync, SchemaSync raw) Build()
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var diag = new DiagnosticsRingBuffer(db);
        var sync = new SchemaSync(cache, diag);
        return (sync, sync);
    }

    [Fact]
    public async Task StartAsync_marks_the_sync_as_running()
    {
        var (sync, raw) = Build();
        Assert.False(raw.IsRunning);
        await sync.StartAsync("server", "db");
        Assert.True(raw.IsRunning);
        await sync.StopAsync();
        Assert.False(raw.IsRunning);
    }

    [Fact]
    public async Task ReportEditorActive_updates_the_last_activity_timestamp()
    {
        var (sync, raw) = Build();
        await sync.StartAsync("s", "db");

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
        var (sync, raw) = Build();
        await sync.StartAsync("s", "db");
        await sync.StartAsync("s", "db");   // second start should not throw
        Assert.True(raw.IsRunning);
        await sync.StopAsync();
    }
}

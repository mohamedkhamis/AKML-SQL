using System;
using System.Threading.Tasks;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T051, HIS-02, FR-011/FR-012, data-model §2.3) — open state per query session, owned
/// by the shell process that opened it, and reconciled when a shell starts.
/// </summary>
public sealed class HistoryOpenStateTests : IAsyncLifetime
{
    private const int DeadPid = int.MaxValue - 1;
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync() => _db = await HistoryTestDb.CreateAsync("akml-open");

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task SetOpenStatus_by_session_opens_and_closes_every_run()
    {
        await _db.RunAsync("SELECT 1", "tab-A");
        await _db.RunAsync("SELECT 2", "tab-A");
        await _db.RunAsync("SELECT 3", "tab-B");

        await _db.Database.SetOpenStatusBySessionAsync("tab-A", true, 1234);
        Assert.Equal(2, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1 AND open_pid = 1234"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1 AND open_pid IS NULL"));

        await _db.Database.SetOpenStatusBySessionAsync("tab-A", false, 1234);
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1 OR open_pid IS NOT NULL"));
    }

    [Fact]
    public async Task Reconcile_closes_this_shells_sessions_that_are_no_longer_open()
    {
        await _db.RunAsync("SELECT 1", "tab-open");
        await _db.RunAsync("SELECT 2", "tab-gone");
        await _db.Database.SetOpenStatusBySessionAsync("tab-open", true, 500);
        await _db.Database.SetOpenStatusBySessionAsync("tab-gone", true, 500);

        await _db.Database.ReconcileOpenAsync(500, new[] { "tab-open" });

        Assert.Equal(1, await _db.CountAsync(
            "SELECT COUNT(*) FROM history h JOIN query_sessions qs ON qs.id = h.session_id WHERE h.is_open = 1 AND qs.session_key = 'tab-open'"));
        Assert.Equal(0, await _db.CountAsync(
            "SELECT COUNT(*) FROM history h JOIN query_sessions qs ON qs.id = h.session_id WHERE h.is_open = 1 AND qs.session_key = 'tab-gone'"));
    }

    [Fact]
    public async Task Reconcile_closes_sessions_of_dead_shells_and_returns_them_newest_first()
    {
        var older = await _db.RunAsync("SELECT old", "tab-crash-1");
        var newerFirst = await _db.RunAsync("SELECT new", "tab-crash-2");
        var newer = await _db.RunAsync("SELECT newer", "tab-crash-2");
        await _db.Database.SetOpenStatusBySessionAsync("tab-crash-1", true, DeadPid);
        await _db.Database.SetOpenStatusBySessionAsync("tab-crash-2", true, DeadPid);

        var restorable = await _db.Database.ReconcileOpenAsync(500, Array.Empty<string>());

        Assert.Equal(new[] { newer, older }, restorable);
        Assert.DoesNotContain(newerFirst, restorable);
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1"));
    }

    [Fact]
    public async Task A_dead_shells_pid_reused_by_another_program_does_not_keep_its_queries_open()
    {
        // SSMS 4120 crashed with the query open; after a reboot another program got PID 4120.
        var starts = new System.Collections.Generic.Dictionary<int, long?> { [4120] = 1000, [500] = 9000 };
        _db.Database.ProcessStartTicks = pid => starts.TryGetValue(pid, out var t) ? t : null;
        var run = await _db.RunAsync("SELECT 1", "tab-crash");
        await _db.Database.SetOpenStatusBySessionAsync("tab-crash", true, 4120);
        starts[4120] = 2000;

        var restorable = await _db.Database.ReconcileOpenAsync(500, Array.Empty<string>());

        Assert.Equal(new[] { run }, restorable);
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1"));
    }

    [Fact]
    public async Task A_new_ssms_given_a_dead_ones_pid_offers_its_queries_for_restore()
    {
        // The new SSMS itself got PID 4120: step 1 used to close the old one's rows as its own.
        var starts = new System.Collections.Generic.Dictionary<int, long?> { [4120] = 1000 };
        _db.Database.ProcessStartTicks = pid => starts.TryGetValue(pid, out var t) ? t : null;
        var run = await _db.RunAsync("SELECT 1", "tab-old");
        await _db.Database.SetOpenStatusBySessionAsync("tab-old", true, 4120);
        starts[4120] = 2000;

        var restorable = await _db.Database.ReconcileOpenAsync(4120, Array.Empty<string>());

        Assert.Equal(new[] { run }, restorable);
    }

    [Fact]
    public async Task The_same_process_is_still_the_owner()
    {
        var starts = new System.Collections.Generic.Dictionary<int, long?> { [4120] = 1000, [500] = 9000 };
        _db.Database.ProcessStartTicks = pid => starts.TryGetValue(pid, out var t) ? t : null;
        await _db.RunAsync("SELECT 1", "tab-live");
        await _db.Database.SetOpenStatusBySessionAsync("tab-live", true, 4120);

        Assert.Empty(await _db.Database.ReconcileOpenAsync(500, Array.Empty<string>()));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1 AND open_pid_started = 1000"));
    }

    [Fact]
    public async Task Reconcile_leaves_live_shells_and_web_rows_alone()
    {
        await _db.RunAsync("SELECT live", "tab-live");
        await _db.Database.SetOpenStatusBySessionAsync("tab-live", true, Environment.ProcessId);
        var web = await _db.RunAsync("SELECT web", "tab-web");
        await _db.ExecAsync($"UPDATE history SET is_open = 1, open_pid = NULL WHERE id = {web};");

        var restorable = await _db.Database.ReconcileOpenAsync(500, Array.Empty<string>());

        Assert.Empty(restorable);
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM history WHERE is_open = 1 AND open_pid = {Environment.ProcessId}"));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM history WHERE id = {web} AND is_open = 1 AND open_pid IS NULL"));
    }
}

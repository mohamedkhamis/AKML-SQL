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

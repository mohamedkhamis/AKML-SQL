using System;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// F1 fix (final review wave): version snapshots (tab-close / tab-focus-change auto-save) must be
/// found by <c>history.source</c>, not <c>history.tab_title</c>. Since this branch, the shell sends
/// <c>TabTitle</c> only for a document actually saved to disk (see
/// <c>ExecutionCapture.OnAfterCommandExecute</c>) — an unsaved SSMS scratch tab's <c>tab_title</c> is
/// NULL. Before this fix, <c>SaveVersionByTabTitleAsync</c> looked rows up by
/// <c>WHERE tab_title = @title</c>, which can never match a NULL column, so NOTHING was ever written
/// to <c>history_versions</c> for exactly the scratch tabs this feature exists to serve.
/// <see cref="HistoryDatabase.SaveVersionBySourceAsync"/> now looks the row up by <c>source</c>
/// (the document's full path), which is populated unconditionally for saved and unsaved documents
/// alike (see <c>ExecutionCapture.OnAfterCommandExecute</c>'s <c>source = activeDoc.FullName</c>).
/// </summary>
public class HistoryVersionSnapshotBySourceTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-verssrc-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_path);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
        return Task.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Snapshot_lands_in_history_versions_when_tab_title_is_null()
    {
        // Simulates an unsaved SSMS scratch tab: tab_title is NULL (the deliberate F1-predating
        // behaviour), but source (activeDoc.FullName) is always populated.
        const string source = @"C:\Users\someone\AppData\Local\Temp\dwnhdxfq.sql";
        var id = await _db.InsertEntryAsync(
            "SELECT 1", false, "localhost", "Northwind", null, 5, 1,
            (int)ExecutionStatus.Success, null,
            source: source, tabTitle: null, sessionKey: "tab-A");

        var saved = await _db.SaveVersionBySourceAsync(source, "SELECT 1; -- edited before tab close");

        Assert.True(saved);

        var versions = await _db.GetVersionsAsync(id);
        var version = Assert.Single(versions);
        Assert.Equal("SELECT 1; -- edited before tab close", version.SqlText);
    }

    [Fact]
    public async Task Snapshot_returns_false_when_no_row_matches_the_source()
    {
        var saved = await _db.SaveVersionBySourceAsync(@"C:\nope.sql", "SELECT 1");
        Assert.False(saved);
    }

    /// <summary>
    /// Finding 4 (PR #249 review): the target-row lookup must be immune to executed_at's mixed
    /// ISO/space format. This method REWRITES its target row's executed_at to the space format
    /// (via datetime('now')) every time it snapshots -- so the SECOND snapshot for a given source
    /// runs with one row already in the space format and one still in ISO. A raw
    /// `ORDER BY executed_at DESC` sorts any same-day space-format timestamp BELOW any ISO one,
    /// so the buggy version would pick the OLDER, never-snapshotted row instead of the newest.
    /// `id DESC` sidesteps the format entirely.
    /// </summary>
    [Fact]
    public async Task Second_snapshot_still_attaches_to_the_newest_row_after_the_first_rewrites_it_to_space_format()
    {
        const string source = @"C:\Reports\MonthlyReport.sql";

        var olderId = await _db.InsertEntryAsync(
            "SELECT old", false, "localhost", "Northwind", null, 5, 1,
            (int)ExecutionStatus.Success, null, source: source, tabTitle: "MonthlyReport.sql", sessionKey: "tab-old");

        var newerId = await _db.InsertEntryAsync(
            "SELECT new", false, "localhost", "Northwind", null, 5, 1,
            (int)ExecutionStatus.Success, null, source: source, tabTitle: "MonthlyReport.sql", sessionKey: "tab-new");

        Assert.True(newerId > olderId);

        // First snapshot: both rows are still ISO-format here, so even the buggy raw-string
        // ORDER BY happens to pick the right (newest) row -- this call rewrites the newest row's
        // executed_at to the SPACE format.
        Assert.True(await _db.SaveVersionBySourceAsync(source, "-- first edit"));
        Assert.Single(await _db.GetVersionsAsync(newerId));

        // Second snapshot: newerId is now space-format; olderId is still ISO-format. Must still
        // land on newerId, not fall back to the older, untouched entry.
        Assert.True(await _db.SaveVersionBySourceAsync(source, "-- second edit"));

        var newerVersions = await _db.GetVersionsAsync(newerId);
        Assert.Equal(2, newerVersions.Count);   // both edits landed on the newest entry

        var olderVersions = await _db.GetVersionsAsync(olderId);
        Assert.Empty(olderVersions);            // the older entry was never touched
    }
}

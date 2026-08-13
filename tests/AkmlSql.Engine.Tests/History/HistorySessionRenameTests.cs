using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Task 6 fix-round-1: <see cref="HistoryDatabase.UpdateTabTitleAsync"/> must rename the SESSION
/// (not just <c>history.tab_title</c>) when the target entry has one, because the deduplicated
/// search's display name now comes from <c>query_sessions.name</c> via a <c>LEFT JOIN</c> (see
/// <see cref="HistoryDatabase.SearchAsync"/>). Before this fix, a rename on a sessioned entry wrote
/// only <c>history.tab_title</c>, which the read path never consults once a session exists (Task
/// 5's backfill assigns one to every row) — the write would succeed and the new name would never
/// appear in the list. Every rename-related test that predates this file seeds sessionless rows,
/// so none of them would have caught this.
/// </summary>
public class HistorySessionRenameTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-rename-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_path);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        // Microsoft.Data.Sqlite pools the native handle per connection string, so the OS file
        // (and its WAL-mode sidecars) can still be open past our disposals. A bare File.Delete
        // throws IOException on Windows when that happens; best-effort delete instead, same
        // pattern as QuerySessionStoreTests.DisposeAsync.
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
        return Task.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private Task<long> Add(string sql, string? sessionKey, string? tabTitle = null) =>
        _db.InsertEntryAsync(sql, false, "localhost", "Northwind", null, 5, 1,
                     (int)ExecutionStatus.Success, null, null, tabTitle, sessionKey);

    private async Task<long?> GetSessionIdAsync(long entryId)
    {
        await using var c = new SqliteConnection($"Data Source={_path}");
        await c.OpenAsync();
        await using var cmd = new SqliteCommand("SELECT session_id FROM history WHERE id = @id", c);
        cmd.Parameters.AddWithValue("@id", entryId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
    }

    private async Task<int> GetNameSourceAsync(long sessionId)
    {
        await using var c = new SqliteConnection($"Data Source={_path}");
        await c.OpenAsync();
        await using var cmd = new SqliteCommand("SELECT name_source FROM query_sessions WHERE id = @id", c);
        cmd.Parameters.AddWithValue("@id", sessionId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rename_WithSession_UpdatesSessionName_AndListReflectsIt()
    {
        var id = await Add("SELECT 1", "tab-A"); // scratch session, auto-named "query-01"

        await _db.UpdateTabTitleAsync(id, "My Renamed Query");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 50 });
        var entry = Assert.Single(result.Entries);
        Assert.Equal("My Renamed Query", entry.TabTitle);
    }

    [Fact]
    public async Task Rename_WithSession_SetsNameSourceToManual()
    {
        var id = await Add("SELECT 1", "tab-A");
        var sessionId = await GetSessionIdAsync(id);
        Assert.NotNull(sessionId);

        await _db.UpdateTabTitleAsync(id, "My Renamed Query");

        Assert.Equal(2, await GetNameSourceAsync(sessionId!.Value)); // 2 = manual, never auto-overwritten
    }

    [Fact]
    public async Task Rename_WithSession_SurvivesALaterExecutionCarryingARealFilename()
    {
        var id = await Add("SELECT 1", "tab-A"); // scratch — auto-named
        await _db.UpdateTabTitleAsync(id, "My Renamed Query");

        // A later execution on the SAME session, this time carrying a real (non-scratch) filename.
        // QuerySessionStore.MaybeUpgradeNameAsync only upgrades a session whose name_source = 0
        // (auto); a manually-renamed session (name_source = 2) must NOT be touched.
        await Add("SELECT 2", "tab-A", tabTitle: "SomeRealFile.sql");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 50 });
        var entry = Assert.Single(result.Entries); // still one group — same session
        Assert.Equal("My Renamed Query", entry.TabTitle); // NOT overwritten by the real filename
        Assert.Equal(2, entry.ExecutionCount);
        Assert.Equal(2, entry.VersionCount);
    }

    [Fact]
    public async Task Rename_WithoutSession_FallsBackToPerRowTabTitleStamp()
    {
        // No sessionKey → session_id stays NULL → the pre-fix-round-1 fallback path must still work.
        var id = await Add("SELECT 3", null);

        await _db.UpdateTabTitleAsync(id, "Legacy Rename");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 50 });
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Legacy Rename", entry.TabTitle);
    }
}

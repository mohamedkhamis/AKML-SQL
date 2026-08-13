using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class HistorySessionSearchTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-search-{Guid.NewGuid():N}.db");
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

    private Task Add(string sql, string? sessionKey) =>
        _db.InsertEntryAsync(sql, false, "localhost", "Northwind", null, 5, 1,
                     (int)ExecutionStatus.Success, null, null, null, sessionKey);

    [Fact]
    public async Task One_row_per_session_with_run_and_version_counts()
    {
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 1", "tab-A");   // identical re-run
        await Add("SELECT 2", "tab-A");   // edited — same session, new version
        await Add("SELECT 9", "tab-B");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 50 });

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(2, result.TotalCount);  // count query must agree with the rows returned

        var a = result.Entries.Single(e => e.TabTitle == "query-01");
        Assert.Equal(3, a.ExecutionCount);   // three executions
        Assert.Equal(2, a.VersionCount);     // two distinct texts
    }

    [Fact]
    public async Task Raw_view_still_lists_every_execution()
    {
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 1", "tab-A");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = false, Limit = 50 });
        Assert.Equal(2, result.Entries.Count);   // storage unchanged
    }

    [Fact]
    public async Task Mixed_session_and_sessionless_rows_CountAndTotalCount_agree()
    {
        // Only coverage of the 'hash:' fallback arm of GroupKey: two rows share a session (one
        // group), and two rows have NO session (session_id IS NULL) with DIFFERENT sql text each,
        // so they fall back to per-content-hash grouping — one group per distinct hash. Total
        // distinct groups = 1 (session) + 2 (hash fallback) = 3, exercised in the SAME result set
        // so the two GroupKey arms are proven to coexist correctly, not just individually.
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 1", "tab-A");   // same session, re-run — folds into the session group
        await Add("SELECT 100", null);    // no session — its own hash-fallback group
        await Add("SELECT 200", null);    // no session, different text — another hash-fallback group

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 50 });

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(3, result.TotalCount);  // count query must agree with the rows returned

        var sessionGroup = result.Entries.Single(e => e.TabTitle == "query-01");
        Assert.Equal(2, sessionGroup.ExecutionCount);
        Assert.Equal(1, sessionGroup.VersionCount);

        var hashFallbackGroups = result.Entries.Where(e => e.TabTitle == string.Empty).ToList();
        Assert.Equal(2, hashFallbackGroups.Count);
        Assert.All(hashFallbackGroups, e =>
        {
            Assert.Equal(1, e.ExecutionCount);
            Assert.Equal(1, e.VersionCount);
        });
    }
}

using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T116, HIS-07/HIS-14) — the server and database lists behind History's filter menu,
/// and drafts: a query captured without being run is listed as "Not executed", never counted as a
/// run, and becomes a version of the query when the query is run later.
/// </summary>
public class HistoryFilterValuesAndDraftTests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync() => _db = await HistoryTestDb.CreateAsync("akml-filter-draft");

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Task<long> Draft(string sql, string key) =>
        _db.Database.InsertEntryAsync(sql, false, "(local)", "Northwind", null, 0, 0, (int)ExecutionStatus.NotExecuted, null,
            null, "Draft tab", sessionKey: key);

    [Fact]
    public async Task Filter_values_are_distinct_sorted_and_non_empty()
    {
        await _db.RunAsync("SELECT 1", "a", server: "srv-b", database: "Northwind");
        await _db.RunAsync("SELECT 2", "b", server: "srv-a", database: "Archive");
        await _db.RunAsync("SELECT 3", "c", server: "srv-b", database: "Northwind");
        await _db.RunAsync("SELECT 4", "d", server: "", database: "");

        var (servers, databases) = await _db.Database.GetFilterValuesAsync();

        Assert.Equal(new[] { "srv-a", "srv-b" }, servers);
        Assert.Equal(new[] { "Archive", "Northwind" }, databases);
    }

    [Fact]
    public async Task Filter_values_stop_at_500()
    {
        await _db.ExecAsync(@"
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 510)
            INSERT INTO history (sql_text, server, database_name, executed_at, duration_ms, row_count, status, content_hash)
            SELECT 'SELECT ' || i, printf('srv-%03d', i), 'Northwind', '2026-09-01T00:00:00.0000000Z', 1, 1, 0, 'h' || i FROM n;");

        var (servers, _) = await _db.Database.GetFilterValuesAsync();

        Assert.Equal(500, servers.Count);
        Assert.Equal("srv-001", servers[0]);
    }

    [Fact]
    public async Task A_draft_is_listed_but_not_counted_as_a_run()
    {
        await Draft("SELECT ProductName FROM dbo.Products;", "draft-only");

        Assert.Equal(3L, await _db.ScalarAsync("SELECT status FROM history"));
        var (entries, total) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true });
        var row = Assert.Single(entries);
        Assert.Equal(1, total);
        Assert.Equal((int)ExecutionStatus.NotExecuted, row.Status);
        Assert.Equal(0, row.ExecutionCount);
    }

    [Fact]
    public async Task A_later_run_replaces_the_draft_and_keeps_its_text_as_a_version()
    {
        await Draft("SELECT ProductName FROM dbo.Products;", "tab-1");
        var runId = await _db.RunAsync("SELECT ProductName, UnitPrice FROM dbo.Products;", "tab-1");

        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE status = 3"));
        var (entries, _) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true });
        var row = Assert.Single(entries);
        Assert.Equal(runId, row.Id);
        Assert.Equal(1, row.ExecutionCount);
        Assert.Equal(2, row.VersionCount);

        var versions = await _db.Database.GetVersionsForGroupAsync(runId);
        Assert.Contains(versions, v => v.SqlText == "SELECT ProductName FROM dbo.Products;");
    }

    [Fact]
    public async Task A_run_in_another_session_leaves_the_draft_alone()
    {
        await Draft("SELECT 1;", "tab-1");
        await _db.RunAsync("SELECT 2;", "tab-2");

        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE status = 3"));
    }
}

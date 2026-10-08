using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T053, HIS-02/HIS-04, FR-012) — with grouping on, the starred and open filters apply
/// to the grouped query (any run), and the total counts the groups the page shows.
/// </summary>
public sealed class HistoryGroupFiltersTests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-groupfilters");

        // tab-A: an older starred run, a newer unstarred run; open (one run open is enough).
        var aOld = await _db.RunAsync("SELECT a1", "tab-A");
        await _db.RunAsync("SELECT a2", "tab-A");
        await _db.ExecAsync($"UPDATE history SET is_favorite = 1 WHERE id = {aOld};");
        await _db.Database.SetOpenStatusBySessionAsync("tab-A", true, 1);

        // tab-B: closed, never starred.
        await _db.RunAsync("SELECT b1", "tab-B");
        await _db.RunAsync("SELECT b2", "tab-B");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Closed_filter_never_returns_a_group_with_an_open_run()
    {
        // Only one tab-A row is marked open when the older run is the only open one.
        await _db.ExecAsync("UPDATE history SET is_open = 0 WHERE id = (SELECT MAX(id) FROM history WHERE session_id = (SELECT id FROM query_sessions WHERE session_key = 'tab-A'));");

        var (entries, total) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, IsOpen = false, Limit = 100 });

        Assert.Equal(new[] { "tab-B" }, entries.Select(e => e.SessionKey).ToArray());
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Starred_filter_returns_a_group_whose_older_run_is_starred()
    {
        var (entries, total) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, FavoritesOnly = true, Limit = 100 });

        var row = Assert.Single(entries);
        Assert.Equal("tab-A", row.SessionKey);
        Assert.True(row.IsFavorite);
        Assert.Equal(2, row.ExecutionCount);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Total_counts_the_groups()
    {
        var (entries, total) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 100 });

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, total);
    }
}

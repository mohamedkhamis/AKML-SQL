using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T050, HIS-04, FR-014) — Delete, Favorite and Versions act on the whole grouped
/// query (every run in its query session), and the version count matches the row's.
/// </summary>
public sealed class HistoryGroupActionsTests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;
    private long _first, _second, _third;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-group");
        _first = await _db.RunAsync("SELECT alpha", "tab-A");
        _second = await _db.RunAsync("SELECT beta", "tab-A");
        _third = await _db.RunAsync("SELECT alpha", "tab-A");
        await _db.RunAsync("SELECT other", "tab-B");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Delete_group_removes_every_run_its_versions_and_its_session()
    {
        await _db.Database.InsertVersionAsync(_second, "SELECT beta -- edited");

        var deleted = await _db.Database.DeleteGroupAsync(_second);

        Assert.Equal(3, deleted);
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history_versions"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM query_sessions WHERE session_key = 'tab-A'"));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM query_sessions WHERE session_key = 'tab-B'"));
    }

    [Fact]
    public async Task Favorite_group_stars_every_run_then_a_second_toggle_clears_every_run()
    {
        Assert.True(await _db.Database.ToggleFavoriteGroupAsync(_first));
        Assert.Equal(3, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_favorite = 1"));

        await _db.RunAsync("SELECT alpha", "tab-A"); // a later, unstarred run

        Assert.False(await _db.Database.ToggleFavoriteGroupAsync(_third));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_favorite = 1"));
    }

    [Fact]
    public async Task Versions_are_distinct_newest_first_and_match_the_row_count()
    {
        var versions = await _db.Database.GetVersionsForGroupAsync(_first);

        Assert.Equal(new[] { "SELECT alpha", "SELECT beta" }, versions.Select(v => v.SqlText).ToArray());

        var (entries, _) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 100 });
        var row = entries.Single(e => e.SessionKey == "tab-A");
        Assert.Equal(versions.Count, row.VersionCount);
    }

    [Fact]
    public async Task Snapshots_count_as_versions_on_the_row_and_in_the_panel()
    {
        await _db.Database.InsertVersionAsync(_third, "SELECT gamma");

        var versions = await _db.Database.GetVersionsForGroupAsync(_first);
        Assert.Equal("SELECT gamma", versions[0].SqlText);
        Assert.Equal(3, versions.Count);

        var (entries, _) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 100 });
        Assert.Equal(3, entries.Single(e => e.SessionKey == "tab-A").VersionCount);
    }

    [Fact]
    public async Task Per_id_delete_still_deletes_one_row_and_reports_it()
    {
        Assert.Equal(1, await _db.Database.DeleteEntriesAsync(new[] { _first }));
        Assert.Equal(3, await _db.CountAsync("SELECT COUNT(*) FROM history"));
    }
}

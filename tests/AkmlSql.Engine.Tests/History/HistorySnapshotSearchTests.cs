using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T052, HIS-05, FR-015) — after a version snapshot replaces a row's text, search finds
/// the new text (not the replaced one), the row keeps an ISO "o" timestamp and a matching content
/// hash, and time ordering holds across rows written before and after.
/// </summary>
public sealed class HistorySnapshotSearchTests : IAsyncLifetime
{
    private const string Source = @"C:\Reports\Monthly.sql";
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync() => _db = await HistoryTestDb.CreateAsync("akml-snapsearch");

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Search_follows_the_snapshot_text()
    {
        await _db.RunAsync("SELECT alphaword FROM dbo.Orders", "tab-A", Source);

        Assert.True(await _db.Database.SaveVersionBySourceAsync(Source, "SELECT betaword FROM dbo.Orders"));

        var (found, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "betaword", Deduplicate = true, Limit = 100 });
        var (gone, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "alphaword", Deduplicate = true, Limit = 100 });
        Assert.Single(found);
        Assert.Empty(gone);
    }

    [Fact]
    public async Task Snapshot_keeps_the_timestamp_format_and_the_hash_in_step()
    {
        var id = await _db.RunAsync("SELECT 1", "tab-A", Source);

        await _db.Database.SaveVersionBySourceAsync(Source, "SELECT 2");

        Assert.Matches(new Regex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$"),
            (string)(await _db.ScalarAsync($"SELECT executed_at FROM history WHERE id = {id}"))!);
        Assert.Equal(HistoryDatabase.ComputeContentHash("SELECT 2"),
            (string)(await _db.ScalarAsync($"SELECT content_hash FROM history WHERE id = {id}"))!);
    }

    [Fact]
    public async Task Ordering_is_by_time_across_rows_written_before_and_after()
    {
        await _db.RunAsync("SELECT first", "tab-A", Source);
        await _db.RunAsync("SELECT second", "tab-B", @"C:\Other.sql");
        await _db.Database.SaveVersionBySourceAsync(Source, "SELECT first edited"); // tab-A is now the newest

        var (entries, _) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 100 });

        Assert.Equal(new[] { "tab-A", "tab-B" }, entries.Select(e => e.SessionKey).ToArray());
    }

    [Fact]
    public async Task A_session_key_finds_its_own_row_before_the_source()
    {
        var mine = await _db.RunAsync("SELECT mine", "tab-A", Source);
        await _db.RunAsync("SELECT theirs", "tab-B", Source); // newer row, same path

        Assert.True(await _db.Database.SaveVersionBySourceAsync(Source, "SELECT mine edited", sessionKey: "tab-A"));

        Assert.Equal("SELECT mine edited", (string)(await _db.ScalarAsync($"SELECT sql_text FROM history WHERE id = {mine}"))!);
    }
}

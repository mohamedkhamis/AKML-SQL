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
    public async Task Sql_prefix_searches_the_sql_text_only()
    {
        // "sql:orders" (help: "Only the SQL text") also found a query saved as orders.sql.
        await _db.RunAsync("SELECT 1", "by-path", source: @"C:\Reports\orders.sql");
        await _db.RunAsync("SELECT * FROM dbo.Orders", "by-sql");

        var (sqlOnly, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "orders", SqlOnly = true, Deduplicate = true, Limit = 100 });
        var (anywhere, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "orders", Deduplicate = true, Limit = 100 });

        Assert.Equal(new[] { "by-sql" }, sqlOnly.Select(e => e.SessionKey).ToArray());
        Assert.Equal(2, anywhere.Count);
    }

    [Fact]
    public async Task A_word_typed_part_way_finds_the_query()
    {
        // Search runs as the user types: "Produ" (and "dbo.Prod") must already find dbo.Products.
        await _db.RunAsync("SELECT ProductName FROM dbo.Products", "products");
        await _db.RunAsync("SELECT 1 AS other", "other");

        var (partWord, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "Produ", Deduplicate = true, Limit = 100 });
        var (qualified, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "dbo.Prod", SqlOnly = true, Deduplicate = true, Limit = 100 });

        Assert.Equal(new[] { "products" }, partWord.Select(e => e.SessionKey).ToArray());
        Assert.Equal(new[] { "products" }, qualified.Select(e => e.SessionKey).ToArray());
    }

    [Fact]
    public async Task A_word_with_no_letters_or_digits_is_matched_as_written()
    {
        // "=" became a full-text phrase that matches nothing, so "id = 5" found nothing.
        await _db.RunAsync("SELECT * FROM dbo.Orders WHERE id = 5", "eq");
        await _db.RunAsync("SELECT * FROM dbo.Orders WHERE id > 5", "gt");

        var (found, _) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "id = 5", Deduplicate = true, Limit = 100 });

        Assert.Equal(new[] { "eq" }, found.Select(e => e.SessionKey).ToArray());
    }

    [Fact]
    public async Task A_page_shortened_by_the_camelcase_filter_still_says_more_follow()
    {
        // 120 runs, every other one matching "PC": the first page of 100 keeps about 50.
        for (var i = 0; i < 120; i++)
            await _db.RunAsync(i % 2 == 0 ? $"SELECT ProductCategory_{i}" : $"SELECT other_{i}", "s" + i);
        var filter = new HistoryFilter { CamelCaseTokens = new[] { "PC" }, Deduplicate = true, Limit = 100 };

        var first = await _db.Database.SearchPageAsync(filter);
        filter.Offset = 100;
        var last = await _db.Database.SearchPageAsync(filter);

        Assert.True(first.Entries.Count < 100);
        Assert.True(first.HasMore);
        Assert.False(last.HasMore);
    }

    [Fact]
    public async Task Unchanged_text_adds_no_version()
    {
        // The autosave and each tab switch snapshot the open text; when it has not changed since
        // the run (or the last snapshot) there is nothing new to keep.
        var id = await _db.RunAsync("SELECT 1", "tab-A", Source);

        Assert.True(await _db.Database.SaveVersionBySourceAsync(Source, "SELECT 1"));
        Assert.True(await _db.Database.SaveVersionBySourceAsync(Source, "SELECT 2"));
        Assert.True(await _db.Database.SaveVersionBySourceAsync(Source, "SELECT 2"));

        Assert.Equal(1L, (long)(await _db.ScalarAsync($"SELECT COUNT(*) FROM history_versions WHERE history_id = {id}"))!);
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

    [Fact]
    public async Task A_session_with_no_rows_does_not_take_over_another_sessions_row_on_the_same_path()
    {
        // SSMS numbers SQLQueryN.sql from 1 again every start: yesterday's SQLQuery1.sql is not
        // today's, and a never-run tab's text must not overwrite it.
        var yesterday = await _db.RunAsync("SELECT * FROM Orders", "yesterday", Source);

        Assert.False(await _db.Database.SaveVersionBySourceAsync(Source, "DELETE FROM Staging", sessionKey: "today-never-run"));

        Assert.Equal("SELECT * FROM Orders", (string)(await _db.ScalarAsync($"SELECT sql_text FROM history WHERE id = {yesterday}"))!);
        Assert.Equal(0L, (long)(await _db.ScalarAsync($"SELECT COUNT(*) FROM history_versions WHERE history_id = {yesterday}"))!);
    }
}

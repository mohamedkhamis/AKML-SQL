using System.Diagnostics;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T124, SC-009) — History stays responsive at scale: 100,000 runs in about 20,000
/// query sessions. Machine-dependent, like PerformanceBaselineTests; filter out with
/// <c>--filter "Category!=Performance"</c> on a loaded machine. The timings run alone, after the
/// parallel tests: inside a full parallel run they measured the machine's load (500+ ms), not History.
/// </summary>
[Trait("Category", "Performance")]
[Collection(HistoryScaleCollection.Name)]
public sealed class HistoryScaleTests : IAsyncLifetime
{
    private const int Runs = 100_000;
    private const int Sessions = 20_000;
    private const long BudgetMs = 250;

    private readonly ITestOutputHelper _output;
    private HistoryTestDb _db = null!;

    public HistoryScaleTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-history-scale");
        await _db.ExecAsync($@"
            BEGIN;
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {Sessions})
            INSERT INTO query_sessions (session_key, local_date, ordinal, name, name_source, created_at)
            SELECT 'key-' || i, '2026-09-01', i, 'Report ' || i || '.sql', 1, '2026-09-01T00:00:00.0000000Z' FROM n;

            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {Runs})
            INSERT INTO history (sql_text, server, database_name, executed_at, duration_ms, row_count, status, content_hash, session_id, source)
            SELECT 'SELECT OrderID, CustomerID FROM dbo.Orders WHERE OrderID = ' || i || ';',
                   'srv-' || (i % 7), 'Northwind',
                   strftime('%Y-%m-%dT%H:%M:%S.0000000Z', '2026-09-01', '+' || (i % 20000) || ' minutes'),
                   5, 1, 0, 'hash-' || (i % 50000), 1 + (i % {Sessions}), 'C:\Reports\r' || (i % {Sessions}) || '.sql'
              FROM n;
            COMMIT;");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<(long Ms, int Count, int Total)> TimeAsync(HistoryFilter filter)
    {
        await _db.Database.SearchAsync(filter); // warm-up
        var sw = Stopwatch.StartNew();
        var (entries, total) = await _db.Database.SearchAsync(filter);
        sw.Stop();
        return (sw.ElapsedMilliseconds, entries.Count, total);
    }

    [Fact]
    public async Task A_grouped_page_and_its_count_come_back_quickly()
    {
        var (ms, count, total) = await TimeAsync(new HistoryFilter { Deduplicate = true, Limit = 100 });
        _output.WriteLine($"grouped page: {ms} ms, {count} rows of {total}");

        Assert.Equal(100, count);
        Assert.Equal(Sessions, total);
        Assert.True(ms < BudgetMs, $"grouped page took {ms} ms (budget {BudgetMs} ms)");
    }

    [Fact]
    public async Task Paging_reaches_the_last_page()
    {
        const int page = 1000;
        var loaded = 0;
        int total;
        int got;
        do
        {
            (var entries, total) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = page, Offset = loaded });
            got = entries.Count;
            loaded += got;
        }
        while (got == page && loaded < total); // HistoryViewModel.HasMoreEntries

        Assert.Equal(total, loaded);
        Assert.Equal(Sessions, loaded);
    }

    [Fact]
    public async Task A_free_text_search_over_names_and_servers_comes_back_quickly()
    {
        var (ms, count, total) = await TimeAsync(new HistoryFilter { Deduplicate = true, Limit = 100, SearchText = "Report srv-3" });
        _output.WriteLine($"free-text search: {ms} ms, {count} rows of {total}");

        Assert.True(total > 0);
        Assert.True(ms < BudgetMs, $"free-text search took {ms} ms (budget {BudgetMs} ms)");
    }
}

/// <summary>Runs the History scale timings on their own, after every parallel test.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HistoryScaleCollection
{
    public const string Name = "History scale (not parallel)";
}

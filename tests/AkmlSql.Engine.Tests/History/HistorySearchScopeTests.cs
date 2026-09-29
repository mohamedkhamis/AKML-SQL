using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T115, HIS-07, data-model §2.4) — History search finds a query by its name, source
/// path, server or database as well as by its SQL; every word must match somewhere; path and date
/// filters narrow it; a malformed full-text query still finds matches; and the grouped count stays
/// right.
/// </summary>
public class HistorySearchScopeTests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-search-scope");
        await Insert("SELECT CustomerID FROM dbo.Customers;", "k-sales", "Monthly sales", source: @"C:\Reports\sales.sql", server: "srv-east");
        await Insert("SELECT OrderID FROM dbo.Orders;", "k-orders", "Order check", source: @"C:\Scratch\orders.sql", server: "srv-west");
        await Insert("SELECT CustomerID FROM dbo.Customers WHERE Country = 'UK';", "k-uk", "UK list", source: null, server: "srv-west", database: "Archive");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Task<long> Insert(string sql, string key, string title, string? source, string server, string database = "Northwind") =>
        _db.Database.InsertEntryAsync(sql, false, server, database, null, 5, 1, (int)ExecutionStatus.Success, null,
            source, title, sessionKey: key);

    private async Task<string[]> Titles(HistoryFilter filter)
    {
        var (entries, _) = await _db.Database.SearchAsync(filter);
        return entries.Select(e => e.TabTitle ?? "").OrderBy(t => t).ToArray();
    }

    [Theory]
    [InlineData("Monthly", "Monthly sales")]   // the query's name
    [InlineData("Reports", "Monthly sales")]   // its source path
    [InlineData("srv-east", "Monthly sales")]  // its server
    [InlineData("Archive", "UK list")]         // its database
    [InlineData("Orders", "Order check")]      // its SQL, through the full-text index
    public async Task Free_text_matches_names_paths_servers_databases_and_sql(string text, string expected)
        => Assert.Equal(new[] { expected }, await Titles(new HistoryFilter { SearchText = text }));

    [Fact]
    public async Task Every_word_must_match_somewhere()
    {
        // "Customers" is in two queries' SQL; only one of them ran on srv-east.
        Assert.Equal(new[] { "Monthly sales" }, await Titles(new HistoryFilter { SearchText = "Customers srv-east" }));
        Assert.Empty(await Titles(new HistoryFilter { SearchText = "Customers srv-nowhere" }));
    }

    [Fact]
    public async Task Path_filter_limits_to_the_source()
    {
        Assert.Equal(new[] { "Monthly sales" }, await Titles(new HistoryFilter { PathFilter = "Reports" }));
        Assert.Equal(new[] { "Order check" }, await Titles(new HistoryFilter { PathFilter = @"scratch\ORDERS" }));
    }

    [Fact]
    public async Task Dates_filter_on_the_moment_whatever_the_offset()
    {
        await _db.ExecAsync("UPDATE history SET executed_at = '2026-09-10T08:00:00.0000000Z' WHERE tab_title = 'Monthly sales';");
        await _db.ExecAsync("UPDATE history SET executed_at = '2026-09-20T08:00:00.0000000Z' WHERE tab_title = 'Order check';");
        await _db.ExecAsync("UPDATE history SET executed_at = '2026-09-25T08:00:00.0000000Z' WHERE tab_title = 'UK list';");

        var from = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.FromHours(3)).UtcDateTime;
        var to = new DateTimeOffset(2026, 9, 20, 11, 0, 0, TimeSpan.FromHours(3)).UtcDateTime; // 08:00Z
        Assert.Equal(new[] { "Order check" }, await Titles(new HistoryFilter { DateFrom = from, DateTo = to }));

        // A local-time bound means the same instant as its UTC equivalent.
        var localFrom = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(new[] { "Order check", "UK list" }, await Titles(new HistoryFilter { DateFrom = localFrom }));
    }

    [Theory]
    [InlineData("Orders OR")]
    [InlineData("Orders NOT")]
    [InlineData("\"Orders")]
    public async Task A_malformed_full_text_query_still_matches(string text)
        => Assert.Contains("Order check", await Titles(new HistoryFilter { SearchText = text }));

    [Fact]
    public async Task Grouped_total_counts_groups_matched_by_metadata()
    {
        await Insert("SELECT CustomerID FROM dbo.Customers;", "k-sales", "Monthly sales", @"C:\Reports\sales.sql", "srv-east");
        await Insert("SELECT 2;", "k-sales", "Monthly sales", @"C:\Reports\sales.sql", "srv-east");

        var (entries, total) = await _db.Database.SearchAsync(new HistoryFilter { SearchText = "srv", Deduplicate = true });

        Assert.Equal(3, total);
        Assert.Equal(3, entries.Count);
        Assert.Equal(3, entries.Single(e => e.TabTitle == "Monthly sales").ExecutionCount);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionStoreTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private string _cs = string.Empty;
    private QuerySessionStore _store = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-qs-{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_path}";
        await new HistoryDatabase(_path).InitializeAsync();
        _store = new QuerySessionStore(_cs);
    }

    public Task DisposeAsync()
    {
        // Best-effort: Microsoft.Data.Sqlite pools the native handle per connection string, so
        // the OS file can still be open past our `await using` disposals. Deliberately NOT
        // calling SqliteConnection.ClearAllPools() here — it is process-global and would close
        // pooled connections held by other engine tests running in parallel (see the identical
        // precedent + rationale in HistoryDedupRepresentativeTests.Dispose). A still-pooled temp
        // file is harmless; the OS reclaims %TEMP% eventually.
        try { File.Delete(_path); } catch { /* ignore */ }
        return Task.CompletedTask;
    }

    private async Task<(string Name, int NameSource, string LocalDate)> Read(long id)
    {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT name, name_source, local_date FROM query_sessions WHERE id=@id", c);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetString(0), r.GetInt32(1), r.GetString(2));
    }

    [Fact]
    public async Task Same_key_returns_same_session()
    {
        var now = DateTime.UtcNow;
        var a = await _store.GetOrCreateAsync("key-A", now, null, "localhost", "Northwind");
        var b = await _store.GetOrCreateAsync("key-A", now, null, "localhost", "Northwind");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Ordinals_increment_within_a_day_and_reset_on_the_next()
    {
        // 10:00 and 11:00 LOCAL on one day, then 10:00 LOCAL the next.
        var day1 = DateTime.SpecifyKind(DateTime.Today.AddHours(10), DateTimeKind.Local).ToUniversalTime();
        var day1b = DateTime.SpecifyKind(DateTime.Today.AddHours(11), DateTimeKind.Local).ToUniversalTime();
        var day2 = DateTime.SpecifyKind(DateTime.Today.AddDays(1).AddHours(10), DateTimeKind.Local).ToUniversalTime();

        var s1 = await _store.GetOrCreateAsync("k1", day1, null, null, null);
        var s2 = await _store.GetOrCreateAsync("k2", day1b, null, null, null);
        var s3 = await _store.GetOrCreateAsync("k3", day2, null, null, null);

        Assert.Equal("query-01", (await Read(s1)).Name);
        Assert.Equal("query-02", (await Read(s2)).Name);
        Assert.Equal("query-01", (await Read(s3)).Name);   // counter reset
    }

    [Fact]
    public async Task Real_file_name_wins_over_auto_name()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("MonthlyReport.sql", row.Name);
        Assert.Equal(1, row.NameSource);
    }

    [Fact]
    public async Task Scratch_title_is_auto_named()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, "dwnhdxfq.sql", null, null);
        var row = await Read(id);
        Assert.Equal("query-01", row.Name);
        Assert.Equal(0, row.NameSource);
    }

    [Fact]
    public async Task File_name_upgrades_an_auto_named_session()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, null, null, null);
        Assert.Equal(0, (await Read(id)).NameSource);

        await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("MonthlyReport.sql", row.Name);
        Assert.Equal(1, row.NameSource);
    }

    [Fact]
    public async Task Manual_rename_is_never_overwritten()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, null, null, null);

        await using (var c = new SqliteConnection(_cs))
        {
            await c.OpenAsync();
            await using var cmd = new SqliteCommand(
                "UPDATE query_sessions SET name='Germany customers', name_source=2 WHERE id=@id", c);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // A later execution carrying a real file name must NOT clobber the manual name.
        await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("Germany customers", row.Name);
        Assert.Equal(2, row.NameSource);
    }

    [Fact]
    public async Task Concurrent_creation_never_duplicates_an_ordinal()
    {
        var now = DateTime.UtcNow;
        var ids = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => _store.GetOrCreateAsync($"concurrent-{i}", now, null, null, null)));

        Assert.Equal(12, ids.Distinct().Count());

        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*), COUNT(DISTINCT ordinal) FROM query_sessions WHERE local_date=@d", c);
        cmd.Parameters.AddWithValue("@d", QuerySessionNamerProbe.LocalDate(now));
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(r.GetInt32(0), r.GetInt32(1));   // every ordinal unique
    }
}

/// <summary>Test-only shim so the test can compute the same local-date key the store uses.</summary>
internal static class QuerySessionNamerProbe
{
    internal static string LocalDate(DateTime utc) => QuerySessionNamer.LocalDateKey(utc);
}

using AkmlSql.Site.Analytics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T008: the feature adds columns (ip, visitor_id, consent, release_version) to a database
/// that already holds months of live history — 3,123 visits and 43 downloads on the deployed site at
/// the time of writing. These tests pin the property that makes that safe: the migration is additive
/// and idempotent, existing rows keep every value they had, and rows written before the columns
/// existed simply carry NULL rather than being reset or backfilled with invented data.
/// </summary>
public sealed class SchemaMigrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static string DbPath(TempDirectory dir) => Path.Combine(dir.Path, "analytics.db");

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static IReadOnlyList<string> IndexNames(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND name IS NOT NULL;";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public void InitializeSchema_AddsTheFeature038ColumnsToVisits()
    {
        using var dir = new TempDirectory();

        using (var store = new AnalyticsStore(DbPath(dir)))
        {
            store.LogVisit(new VisitInfo(Now, "/download", null, "Chrome", "203.0.113.7"));
        }

        var columns = ColumnNames(DbPath(dir), "visits");

        Assert.Contains("ip", columns);
        Assert.Contains("visitor_id", columns);
        Assert.Contains("consent", columns);

        // The pre-existing privacy columns must survive: ip_hash still drives session grouping and
        // per-day unique counting, ip_prefix is the network grouping key and the only network value
        // available for a visitor who did not consent.
        Assert.Contains("ip_hash", columns);
        Assert.Contains("ip_prefix", columns);
    }

    [Fact]
    public void InitializeSchema_AddsTheFeature038ColumnsToDownloads()
    {
        using var dir = new TempDirectory();

        using (var store = new AnalyticsStore(DbPath(dir)))
        {
            store.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-1.26.0910.2248.exe", null, "Chrome", "203.0.113.7"));
        }

        var columns = ColumnNames(DbPath(dir), "downloads");

        Assert.Contains("ip", columns);
        Assert.Contains("visitor_id", columns);
        Assert.Contains("consent", columns);
        Assert.Contains("release_version", columns);
        Assert.Contains("ip_hash", columns);
        Assert.Contains("ip_prefix", columns);
    }

    [Fact]
    public void InitializeSchema_CreatesTheFeature038Indexes()
    {
        using var dir = new TempDirectory();

        using var store = new AnalyticsStore(DbPath(dir));

        var indexes = IndexNames(DbPath(dir));

        Assert.Contains("ix_visits_visitor", indexes);
        Assert.Contains("ix_downloads_visitor", indexes);
        Assert.Contains("ix_downloads_day_country", indexes);
        Assert.Contains("ix_visits_day_country", indexes);

        // Existing indexes are kept, not replaced.
        Assert.Contains("ix_visits_day", indexes);
        Assert.Contains("ix_downloads_day", indexes);
    }

    [Fact]
    public void Reopen_PreservesExistingRowsAndLeavesNewColumnsNull()
    {
        using var dir = new TempDirectory();

        // First run: write history, exactly as the deployed site has been doing for months.
        using (var first = new AnalyticsStore(DbPath(dir)))
        {
            first.LogVisit(new VisitInfo(Now, "/download", "google.com", "Chrome", "203.0.113.7"));
            first.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-1.26.0910.2248.exe", "google.com", "Chrome", "203.0.113.7"));
        }

        // Second run: a redeploy re-runs InitializeSchema against the SAME file.
        using (var second = new AnalyticsStore(DbPath(dir)))
        {
            var summary = second.GetSummary(30, Now);

            // The history is intact — the migration must never reset the database.
            Assert.Equal(1, summary.VisitsToday);
            Assert.Equal(1, summary.DownloadsTotal);
            Assert.Equal("/download", Assert.Single(summary.TopPages).Key);
        }

        // Rows written before the columns existed carry NULL. Data-model §7: they aggregate as
        // unattributed and never resolve to an individual — that is correct, not a gap to backfill.
        Assert.Null(ScalarOrNull(DbPath(dir), "SELECT ip FROM visits LIMIT 1;"));
        Assert.Null(ScalarOrNull(DbPath(dir), "SELECT visitor_id FROM visits LIMIT 1;"));
        Assert.Null(ScalarOrNull(DbPath(dir), "SELECT ip FROM downloads LIMIT 1;"));
        Assert.Null(ScalarOrNull(DbPath(dir), "SELECT visitor_id FROM downloads LIMIT 1;"));

        // The pre-existing privacy columns were still written for that row.
        Assert.NotNull(ScalarOrNull(DbPath(dir), "SELECT ip_hash FROM visits LIMIT 1;"));
        Assert.Equal("203.0.113.0", ScalarOrNull(DbPath(dir), "SELECT ip_prefix FROM visits LIMIT 1;"));
    }

    [Fact]
    public void InitializeSchema_IsIdempotentAcrossRepeatedOpens()
    {
        using var dir = new TempDirectory();

        using (var first = new AnalyticsStore(DbPath(dir)))
        {
            first.LogVisit(new VisitInfo(Now, "/", null, "Chrome", "203.0.113.7"));
        }

        var afterFirst = ColumnNames(DbPath(dir), "visits");

        // Two further opens: a redeploy, then an app-pool recycle. Neither may duplicate a column
        // (SQLite would throw on a duplicate ADD COLUMN) or drop data.
        using (var second = new AnalyticsStore(DbPath(dir)))
        {
        }

        using (var third = new AnalyticsStore(DbPath(dir)))
        {
            Assert.Equal(1, third.GetSummary(30, Now).VisitsToday);
        }

        Assert.Equal(afterFirst, ColumnNames(DbPath(dir), "visits"));
    }

    private static object? ScalarOrNull(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : value;
    }
}

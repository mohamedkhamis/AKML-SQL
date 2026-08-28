using AkmlSql.Site.Analytics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// AnalyticsStore against a temp-file SQLite database: schema creation, log/query round-trips
/// (totals, per-page, by-file, daily series, referrer aggregation), and the privacy contract —
/// salt persistence across reopen, per-install salt uniqueness, no raw IPs at rest.
/// </summary>
public sealed class AnalyticsStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static AnalyticsStore NewStore(TempDirectory dir) => new(Path.Combine(dir.Path, "analytics.db"));

    private static VisitInfo Visit(int daysAgo, string path, string? referrer = null, string? ua = null, string ip = "203.0.113.7") =>
        new(Now.AddDays(-daysAgo), path, referrer, ua, ip);

    [Fact]
    public void Constructor_CreatesDatabaseSaltAndWorkingSchema()
    {
        using var dir = new TempDirectory();

        using var store = NewStore(dir);

        Assert.True(File.Exists(Path.Combine(dir.Path, "analytics.db")));
        var saltBytes = File.ReadAllBytes(Path.Combine(dir.Path, "salt.bin"));
        Assert.True(saltBytes.Length >= 16);

        // Schema is live: a write + read round-trips without further setup.
        store.LogVisit(Visit(0, "/"));
        Assert.Equal(1, store.GetSummary(30, Now).VisitsToday);
    }

    [Fact]
    public void RoundTrip_TotalsPerPageByFileDailySeriesAndReferrers()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/", "example.com", "Chrome"));
        store.LogVisit(Visit(0, "/", "example.com", "Chrome"));
        store.LogVisit(Visit(0, "/features"));                       // no referrer
        store.LogVisit(Visit(1, "/docs", "github.com", "Firefox"));
        store.LogVisit(Visit(10, "/", "example.com", "Edge"));
        store.LogVisit(Visit(40, "/old", "example.com", "curl")); // outside the 30d window

        store.LogDownload(new DownloadInfo(Now, "setup-1.1.0.exe", null, "Chrome", "198.51.100.2"));
        store.LogDownload(new DownloadInfo(Now.AddDays(-3), "setup-1.0.0.exe", null, "Edge", "198.51.100.3"));
        store.LogDownload(new DownloadInfo(Now.AddDays(-3), "setup-1.0.0.exe", null, "Edge", "198.51.100.3"));
        store.LogDownload(new DownloadInfo(Now.AddDays(-40), "setup-1.1.0.exe", null, "curl", "198.51.100.4"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(30, summary.Days);
        Assert.Equal(3, summary.VisitsToday);
        Assert.Equal(4, summary.VisitsLast7Days);      // 3 today + 1 yesterday
        Assert.Equal(5, summary.VisitsWindow);         // excludes the 40-day-old visit
        Assert.Equal(4, summary.DownloadsTotal);       // all-time, includes the 40-day-old one
        Assert.Equal(3, summary.DownloadsLast7Days);

        Assert.Equal(new CountRow("/", 3), summary.TopPages[0]);
        Assert.Contains(summary.TopPages, r => r.Key == "/features" && r.Count == 1);
        Assert.Contains(summary.TopPages, r => r.Key == "/docs" && r.Count == 1);
        Assert.DoesNotContain(summary.TopPages, r => r.Key == "/old");

        Assert.Equal(2, summary.DownloadsByFile.Count);
        Assert.Contains(summary.DownloadsByFile, r => r.Key == "setup-1.1.0.exe" && r.Count == 2);
        Assert.Contains(summary.DownloadsByFile, r => r.Key == "setup-1.0.0.exe" && r.Count == 2);

        Assert.Equal(new CountRow("example.com", 3), summary.TopReferrers[0]);
        Assert.Equal(new CountRow("github.com", 1), summary.TopReferrers[1]);

        // Daily series: exactly `days` entries, oldest first, zero-filled, anchored at today.
        Assert.Equal(30, summary.DailyVisits.Count);
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime), summary.DailyVisits[^1].Day);
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-29), summary.DailyVisits[0].Day);
        Assert.Equal(3, summary.DailyVisits[^1].Count);
        Assert.Equal(1, summary.DailyVisits[^2].Count);
        Assert.Equal(0, summary.DailyVisits[^3].Count);
    }

    [Fact]
    public void IpHash_NeverStoresRawIp_AndRotatesByDay()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "analytics.db");
        using (var store = NewStore(dir))
        {
            store.LogVisit(Visit(0, "/", ip: "203.0.113.99"));
        }

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ip_hash FROM visits;";
        var hash = Assert.IsType<string>(command.ExecuteScalar());

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("203.0.113.99", hash, StringComparison.OrdinalIgnoreCase);

        // Same IP on a different day hashes differently (per-day unlinkability).
        using var store2 = NewStore(dir);
        var today = store2.ComputeIpHash("203.0.113.99", DateOnly.FromDateTime(Now.UtcDateTime));
        var tomorrow = store2.ComputeIpHash("203.0.113.99", DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1));
        Assert.Equal(hash, today);
        Assert.NotEqual(today, tomorrow);
    }

    [Fact]
    public void Salt_PersistsAcrossReopen()
    {
        using var dir = new TempDirectory();
        var day = new DateOnly(2026, 8, 28);

        string first;
        using (var store = NewStore(dir))
        {
            first = store.ComputeIpHash("203.0.113.7", day);
        }

        using var reopened = NewStore(dir);
        Assert.Equal(first, reopened.ComputeIpHash("203.0.113.7", day));
    }

    [Fact]
    public void Salt_DiffersBetweenInstallations()
    {
        using var dirA = new TempDirectory();
        using var dirB = new TempDirectory();
        var day = new DateOnly(2026, 8, 28);

        using var storeA = NewStore(dirA);
        using var storeB = NewStore(dirB);

        Assert.NotEqual(
            storeA.ComputeIpHash("203.0.113.7", day),
            storeB.ComputeIpHash("203.0.113.7", day));
    }

    [Fact]
    public void GetSummary_EmptyDatabase_ReturnsZeroFilledSeries()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        var summary = store.GetSummary(7, Now);

        Assert.Equal(7, summary.DailyVisits.Count);
        Assert.All(summary.DailyVisits, d => Assert.Equal(0, d.Count));
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime), summary.DailyVisits[^1].Day);
        Assert.Equal(0, summary.VisitsToday);
        Assert.Equal(0, summary.DownloadsTotal);
        Assert.Empty(summary.TopPages);
        Assert.Empty(summary.TopReferrers);
        Assert.Empty(summary.DownloadsByFile);
    }

    [Fact]
    public void ResolveDatabasePath_DefaultsToProgramData()
    {
        var resolved = AnalyticsStore.ResolveDatabasePath(null);

        Assert.EndsWith(Path.Combine("AKML SQL Site", "analytics.db"), resolved);
        Assert.True(Path.IsPathRooted(resolved));
    }

    [Fact]
    public void ResolveDatabasePath_ExpandsEnvironmentVariables()
    {
        var resolved = AnalyticsStore.ResolveDatabasePath(Path.Combine("%TEMP%", "akml-metrics-test", "a.db"));

        Assert.DoesNotContain("%TEMP%", resolved);
        Assert.True(Path.IsPathRooted(resolved));
    }
}

using AkmlSql.Site.Analytics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Phase 4 store behaviour: bot exclusion (ADM-001), unique visitors and browser mix from data
/// that was already being recorded (ADM-002), the downloads series (ADM-005), the indexed day
/// column and retention prune (ADM-004), and the CSV export (ADM-007).
/// </summary>
public sealed class AnalyticsInsightsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static AnalyticsStore NewStore(TempDirectory dir) => new(Path.Combine(dir.Path, "analytics.db"));

    /// <summary>VisitInfo.ReferrerHost is already a HOST -- the middleware extracts it from the
    /// Referer header before the store ever sees it -- so pass a bare host, not a URL.</summary>
    private static VisitInfo Visit(int daysAgo, string path, string? ua = "Chrome", string ip = "203.0.113.7", string? referrer = null) =>
        new(Now.AddDays(-daysAgo), path, referrer, ua, ip);

    // --- ADM-001: bots ------------------------------------------------------

    [Fact]
    public void BotVisits_AreExcludedFromEveryVisitorFigure_ButCountedSeparately()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/"));
        store.LogVisit(Visit(0, "/crawled", ua: "bot", ip: "198.51.100.1"));
        store.LogVisit(Visit(0, "/crawled", ua: "bot", ip: "198.51.100.2"));
        store.LogVisit(Visit(2, "/docs", ua: "bot", ip: "198.51.100.3"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(1, summary.VisitsToday);
        Assert.Equal(1, summary.VisitsLast7Days);
        Assert.Equal(1, summary.VisitsWindow);
        Assert.Equal(3, summary.AutomatedVisitsWindow);

        // The crawled path must not appear in top pages, the daily series, or the browser mix.
        Assert.DoesNotContain(summary.TopPages, r => r.Key == "/crawled");
        Assert.DoesNotContain(summary.BrowserMix, r => r.Key == "bot");
        Assert.Equal(1, summary.DailyVisits.Sum(d => d.Count));
    }

    [Fact]
    public void BotDownloads_AreExcludedFromDownloadFigures()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "Chrome", "203.0.113.1"));
        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "bot", "198.51.100.1"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(1, summary.DownloadsTotal);
        Assert.Equal(1, summary.DownloadsWindow);
        Assert.Equal(1, summary.DownloadsByFile.Single().Count);
    }

    // --- ADM-002: uniques + browsers ---------------------------------------

    [Fact]
    public void UniqueVisitors_CountDistinctIpHashes_NotPageViews()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // One visitor reading three pages, plus a second visitor reading one.
        store.LogVisit(Visit(0, "/", ip: "203.0.113.1"));
        store.LogVisit(Visit(0, "/docs", ip: "203.0.113.1"));
        store.LogVisit(Visit(0, "/features", ip: "203.0.113.1"));
        store.LogVisit(Visit(0, "/", ip: "203.0.113.2"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(4, summary.VisitsToday);
        Assert.Equal(2, summary.UniqueVisitorsToday);
        Assert.Equal(2, summary.DailyUniqueVisitors[^1].Count);
    }

    [Fact]
    public void BrowserMix_AggregatesTheRecordedUserAgentFamilies()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/", ua: "Chrome"));
        store.LogVisit(Visit(1, "/", ua: "Chrome"));
        store.LogVisit(Visit(1, "/", ua: "Firefox"));
        store.LogVisit(Visit(2, "/", ua: null)); // unknown UA buckets as "other"

        var mix = store.GetSummary(30, Now).BrowserMix;

        Assert.Equal("Chrome", mix[0].Key);
        Assert.Equal(2, mix[0].Count);
        Assert.Contains(mix, r => r.Key == "Firefox" && r.Count == 1);
        Assert.Contains(mix, r => r.Key == "other" && r.Count == 1);
    }

    // --- ADM-005: downloads series -----------------------------------------

    [Fact]
    public void DailyDownloads_AreZeroFilledAcrossTheWindow()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "Chrome", "203.0.113.1"));
        store.LogDownload(new DownloadInfo(Now.AddDays(-2), "setup.exe", null, "Chrome", "203.0.113.2"));

        var series = store.GetSummary(7, Now).DailyDownloads;

        Assert.Equal(7, series.Count);
        Assert.Equal(1, series[^1].Count); // today
        Assert.Equal(0, series[^2].Count); // yesterday, quiet
        Assert.Equal(1, series[^3].Count);
    }

    // --- ADM-004: indexed day column + retention ---------------------------

    [Fact]
    public void DayColumn_IsPopulatedAndIndexed()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");
        using (var store = new AnalyticsStore(path))
        {
            store.LogVisit(Visit(0, "/"));
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT day FROM visits LIMIT 1;";
            Assert.Equal("2026-08-28", command.ExecuteScalar() as string);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_visits_day';";
            Assert.Equal(1L, command.ExecuteScalar());
        }

        // The old index on utc could never serve substr(utc,1,10) >= ? and is gone.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_visits_utc';";
            Assert.Equal(0L, command.ExecuteScalar());
        }
    }

    [Fact]
    public void ExistingDatabase_WithoutTheDayColumn_IsUpgradedAndBackfilled()
    {
        // An installed database predates the day column; opening it must migrate, not fail.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE visits (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, path TEXT NOT NULL,
                    referrer_host TEXT NULL, ua_family TEXT NULL, ip_hash TEXT NOT NULL);
                CREATE TABLE downloads (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, file TEXT NOT NULL,
                    referrer_host TEXT NULL, ua_family TEXT NULL, ip_hash TEXT NOT NULL);
                INSERT INTO visits (utc, path, ua_family, ip_hash)
                VALUES ('2026-08-28T09:00:00.0000000Z', '/legacy', 'Chrome', 'deadbeef');
                """;
            command.ExecuteNonQuery();
        }

        using var store = new AnalyticsStore(path);
        var summary = store.GetSummary(30, Now);

        // The pre-existing row is visible through the new day-based queries.
        Assert.Equal(1, summary.VisitsToday);
        Assert.Contains(summary.TopPages, r => r.Key == "/legacy");
    }

    [Fact]
    public void Prune_RemovesRowsOlderThanRetention_AndKeepsTheRest()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/keep"));
        store.LogVisit(Visit(10, "/keep-too"));
        store.LogVisit(Visit(400, "/ancient"));
        store.LogDownload(new DownloadInfo(Now.AddDays(-400), "old.exe", null, "Chrome", "203.0.113.9"));

        var removed = store.Prune(retentionDays: 90, now: Now);

        Assert.Equal(2, removed);
        Assert.Equal(2, store.GetSummary(365, Now).VisitsWindow);
        Assert.Empty(store.GetSummary(365, Now).DownloadsByFile);
    }

    [Fact]
    public void Prune_WithNonPositiveRetention_KeepsEverything()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        store.LogVisit(Visit(5000, "/ancient"));

        Assert.Equal(0, store.Prune(retentionDays: 0, now: Now));
        Assert.Equal(1, store.GetSummary(9999, Now).VisitsWindow);
    }

    // --- ADM-007: CSV export -----------------------------------------------

    [Fact]
    public void Csv_ContainsTotalsSeriesAndBreakdowns()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        store.LogVisit(Visit(0, "/features", referrer: "example.com"));
        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "Chrome", "203.0.113.1"));

        var csv = MetricsExport.ToCsv(store.GetSummary(7, Now));

        Assert.StartsWith("section,key,value\n", csv, StringComparison.Ordinal);
        Assert.Contains("totals,visits_today,1", csv, StringComparison.Ordinal);
        Assert.Contains("totals,downloads_total,1", csv, StringComparison.Ordinal);
        Assert.Contains("top_pages,/features,1", csv, StringComparison.Ordinal);
        Assert.Contains("browsers,Chrome,1", csv, StringComparison.Ordinal);
        Assert.Contains("top_referrers,example.com,1", csv, StringComparison.Ordinal);
        Assert.Contains("daily_visits,2026-08-28,1", csv, StringComparison.Ordinal);
        Assert.Contains("daily_downloads,2026-08-28,1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_QuotesKeysContainingSeparators()
    {
        // Page paths come from request data: a comma or quote must not shift columns.
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        store.LogVisit(Visit(0, "/a,b"));
        store.LogVisit(Visit(0, "/say\"hi\""));

        var csv = MetricsExport.ToCsv(store.GetSummary(7, Now));

        Assert.Contains("top_pages,\"/a,b\",1", csv, StringComparison.Ordinal);
        Assert.Contains("top_pages,\"/say\"\"hi\"\"\",1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvFileName_CarriesTheWindowAndDate()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        Assert.Equal(
            "akml-site-metrics-30d-2026-08-28.csv",
            MetricsExport.FileName(store.GetSummary(30, Now), Now));
    }
    // --- ADM-008: 404 tracking ---------------------------------------------

    [Fact]
    public void NotFound_PathsAreAggregated_AndKeptOutOfVisitFigures()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/"));
        store.LogNotFound(new NotFoundInfo(Now, "/docs/old-guide", "example.com"));
        store.LogNotFound(new NotFoundInfo(Now, "/docs/old-guide", "example.com"));
        store.LogNotFound(new NotFoundInfo(Now.AddDays(-1), "/gone", null));

        var summary = store.GetSummary(30, Now);

        Assert.Equal("/docs/old-guide", summary.TopNotFound[0].Key);
        Assert.Equal(2, summary.TopNotFound[0].Count);
        Assert.Contains(summary.TopNotFound, r => r.Key == "/gone" && r.Count == 1);

        // A 404 is not a page view.
        Assert.Equal(1, summary.VisitsToday);
        Assert.DoesNotContain(summary.TopPages, r => r.Key == "/gone");
    }

    [Fact]
    public void NotFound_RespectsTheWindow()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogNotFound(new NotFoundInfo(Now.AddDays(-20), "/stale", null));

        Assert.Empty(store.GetSummary(7, Now).TopNotFound);
        Assert.Single(store.GetSummary(30, Now).TopNotFound);
    }

    [Fact]
    public void ScriptedClients_AreExcludedFromVisitsLikeCrawlers()
    {
        // A curl or PowerShell page fetch is automation, not a reader. On the live site these
        // were 38% of the browser table, inflating visits, uniques and session shape.
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/", ua: "Chrome", ip: "203.0.113.1"));
        store.LogVisit(Visit(0, "/", ua: "curl", ip: "198.51.100.1"));
        store.LogVisit(Visit(0, "/", ua: "wget", ip: "198.51.100.2"));
        store.LogVisit(Visit(0, "/", ua: "powershell", ip: "198.51.100.3"));
        store.LogVisit(Visit(0, "/", ua: "bot", ip: "198.51.100.4"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(1, summary.VisitsToday);
        Assert.Equal(1, summary.UniqueVisitorsToday);
        Assert.Equal(4, summary.AutomatedVisitsWindow);
        Assert.DoesNotContain(summary.BrowserMix, r => r.Key is "curl" or "wget" or "powershell" or "bot");
    }

    [Fact]
    public void ScriptedDownloads_StillCount_BecauseTheyAreRealAcquisitions()
    {
        // Deliberately different from visits: someone fetching the installer with curl or
        // PowerShell has genuinely acquired it. Only crawlers are dropped.
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "Chrome", "203.0.113.1"));
        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "curl", "203.0.113.2"));
        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "powershell", "203.0.113.3"));
        store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "bot", "198.51.100.9"));

        var summary = store.GetSummary(30, Now);

        Assert.Equal(3, summary.DownloadsTotal);
        Assert.Equal(3, summary.DownloadsWindow);
        Assert.Equal(3, summary.DailyDownloads.Sum(d => d.Count));
    }

    [Fact]
    public void ClearSameOriginReferrers_RepairsHistory_AndIsIdempotent()
    {
        // History written before same-origin was filtered at write time: the site was its own
        // top referrer, which answered the wrong question entirely.
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/docs", referrer: "akml.khamis.work"));
        store.LogVisit(Visit(0, "/", referrer: "akml.khamis.work"));
        store.LogVisit(Visit(0, "/", referrer: "news.example.com"));

        var corrected = store.ClearSameOriginReferrers("akml.khamis.work");

        Assert.Equal(2, corrected);
        var referrers = store.GetSummary(30, Now).TopReferrers;
        Assert.Equal("news.example.com", referrers.Single().Key);

        // Safe to run on every startup.
        Assert.Equal(0, store.ClearSameOriginReferrers("akml.khamis.work"));
    }

    [Fact]
    public void ClearSameOriginReferrers_KeepsTheVisitsThemselves()
    {
        // Only the referrer columns are cleared — the visit really happened and still counts.
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, "/docs", referrer: "akml.khamis.work"));
        store.ClearSameOriginReferrers("akml.khamis.work");

        var summary = store.GetSummary(30, Now);
        Assert.Equal(1, summary.VisitsToday);
        Assert.Contains(summary.TopPages, r => r.Key == "/docs");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ClearSameOriginReferrers_WithNoHost_DoesNothing(string? host)
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        store.LogVisit(Visit(0, "/", referrer: "akml.khamis.work"));

        Assert.Equal(0, store.ClearSameOriginReferrers(host));
        Assert.Single(store.GetSummary(30, Now).TopReferrers);
    }

}

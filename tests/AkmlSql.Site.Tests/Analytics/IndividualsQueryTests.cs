using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T078/T079/T081 (US3): the download-grouping and per-individual queries.
/// <para>
/// Two properties are load-bearing beyond "the counts are right": every grouping reconciles to the
/// headline total with unknowns shown explicitly (SC-008), and a visitor whose cookie was cleared is
/// never silently re-joined to their old rows (FR-022b).
/// </para>
/// </summary>
public sealed class IndividualsQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static AnalyticsStore NewStore(TempDirectory dir) => new(Path.Combine(dir.Path, "analytics.db"));

    private static VisitInfo Visit(
        int daysAgo,
        string? visitorId,
        string? country = null,
        string ip = "203.0.113.7",
        string ua = "Chrome",
        string path = "/download") =>
        new(Now.AddDays(-daysAgo), path, null, ua, ip)
        {
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
            Location = country is null ? GeoLocation.Unknown : new GeoLocation(country[..2].ToUpperInvariant(), country),
        };

    private static DownloadInfo Download(
        int daysAgo,
        string? visitorId,
        string? country = null,
        string file = "AKMLSQLSetup-1.26.0910.2248.exe",
        string? version = "1.26.0910.2248",
        string ua = "Chrome") =>
        new(Now.AddDays(-daysAgo), file, null, ua, "203.0.113.7")
        {
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
            ReleaseVersion = version,
            Location = country is null ? GeoLocation.Unknown : new GeoLocation(country[..2].ToUpperInvariant(), country),
        };

    // --- country grouping ---------------------------------------------------

    [Fact]
    public void DownloadsByCountry_RanksCountriesAndReconcilesToTheTotal()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(Download(0, null, "Egypt"));
        store.LogDownload(Download(0, null, "Egypt"));
        store.LogDownload(Download(1, null, "Egypt"));
        store.LogDownload(Download(1, null, "United Kingdom"));
        store.LogDownload(Download(2, null, country: null)); // unresolved

        var rows = store.GetDownloadsByCountry(30, Now);

        Assert.Equal("Egypt", rows[0].Label);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal(60.0, rows[0].SharePercent);

        // SC-008 / contract M5.1: the rows sum to the headline, unknowns included.
        Assert.Equal(5, rows.Sum(r => r.Count));
        Assert.Equal(100.0, rows.Sum(r => r.SharePercent), 1);
    }

    [Fact]
    public void DownloadsByCountry_ShowsUnknownAsAnExplicitBucket_NeverDropsIt()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // This is the live situation today: the GeoLite2 database has never been installed, so
        // every row's country is NULL. Dropping them would report "0 downloads" for 43 real ones.
        store.LogDownload(Download(0, null, country: null));
        store.LogDownload(Download(0, null, country: null));

        var rows = store.GetDownloadsByCountry(30, Now);

        var unknown = Assert.Single(rows);
        Assert.Equal("Unknown", unknown.Label);
        Assert.Equal(2, unknown.Count);
        Assert.Equal(100.0, unknown.SharePercent);
    }

    // --- version grouping ---------------------------------------------------

    [Fact]
    public void DownloadsByVersion_GroupsByTheVersionRecordedAtDownloadTime()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(Download(0, null, version: "1.26.0910.2248"));
        store.LogDownload(Download(0, null, version: "1.26.0910.2248"));
        store.LogDownload(Download(1, null, file: "old.exe", version: "1.26.0903.1456"));
        store.LogDownload(Download(1, null, file: "stray.exe", version: null));

        var rows = store.GetDownloadsByVersion(30, Now);

        Assert.Equal("1.26.0910.2248", rows[0].Label);
        Assert.Equal(2, rows[0].Count);
        Assert.Contains(rows, r => r.Label == "Unattributed" && r.Count == 1);
        Assert.Equal(4, rows.Sum(r => r.Count));
    }

    // --- bot handling -------------------------------------------------------

    [Fact]
    public void AScriptedInstallerFetchCounts_ButACrawlerDoesNot()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(Download(0, null, ua: "curl"));   // a real acquisition
        store.LogDownload(Download(0, null, ua: "bot"));    // not

        // Contract M5.5: fetching the installer with a script IS an install; a crawler touching the
        // URL is not. The split is deliberate and asymmetric with visit counting.
        Assert.Equal(1, store.GetDownloadsByCountry(30, Now).Sum(r => r.Count));
    }

    [Fact]
    public void ACrawlerNeverAppearsAsAnIndividual()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, Guid.NewGuid().ToString("N"), ua: "bot"));

        Assert.Empty(store.GetIndividuals(new IndividualFilter(30), Now));
    }

    // --- individuals --------------------------------------------------------

    [Fact]
    public void Individuals_AggregateVisitsAndDownloadsPerPerson()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var alice = Guid.NewGuid().ToString("N");
        var bob = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(2, alice, "Egypt"));
        store.LogVisit(Visit(1, alice, "Egypt"));
        store.LogDownload(Download(0, alice, "Egypt"));
        store.LogVisit(Visit(0, bob, "United Kingdom"));

        var rows = store.GetIndividuals(new IndividualFilter(30), Now);

        Assert.Equal(2, rows.Count);

        var aliceRow = rows.Single(r => r.VisitorId == alice);
        Assert.Equal(2, aliceRow.VisitCount);
        Assert.Equal(1, aliceRow.DownloadCount);
        Assert.True(aliceRow.Downloaded);
        Assert.Equal("Egypt", aliceRow.Country);
        Assert.Equal("203.0.113.7", aliceRow.IpAddress);

        var bobRow = rows.Single(r => r.VisitorId == bob);
        Assert.False(bobRow.Downloaded);
    }

    [Fact]
    public void AVisitorActiveAcrossDays_IsOnePersonWithASpanningHistory()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var id = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(5, id));
        store.LogDownload(Download(0, id));

        // SC-019 — the whole point of cookie identity: Monday's visit and Friday's download are one
        // person's story, which the previous daily-re-salted design made impossible.
        var row = Assert.Single(store.GetIndividuals(new IndividualFilter(30), Now));
        Assert.Equal(1, row.VisitCount);
        Assert.Equal(1, row.DownloadCount);
        Assert.True(row.LastSeen - row.FirstSeen >= TimeSpan.FromDays(4));
    }

    [Fact]
    public void AClearedCookie_ProducesANewIndividual_NeverAMerge()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // Same address, same browser, different id — exactly what clearing cookies looks like.
        store.LogVisit(Visit(1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", ip: "203.0.113.7", ua: "Chrome"));
        store.LogVisit(Visit(0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", ip: "203.0.113.7", ua: "Chrome"));

        // FR-022b: re-joining these would reconstruct precisely what the visitor broke.
        Assert.Equal(2, store.GetIndividuals(new IndividualFilter(30), Now).Count);
    }

    [Fact]
    public void NonConsentingTraffic_NeverBecomesAnIndividual_ButStillCounts()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, visitorId: null));
        store.LogDownload(Download(0, visitorId: null));

        Assert.Empty(store.GetIndividuals(new IndividualFilter(30), Now));

        // FR-044: counted, not identified.
        Assert.Equal(1, store.GetSummary(30, Now).DownloadsTotal);
        Assert.Equal(1, store.GetCoverage(30, Now).UnattributedVisits);
    }

    // --- filters ------------------------------------------------------------

    [Fact]
    public void FilteringByCountry_NarrowsTheList()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, Guid.NewGuid().ToString("N"), "Egypt"));
        store.LogVisit(Visit(0, Guid.NewGuid().ToString("N"), "United Kingdom"));

        var egypt = store.GetIndividuals(new IndividualFilter(30, CountryCode: "EG"), Now);

        Assert.Single(egypt);
        Assert.Equal("Egypt", egypt[0].Country);
    }

    [Fact]
    public void FilteringByDownloaded_NarrowsTheListBothWays()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var buyer = Guid.NewGuid().ToString("N");
        var browser = Guid.NewGuid().ToString("N");

        store.LogDownload(Download(0, buyer));
        store.LogVisit(Visit(0, browser));

        Assert.Equal(buyer, Assert.Single(store.GetIndividuals(new IndividualFilter(30, Downloaded: true), Now)).VisitorId);
        Assert.Equal(browser, Assert.Single(store.GetIndividuals(new IndividualFilter(30, Downloaded: false), Now)).VisitorId);
        Assert.Equal(2, store.CountIndividuals(new IndividualFilter(30), Now));
    }

    [Fact]
    public void Paging_ReturnsDistinctSlices()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        for (var i = 0; i < 5; i++)
        {
            store.LogVisit(Visit(i, Guid.NewGuid().ToString("N")));
        }

        var first = store.GetIndividuals(new IndividualFilter(30, Page: 0, PageSize: 2), Now);
        var second = store.GetIndividuals(new IndividualFilter(30, Page: 1, PageSize: 2), Now);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Empty(first.Select(r => r.VisitorId).Intersect(second.Select(r => r.VisitorId)));
        Assert.Equal(5, store.CountIndividuals(new IndividualFilter(30), Now));
    }

    // --- detail -------------------------------------------------------------

    [Fact]
    public void Detail_InterleavesVisitsAndDownloadsInTimeOrder()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var id = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(3, id, path: "/"));
        store.LogVisit(Visit(2, id, path: "/download"));
        store.LogDownload(Download(1, id));
        store.LogVisit(Visit(0, id, path: "/docs"));

        var detail = store.GetIndividual(id, 30, Now);

        Assert.NotNull(detail);
        Assert.Equal(4, detail!.Activity.Count);

        // One stream, ordered — "what did this person do" is inherently chronological.
        Assert.Equal(["visit", "visit", "download", "visit"], detail.Activity.Select(a => a.Kind));
        Assert.Equal("/", detail.Activity[0].Target);
        Assert.Equal("1.26.0910.2248", detail.Activity[2].ReleaseVersion);
    }

    [Fact]
    public void Detail_ForAnUnknownId_IsNull()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        Assert.Null(store.GetIndividual(Guid.NewGuid().ToString("N"), 30, Now));
        Assert.Null(store.GetIndividual("", 30, Now));
    }

    // --- coverage -----------------------------------------------------------

    [Fact]
    public void Coverage_ReportsTheUnattributedShare()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, Guid.NewGuid().ToString("N")));
        store.LogVisit(Visit(0, visitorId: null));
        store.LogVisit(Visit(0, visitorId: null));
        store.LogVisit(Visit(0, visitorId: null));

        var coverage = store.GetCoverage(30, Now);

        Assert.Equal(1, coverage.AttributedVisits);
        Assert.Equal(3, coverage.UnattributedVisits);
        Assert.Equal(75.0, coverage.UnattributedSharePercent);

        // Contract M5.3: attributed + unattributed equals the headline human total.
        Assert.Equal(coverage.TotalVisits, coverage.AttributedVisits + coverage.UnattributedVisits);
    }

    [Fact]
    public void Coverage_SplitsNewFromReturning()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var returning = Guid.NewGuid().ToString("N");
        var fresh = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(40, returning));  // before the 30-day window
        store.LogVisit(Visit(1, returning));   // and again inside it
        store.LogVisit(Visit(0, fresh));

        var coverage = store.GetCoverage(30, Now);

        Assert.Equal(2, coverage.DistinctIndividuals);
        Assert.Equal(1, coverage.ReturningIndividuals);
        Assert.Equal(1, coverage.NewIndividuals);
    }

    [Fact]
    public void Coverage_ReportsAutomatedTrafficSeparatelyRatherThanDiscardingIt()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(0, visitorId: null, ua: "bot"));
        store.LogVisit(Visit(0, visitorId: null, ua: "Chrome"));

        var coverage = store.GetCoverage(30, Now);

        // FR-027: excluded from people figures, but reported — a spike explains an otherwise quiet
        // week, and silently discarding it would make that inexplicable.
        Assert.Equal(1, coverage.AutomatedVisits);
        Assert.Equal(1, coverage.TotalVisits);
    }
}

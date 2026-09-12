using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038, found by looking at the deployed portal: the headline figures must agree with the table
/// underneath them.
/// <para>
/// The live People page reported "8 INDIVIDUALS" above a table reading "0 individuals". The cause
/// was that <see cref="AnalyticsStore.GetIndividuals"/> applied the bot filter and
/// <see cref="AnalyticsStore.GetCoverage"/> did not — so an automated client that accepted a cookie
/// (a headless browser in an E2E run) was counted in one and excluded from the other. A stat that
/// contradicts the list under it is worse than no stat.
/// </para>
/// </summary>
public sealed class CoverageConsistencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static AnalyticsStore NewStore(TempDirectory dir) => new(Path.Combine(dir.Path, "analytics.db"));

    private static VisitInfo Visit(string? visitorId, string ua = "Chrome") =>
        new(Now, "/download", null, ua, "203.0.113.7")
        {
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
        };

    [Fact]
    public void DistinctIndividuals_MatchesTheNumberOfRowsTheListReturns()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogVisit(Visit(Guid.NewGuid().ToString("N")));
        store.LogVisit(Visit(Guid.NewGuid().ToString("N")));
        store.LogVisit(Visit(visitorId: null));

        var coverage = store.GetCoverage(30, Now);
        var rows = store.GetIndividuals(new IndividualFilter(30), Now);

        Assert.Equal(rows.Count, (int)coverage.DistinctIndividuals);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ABotThatAcceptedACookie_IsExcludedFromBothTheStatAndTheList()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // Exactly the live situation: a headless browser accepted consent during an E2E run, so it
        // has a visitor_id AND is classified as a bot.
        store.LogVisit(Visit(Guid.NewGuid().ToString("N"), ua: "bot"));
        store.LogVisit(Visit(Guid.NewGuid().ToString("N"), ua: "Chrome"));

        var coverage = store.GetCoverage(30, Now);
        var rows = store.GetIndividuals(new IndividualFilter(30), Now);

        Assert.Equal(1, coverage.DistinctIndividuals);
        Assert.Single(rows);
        Assert.Equal(rows.Count, (int)coverage.DistinctIndividuals);
    }

    [Fact]
    public void WithOnlyBots_BothReportZero()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        for (var i = 0; i < 8; i++)
        {
            store.LogVisit(Visit(Guid.NewGuid().ToString("N"), ua: "bot"));
        }

        var coverage = store.GetCoverage(30, Now);

        // The exact defect: 8 here, 0 in the table.
        Assert.Equal(0, coverage.DistinctIndividuals);
        Assert.Empty(store.GetIndividuals(new IndividualFilter(30), Now));
    }

    [Fact]
    public void NewPlusReturning_AlwaysEqualsDistinct()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var returning = Guid.NewGuid().ToString("N");

        store.LogVisit(new VisitInfo(Now.AddDays(-40), "/", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = returning,
        });
        store.LogVisit(Visit(returning));
        store.LogVisit(Visit(Guid.NewGuid().ToString("N")));
        store.LogVisit(Visit(Guid.NewGuid().ToString("N"), ua: "bot"));

        var coverage = store.GetCoverage(30, Now);

        Assert.Equal(coverage.DistinctIndividuals, coverage.NewIndividuals + coverage.ReturningIndividuals);
        Assert.Equal(1, coverage.ReturningIndividuals);
    }

    // --- version attribution fallback ---------------------------------------

    [Theory]
    [InlineData("AKMLSQLSetup-1.26.0901.1502.exe", "1.26.0901.1502")]
    [InlineData("AKMLSQLSetup-1.26.0910.2248.exe", "1.26.0910.2248")]
    [InlineData("setup.exe", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void VersionFromFileName_RecoversTheVersionHistoryNeverStored(string? file, string? expected)
    {
        Assert.Equal(expected, AnalyticsStore.VersionFromFileName(file));
    }

    [Fact]
    public void HistoricalDownloads_ShowTheirVersionRatherThanUnattributed()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // A row as it exists in the live database: written before the release_version column, so the
        // column is NULL but the file name carries the version.
        store.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-1.26.0901.1502.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Denied,
            ReleaseVersion = null,
        });

        var row = Assert.Single(store.GetDownloadsByVersion(30, Now));

        // 44 of the live site's 47 downloads looked like this, every one reading "Unattributed"
        // beside a file name that spelled the version out.
        Assert.Equal("1.26.0901.1502", row.Label);
    }

    [Fact]
    public void TheStoredVersionStillWins_WhereItExists()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        // Deliberately mismatched: the stored value is authoritative because the manifest is
        // mutable, and the fallback must never override it.
        store.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-9.99.9999.9999.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Denied,
            ReleaseVersion = "1.26.0910.2248",
        });

        Assert.Equal("1.26.0910.2248", Assert.Single(store.GetDownloadsByVersion(30, Now)).Label);
    }

    [Fact]
    public void AFileWithNoRecoverableVersion_StaysUnattributed()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogDownload(new DownloadInfo(Now, "stray.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Denied,
            ReleaseVersion = null,
        });

        // The explicit bucket still exists — it just no longer swallows rows that could be placed.
        Assert.Equal("Unattributed", Assert.Single(store.GetDownloadsByVersion(30, Now)).Label);
    }
}

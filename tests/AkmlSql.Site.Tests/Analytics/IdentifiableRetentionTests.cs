using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T080 (US3): the retention boundary.
/// <para>
/// Two boundaries exist deliberately. <see cref="AnalyticsStore.DeIdentify"/> erases the address and
/// the identifier at 365 days while <b>keeping the row</b>; the older <see cref="AnalyticsStore.Prune"/>
/// deletes rows outright much later. The test that matters is the one proving the first does not
/// disturb any aggregate — if de-identifying changed the country or version totals for a past
/// period, history would rewrite itself every night and nobody would notice (SC-008, contract M5.4).
/// </para>
/// </summary>
public sealed class IdentifiableRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static string DbPath(TempDirectory dir) => Path.Combine(dir.Path, "analytics.db");

    private static long Count(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static VisitInfo Visit(int daysAgo, string visitorId, string country) =>
        new(Now.AddDays(-daysAgo), "/download", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = visitorId,
            Location = new GeoLocation(country[..2].ToUpperInvariant(), country),
        };

    private static DownloadInfo Download(int daysAgo, string visitorId, string country) =>
        new(Now.AddDays(-daysAgo), "AKMLSQLSetup-1.26.0910.2248.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = visitorId,
            ReleaseVersion = "1.26.0910.2248",
            Location = new GeoLocation(country[..2].ToUpperInvariant(), country),
        };

    [Fact]
    public void DeIdentify_ErasesTheAddressAndIdentifier_ButKeepsTheRow()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogVisit(Visit(400, Guid.NewGuid().ToString("N"), "Egypt"));
        store.LogDownload(Download(400, Guid.NewGuid().ToString("N"), "Egypt"));

        var affected = store.DeIdentify(365, Now);

        Assert.Equal(2, affected);
        Assert.Equal(0, Count(DbPath(dir), "SELECT COUNT(*) FROM visits WHERE ip IS NOT NULL OR visitor_id IS NOT NULL;"));
        Assert.Equal(0, Count(DbPath(dir), "SELECT COUNT(*) FROM downloads WHERE ip IS NOT NULL OR visitor_id IS NOT NULL;"));

        // The rows themselves survive — that is the whole design.
        Assert.Equal(1, Count(DbPath(dir), "SELECT COUNT(*) FROM visits;"));
        Assert.Equal(1, Count(DbPath(dir), "SELECT COUNT(*) FROM downloads;"));
    }

    [Fact]
    public void DeIdentify_LeavesEveryAggregateForThatPeriodUnchanged()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogDownload(Download(400, Guid.NewGuid().ToString("N"), "Egypt"));
        store.LogDownload(Download(400, Guid.NewGuid().ToString("N"), "Egypt"));
        store.LogDownload(Download(400, Guid.NewGuid().ToString("N"), "United Kingdom"));

        var countriesBefore = store.GetDownloadsByCountry(500, Now);
        var versionsBefore = store.GetDownloadsByVersion(500, Now);
        var totalBefore = store.GetSummary(500, Now).DownloadsTotal;

        store.DeIdentify(365, Now);

        // SC-008 / M5.4: identical before and after. If this ever fails, history is being rewritten.
        Assert.Equal(
            countriesBefore.Select(r => (r.Label, r.Count, r.SharePercent)),
            store.GetDownloadsByCountry(500, Now).Select(r => (r.Label, r.Count, r.SharePercent)));
        Assert.Equal(
            versionsBefore.Select(r => (r.Label, r.Count)),
            store.GetDownloadsByVersion(500, Now).Select(r => (r.Label, r.Count)));
        Assert.Equal(totalBefore, store.GetSummary(500, Now).DownloadsTotal);
    }

    [Fact]
    public void DeIdentify_LeavesRowsInsideTheWindowAlone()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));
        var recent = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(10, recent, "Egypt"));
        store.LogVisit(Visit(400, Guid.NewGuid().ToString("N"), "Egypt"));

        store.DeIdentify(365, Now);

        Assert.Equal(1, Count(DbPath(dir), "SELECT COUNT(*) FROM visits WHERE visitor_id IS NOT NULL;"));
        Assert.Single(store.GetIndividuals(new IndividualFilter(500), Now));
    }

    [Fact]
    public void DeIdentify_IsIdempotent()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogVisit(Visit(400, Guid.NewGuid().ToString("N"), "Egypt"));

        Assert.Equal(1, store.DeIdentify(365, Now));

        // Running nightly must not churn rows it has already handled.
        Assert.Equal(0, store.DeIdentify(365, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DeIdentify_WithANonPositivePeriod_DoesNothing(int days)
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogVisit(Visit(400, Guid.NewGuid().ToString("N"), "Egypt"));

        // A misconfigured zero must not be read as "erase everything".
        Assert.Equal(0, store.DeIdentify(days, Now));
        Assert.Equal(1, Count(DbPath(dir), "SELECT COUNT(*) FROM visits WHERE visitor_id IS NOT NULL;"));
    }

    [Fact]
    public void ADeIdentifiedVisitor_NoLongerResolvesToAnIndividual()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));
        var id = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(400, id, "Egypt"));
        Assert.Single(store.GetIndividuals(new IndividualFilter(500), Now));

        store.DeIdentify(365, Now);

        // The person is gone; the visit is still counted. Exactly the intended trade.
        Assert.Empty(store.GetIndividuals(new IndividualFilter(500), Now));
        Assert.Null(store.GetIndividual(id, 500, Now));
        Assert.Equal(1, store.GetCoverage(500, Now).TotalVisits);
    }
}

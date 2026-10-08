using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using AkmlSql.Site.Feedback;
using AkmlSql.Site.Seo;
using AkmlSql.Site.Settings;
using AkmlSql.Site.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

public sealed class AdminReviewFollowUpTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static VisitInfo Visit(string id, string country = "EG", int daysAgo = 0) =>
        new(Now.AddDays(-daysAgo), "/features", null, "Chrome", "192.0.2.1")
        { VisitorId = id, Consent = ConsentState.Granted, Location = new(country, country) };

    [Fact]
    public void LatestDimensionsAndCountryFilterUseAllMatchingPersonsEvents()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.LogVisit(Visit("moving", "ZW", 2) with { UaFamily = "Safari", UserAgent = new("Safari", null, "iOS", null, "tablet") });
        store.LogVisit(Visit("moving", "EG", 1) with { UaFamily = "Firefox", UserAgent = new("Firefox", null, "Android", null, "mobile") });
        var latest = Assert.Single(store.GetIndividuals(new IndividualFilter(30), Now.AddMinutes(1)));
        Assert.Equal("Firefox", latest.Browser);
        Assert.Equal("Android", latest.Os);
        Assert.Equal("mobile", latest.Device);
        store.LogVisit(Visit("moving") with { Location = GeoLocation.Unknown, IpAddress = null });
        store.LogVisit(Visit("fr-only", "FR"));
        var window = store.ResolveWindow(ReportRange.LastDays(30), Now.AddMinutes(1));
        var filter = new IndividualFilter(30, "EG");
        var row = Assert.Single(store.GetIndividuals(filter, window));
        Assert.Equal("EG", row.CountryCode);
        Assert.Equal(3, row.VisitCount);
        Assert.Equal("192.0.2.1", row.IpAddress);
        Assert.Empty(store.GetIndividuals(filter with { CountryCode = "ZW" }, window));
        Assert.Contains(store.GetPeopleCountries(window), c => c.Code == "FR");
        Assert.Equal(1, store.CountIndividuals(filter, window));
        Assert.Equal(0, store.CountReturningIndividuals(filter, window));
        // With identical timestamps, later row ID wins instead of lexical country ordering.
        store.LogVisit(Visit("tie", "ZW"));
        store.LogVisit(Visit("tie", "CA"));
        Assert.Equal("CA", store.GetIndividual("tie", window)!.Summary.CountryCode);
    }

    [Fact]
    public void PeopleExportIncludesAllRowsBeyondFiveHundredAndStableTies()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        for (var i = 0; i < 521; i++) store.LogVisit(Visit($"visitor-{i:D4}"));
        var window = store.ResolveWindow(ReportRange.LastDays(30), Now.AddMinutes(1));
        var filter = new IndividualFilter(30);
        var all = store.EnumerateIndividuals(filter, window).ToList();
        Assert.Equal(521, all.Count);
        Assert.Equal(521, all.Select(r => r.VisitorId).Distinct().Count());
        Assert.Equal(all.Take(50).Select(r => r.VisitorId), store.GetIndividuals(filter, window).Select(r => r.VisitorId));
        Assert.Equal(all.Skip(50).Take(50).Select(r => r.VisitorId), store.GetIndividuals(filter with { Page = 1 }, window).Select(r => r.VisitorId));
        Assert.Equal(526, AdminReportExports.People(store, window, filter).Count());
    }

    [Fact]
    public async Task ReadSnapshotDoesNotBlockWriterAndRemainsConsistent()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.LogVisit(Visit("first"));
        var window = store.ResolveWindow(ReportRange.LastDays(30), Now.AddMinutes(1));
        using var snapshot = store.OpenReadSnapshot();
        Assert.Equal(1, snapshot.GetCoverage(window).DistinctIndividuals);
        await Task.Run(() => store.LogVisit(Visit("second"))).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, snapshot.GetCoverage(window).DistinctIndividuals);
        Assert.Equal(2, store.GetCoverage(window).DistinctIndividuals);
    }

    [Fact]
    public void FeedbackPagingSearchAndExportReachOldestMessages()
    {
        using var dir = new TempDirectory();
        using var store = new FeedbackStore(Path.Combine(dir.Path, "analytics.db"));
        for (var i = 0; i < 235; i++) store.Add(FeedbackCategory.Problem, $"needle {i}", null, null, null, null, Now);
        var ids = Enumerable.Range(0, 5).SelectMany(page => store.List(false, 50, page * 50, "NEEDLE")).Select(r => r.Id).ToArray();
        Assert.Equal(235, ids.Length);
        Assert.Equal(235, ids.Distinct().Count());
        Assert.Equal(1, ids.Last());
        Assert.Equal(235, store.Count(false, "needle"));
        Assert.Equal(235, store.Enumerate(false, "needle").Count());
        Assert.Equal(0, store.Count(false, "%' OR 1=1 --"));
        store.SetHandled(1, true, Now);
        Assert.Equal(234, store.Count(false, "needle"));
        Assert.Equal(1, store.Count(true, "needle"));
    }

    [Fact]
    public void ErrorPagingAndExportHonorAllFiltersWithoutTwoHundredCap()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.LogClientErrors(new ClientErrorBatch(Enumerable.Range(0, 235).Select(i =>
            new ClientErrorInfo(Now, Now, "Error", $"needle {i}", null, "test", "1.2", "ssms", "install")).ToArray()));
        var window = store.ResolveWindow(ReportRange.LastDays(30), Now.AddMinutes(1));
        Assert.Equal(235, store.CountClientErrors(window, "Error", "1.2", "NEEDLE"));
        Assert.Equal(35, store.GetClientErrorPage(window, "Error", "1.2", "needle", 4, 50).Count);
        Assert.Equal(235, store.EnumerateClientErrors(window, "Error", "1.2", "needle").Count());
        Assert.Empty(store.GetClientErrorPage(window, "Warning", "1.2", null, 0, 50));
        Assert.Equal(0, store.CountClientErrors(window, null, "wrong-version", null));
    }

    [Fact]
    public async Task QueueOverflowAndPersistenceAreMeasuredWithoutBlockingProducer()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var health = new CollectionHealth();
        using var sink = new ChannelAnalyticsSink(store, NullLogger<ChannelAnalyticsSink>.Instance, health);
        for (var i = 0; i < 1025; i++) sink.EnqueueVisit(Visit("queued"));
        Assert.Equal(1024, health.Snapshot.Accepted);
        Assert.Equal(1, health.Snapshot.Dropped);
        Assert.Equal(1024, health.Snapshot.Pending);
        await sink.StartAsync(default);
        await Until(() => health.Snapshot.Persisted == 1024);
        await sink.StopAsync(default);
        Assert.Equal(0, health.Snapshot.Pending);
        Assert.Equal(0, health.Snapshot.Failed);
        Assert.NotNull(health.Snapshot.LastProcessingDelay);
    }

    [Fact]
    public async Task QueueFailuresAreVisibleAndDoNotTerminateConsumer()
    {
        using var dir = new TempDirectory();
        var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.Dispose();
        var health = new CollectionHealth();
        using var sink = new ChannelAnalyticsSink(store, NullLogger<ChannelAnalyticsSink>.Instance, health);
        sink.EnqueueVisit(Visit("failed"));
        await sink.StartAsync(default);
        await Until(() => health.Snapshot.Failed == 1);
        await sink.StopAsync(default);
        Assert.Equal(0, health.Snapshot.Pending);
        Assert.Equal(0, health.Snapshot.Persisted);
    }

    [Fact]
    public async Task RetentionChangeTriggersCleanupWhileServiceIsRunning()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");
        using var store = new AnalyticsStore(path);
        using var settings = new SiteSettingsStore(path);
        settings.CreateTableIfMissing(); settings.Load();
        store.LogVisit(Visit("old", daysAgo: 5));
        using var service = new MaintenanceHostedService(store, settings, Options.Create(new AnalyticsOptions()),
            Options.Create(new SiteOptions()), NullLogger<MaintenanceHostedService>.Instance);
        await service.StartAsync(default);
        await Until(() => service.Snapshot.LastSuccessUtc is not null);
        var firstPass = service.Snapshot.LastSuccessUtc;
        Assert.Empty(settings.Save(settings.Current with { IdentifiableRetentionDays = 1 }, "test"));
        await Until(() => service.Snapshot.LastSuccessUtc > firstPass);
        Assert.Equal(1, service.Snapshot.DeIdentified);
        Assert.Equal(0L, service.GetIdentifiableBacklog());
        Assert.Null(store.GetIndividual("old", 30, Now));
        Assert.False(service.Snapshot.Failed);
        await service.StopAsync(default);
    }

    [Fact]
    public void RetentionBacklogUsesCleanupBoundaryAndCountsBothTables()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.LogVisit(Visit("old", daysAgo: 5));
        store.LogVisit(Visit("boundary", daysAgo: 1));
        store.LogDownload(new DownloadInfo(Now.AddDays(-5), "setup.exe", null, "Chrome", "192.0.2.1")
            { VisitorId = "old-download", Consent = ConsentState.Granted });
        Assert.Equal(2, store.CountIdentifiableBacklog(1, Now));
        Assert.Equal(2, store.DeIdentify(1, Now));
        Assert.Equal(0, store.CountIdentifiableBacklog(1, Now));
    }

    [Fact]
    public async Task FailedMaintenanceKeepsLastSuccessAndReportsFailure()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");
        var store = new AnalyticsStore(path);
        using var settings = new SiteSettingsStore(path);
        settings.CreateTableIfMissing(); settings.Load();
        using var service = new MaintenanceHostedService(store, settings, Options.Create(new AnalyticsOptions()),
            Options.Create(new SiteOptions()), NullLogger<MaintenanceHostedService>.Instance);
        await service.RunAsync();
        var success = service.Snapshot.LastSuccessUtc;
        store.Dispose();
        await service.RunAsync();
        Assert.True(service.Snapshot.Failed);
        Assert.False(service.Snapshot.Running);
        Assert.Equal(success, service.Snapshot.LastSuccessUtc);
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("  @SUM(A1)", "\"'  @SUM(A1)\"")]
    [InlineData("a,\"b\"\nline", "\"a,\"\"b\"\"\nline\"")]
    public void StreamingCsvPreservesTextAndNeutralizesFormulas(string value, string expected) =>
        Assert.Equal(expected, AdminCsv.Line(value));

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
}

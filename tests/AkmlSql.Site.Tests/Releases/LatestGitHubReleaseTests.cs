using System.Net;
using System.Text.Json;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using AkmlSql.Site.Telemetry;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AkmlSql.Site.Tests.Releases;

public sealed class LatestGitHubReleaseTests
{
    private const string Url = "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v2.0.0/AKMLSQLSetup-2.0.0.exe";
    private static string Payload(string url = Url, string name = "AKMLSQLSetup-2.0.0.exe", bool prerelease = false) =>
        JsonSerializer.Serialize(new { tag_name = "v2.0.0", draft = false, prerelease, published_at = "2026-10-06T20:00:00Z",
            assets = new[] { new { name, state = "uploaded", size = 12345678, browser_download_url = url, digest = "sha256:" + new string('a', 64) } } });

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = Payload();
        public bool Timeout;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(LatestGitHubRelease.ApiUrl, request.RequestUri!.AbsoluteUri);
            Assert.NotEmpty(request.Headers.UserAgent);
            if (Timeout) throw new TaskCanceledException("timeout");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    [Fact]
    public async Task RefreshCachesMetadataAndSurvivesApiFailureAndRestart()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "last-good.json");
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cache = new LatestGitHubRelease(http, ReleasesManifest.Unavailable, NullLogger<LatestGitHubRelease>.Instance, path);
        await cache.RefreshAsync();
        Assert.Equal(Url, cache.Current!.CdnUrl);
        Assert.Equal(12345678, cache.Current.SizeBytes);
        Assert.Equal(new string('a', 64), cache.Current.Sha256Hash);
        for (var i = 0; i < 100; i++) Assert.Equal("2.0.0", cache.Current.Version);
        Assert.Equal(1, handler.Calls); // Rendering never calls GitHub.
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await cache.RefreshAsync();
        Assert.Equal(Url, cache.Current.CdnUrl);
        using var restarted = new LatestGitHubRelease(http, ReleasesManifest.Unavailable, NullLogger<LatestGitHubRelease>.Instance, path);
        Assert.Equal(Url, restarted.Current!.CdnUrl);
        handler.Timeout = true;
        await restarted.RefreshAsync();
        Assert.Equal(Url, restarted.Current.CdnUrl);
    }

    [Theory]
    [InlineData("https://github.com/mohamedkhamis/AKML-SQL/releases/latest", "AKMLSQLSetup-2.0.0.exe", false)]
    [InlineData("https://example.invalid/setup.exe", "AKMLSQLSetup-2.0.0.exe", false)]
    [InlineData(Url, "updater.exe", false)]
    [InlineData(Url, "AKMLSQLSetup-2.0.0.exe", true)]
    public async Task UnusableAssetDoesNotReplaceLastGood(string url, string name, bool prerelease)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cache = new LatestGitHubRelease(http, ReleasesManifest.Unavailable, NullLogger<LatestGitHubRelease>.Instance);
        await cache.RefreshAsync();
        var good = cache.Current;
        handler.Body = Payload(url, name, prerelease);
        await cache.RefreshAsync();
        Assert.Same(good, cache.Current);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task MalformedResponseRetainsManifestFallback(string payload)
    {
        using var doc = JsonDocument.Parse(Payload());
        var seed = LatestGitHubRelease.Parse(doc.RootElement)!;
        var handler = new Handler { Body = payload };
        using var http = new HttpClient(handler);
        using var cache = new LatestGitHubRelease(http, ReleasesManifest.Create([seed]), NullLogger<LatestGitHubRelease>.Instance);
        await cache.RefreshAsync();
        Assert.Equal(Url, cache.Current!.CdnUrl);
    }

    [Fact]
    public async Task CombinedHistoryKeepsLatestUniqueAndPreservesReleaseNotes()
    {
        using var http = new HttpClient(new Handler());
        using var cache = new LatestGitHubRelease(http, ReleasesManifest.Unavailable, NullLogger<LatestGitHubRelease>.Instance);
        await cache.RefreshAsync();
        var manifest = ReleasesManifest.Create([cache.Current! with { NotesSummary = "Published notes" },
            cache.Current! with { Version = "1.0.0", ReleasedAt = new DateOnly(2026, 10, 1) }]);
        var combined = cache.WithHistory(manifest);
        Assert.Equal(2, combined.Count);
        Assert.Equal("2.0.0", combined[0].Version);
        Assert.Equal("Published notes", combined[0].NotesSummary);
        Assert.True(combined[0].IsLatest);
        Assert.False(combined[1].IsLatest);
        Assert.Single(ReleaseVisibility.Apply(ReleaseVisibilityMode.LatestOnly, 3, combined));
    }

    [Fact]
    public async Task ApiOnlyReleaseRendersDirectNativeLinkWithVersionSizeAndCountsOnce()
    {
        using var dir = new TempDirectory();
        using var http = new HttpClient(new Handler());
        using var cache = new LatestGitHubRelease(http, ReleasesManifest.Unavailable, NullLogger<LatestGitHubRelease>.Instance);
        await cache.RefreshAsync();
        using var settings = new SiteSettingsStore(Path.Combine(dir.Path, "analytics.db"));
        settings.CreateTableIfMissing(); settings.Load();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(ReleasesManifest.Unavailable);
        ctx.Services.AddSingleton(cache);
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton(new ReleaseAvailability(dir.Path));
        var page = ctx.Render<Download>();
        var link = page.Find(".download-hero .download-tracked");
        Assert.Equal(Url, link.GetAttribute("href"));
        Assert.Equal("false", link.GetAttribute("data-enhance-nav"));
        Assert.Contains("2.0.0", link.TextContent);
        Assert.Contains("11.8 MB", link.TextContent);
        var sink = new Sink();
        DownloadEndpoint.HandleCount("AKMLSQLSetup-2.0.0.exe", new DefaultHttpContext(), new DownloadsOptions { Folder = dir.Path }, sink,
            cachedRelease: cache.Find("AKMLSQLSetup-2.0.0.exe"));
        Assert.Equal("2.0.0", Assert.Single(sink.Downloads).ReleaseVersion);
        Assert.Null(cache.Find("../AKMLSQLSetup-2.0.0.exe"));
    }

    private sealed class Sink : IAnalyticsSink
    {
        public List<DownloadInfo> Downloads { get; } = [];
        public void EnqueueDownload(DownloadInfo download) => Downloads.Add(download);
        public void EnqueueVisit(VisitInfo visit) { }
        public void EnqueueNotFound(NotFoundInfo notFound) { }
        public void EnqueueClientErrors(ClientErrorBatch batch) { }
    }
}

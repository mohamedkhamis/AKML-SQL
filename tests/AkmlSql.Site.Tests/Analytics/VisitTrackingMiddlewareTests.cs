using AkmlSql.Site.Analytics;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Path classifier + request gate for the visit-tracking middleware, UA bucketing, and
/// referrer-host extraction. The classifier is a pure static so the exclusion set is pinned
/// by data-driven tests.
/// </summary>
public sealed class VisitTrackingMiddlewareTests
{
    [Theory]
    // Public content pages — tracked.
    [InlineData("/", true)]
    [InlineData("/features", true)]
    [InlineData("/download", true)]
    [InlineData("/docs", true)]
    [InlineData("/docs/formatting", true)]
    [InlineData("/not-found", true)] // reached only via re-execute; the 2xx gate keeps error statuses out
    // Admin portal + download tracker — excluded.
    [InlineData("/admin", false)]
    [InlineData("/admin/login", false)]
    [InlineData("/admin/anything", false)]
    [InlineData("/dl", false)]
    [InlineData("/dl/setup.exe", false)]
    // Static assets, framework files, machine endpoints — excluded.
    [InlineData("/css/site.css", false)]
    [InlineData("/js/theme-boot.js", false)]
    [InlineData("/docs-assets/img.png", false)]
    [InlineData("/_framework/blazor.web.js", false)]
    [InlineData("/favicon.svg", false)]
    [InlineData("/search-index.json", false)]
    [InlineData("/sitemap.xml", false)]
    [InlineData("/robots.txt", false)]
    // Routing is case-insensitive, so the classifier must be too.
    [InlineData("/DL/setup.exe", false)]
    [InlineData("/ADMIN", false)]
    // Segment-boundary checks: lookalikes of excluded prefixes stay trackable.
    [InlineData("/downloads-info", true)]
    [InlineData("/administer", true)]
    // Not a rooted path at all.
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("relative/path", false)]
    public void IsTrackablePath_ClassifiesPaths(string? path, bool expected) =>
        Assert.Equal(expected, VisitTrackingMiddleware.IsTrackablePath(path));

    [Fact]
    public void ShouldTrack_Requires2xxGetOrHeadHtmlAndTrackablePath()
    {
        Assert.True(VisitTrackingMiddleware.ShouldTrack("/", "GET", 200, "text/html; charset=utf-8"));
        Assert.True(VisitTrackingMiddleware.ShouldTrack("/docs/x", "HEAD", 200, "text/html"));

        Assert.False(VisitTrackingMiddleware.ShouldTrack("/", "GET", 404, "text/html"));
        Assert.False(VisitTrackingMiddleware.ShouldTrack("/", "GET", 302, "text/html"));
        Assert.False(VisitTrackingMiddleware.ShouldTrack("/", "POST", 200, "text/html"));
        Assert.False(VisitTrackingMiddleware.ShouldTrack("/", "GET", 200, "application/json"));
        Assert.False(VisitTrackingMiddleware.ShouldTrack("/", "GET", 200, null));
        Assert.False(VisitTrackingMiddleware.ShouldTrack("/dl/x.exe", "GET", 200, "text/html"));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0", "Edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36", "Chrome")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0", "Firefox")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15", "Safari")]
    [InlineData("curl/8.7.1", "curl")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "bot")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; bingbot/2.0) Chrome/125.0 Safari/537.36", "bot")]
    [InlineData("HeadlessChrome/126.0.0.0", "bot")]
    [InlineData("Wget/1.21", "other")]
    [InlineData(null, "other")]
    [InlineData("", "other")]
    public void UserAgentBuckets_MapToCoarseFamilies(string? ua, string expected) =>
        Assert.Equal(expected, UserAgentBuckets.FromUserAgent(ua));

    [Theory]
    [InlineData("https://example.com/some/page?q=1", "example.com")]
    [InlineData("https://sub.example.co.uk:8443/x", "sub.example.co.uk")]
    [InlineData("http://example.com", "example.com")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("/relative/path", null)]
    [InlineData("not a uri", null)]
    public void ReferrerHost_KeepsHostOnly(string? referer, string? expected) =>
        Assert.Equal(expected, HttpRequestFacts.ReferrerHost(referer));
}

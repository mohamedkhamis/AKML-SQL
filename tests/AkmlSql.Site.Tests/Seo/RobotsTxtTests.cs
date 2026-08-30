using AkmlSql.Site.Seo;
using Xunit;

namespace AkmlSql.Site.Tests.Seo;

/// <summary>
/// SEO-002: robots.txt is generated from <c>Site:BaseUrl</c> instead of being a checked-in file.
/// The static copy advertised a sitemap on <c>akmlsql.com</c> while the site served
/// <c>akml.khamis.work</c>, and nothing could detect the drift.
/// </summary>
public sealed class RobotsTxtTests
{
    [Fact]
    public void SitemapLineUsesTheConfiguredHost()
    {
        var robots = RobotsTxt.Build("https://akml.khamis.work");

        Assert.Contains("Sitemap: https://akml.khamis.work/sitemap.xml", robots, StringComparison.Ordinal);
    }

    [Fact]
    public void SitemapLineTracksAHostChange()
    {
        // The whole point of generating the file: a new host needs no second edit.
        Assert.Contains("Sitemap: https://example.test/sitemap.xml", RobotsTxt.Build("https://example.test"));
    }

    [Fact]
    public void TrailingSlashOnTheBaseUrlDoesNotDoubleUp()
    {
        Assert.Contains("Sitemap: https://akml.khamis.work/sitemap.xml", RobotsTxt.Build("https://akml.khamis.work/"));
        Assert.DoesNotContain("//sitemap.xml", RobotsTxt.Build("https://akml.khamis.work/"));
    }

    [Fact]
    public void CrawlingIsAllowedForPublicContent()
    {
        var robots = RobotsTxt.Build("https://akml.khamis.work");

        Assert.Contains("User-agent: *", robots, StringComparison.Ordinal);
        Assert.Contains("Allow: /", robots, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin")]   // SEC-004: defence in depth over the cookie guard + noindex
    [InlineData("/dl/")]     // tracked installer endpoint, not a page
    [InlineData("/health")]  // machine endpoint
    public void NonContentPathsAreDisallowed(string path) =>
        Assert.Contains("Disallow: " + path, RobotsTxt.Build("https://akml.khamis.work"), StringComparison.Ordinal);
}

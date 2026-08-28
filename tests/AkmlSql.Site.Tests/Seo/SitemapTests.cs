using AkmlSql.Site.Docs;
using AkmlSql.Site.Seo;
using Xunit;

namespace AkmlSql.Site.Tests.Seo;

/// <summary>
/// Spec 034 T031 (SEO): sitemap.xml generation — static routes plus every docs route,
/// absolute URLs from the configured base. No lastmod: the only available timestamp was the
/// source file mtime (= checkout date after a fresh clone), which is misleading (C10).
/// </summary>
public sealed class SitemapTests
{
    private static Document MakeDoc(string slug) =>
        new()
        {
            Title = slug,
            Slug = slug,
            SourcePath = slug + ".md",
            Section = "Guides",
            Order = int.MaxValue,
        };

    [Fact]
    public void Build_IncludesStaticRoutes_AndAllDocRoutes()
    {
        var xml = Sitemap.Build("https://akmlsql.com", [MakeDoc("architecture"), MakeDoc("web/m4-iis-installer")]);

        Assert.Contains("<loc>https://akmlsql.com/</loc>", xml);
        Assert.Contains("<loc>https://akmlsql.com/features</loc>", xml);
        Assert.Contains("<loc>https://akmlsql.com/download</loc>", xml);
        Assert.Contains("<loc>https://akmlsql.com/docs</loc>", xml);
        Assert.Contains("<loc>https://akmlsql.com/docs/architecture</loc>", xml);
        Assert.Contains("<loc>https://akmlsql.com/docs/web/m4-iis-installer</loc>", xml);
    }

    [Fact]
    public void Build_TrimsTrailingSlash_FromBaseUrl()
    {
        var xml = Sitemap.Build("https://staging.example.com/", [MakeDoc("formatting")]);

        Assert.Contains("<loc>https://staging.example.com/docs/formatting</loc>", xml);
        Assert.DoesNotContain("https://staging.example.com//", xml);
    }

    [Fact]
    public void Build_OmitsLastMod_Entirely()
    {
        var xml = Sitemap.Build("https://akmlsql.com", [MakeDoc("architecture")]);

        // C10: file mtime equals checkout date after a fresh clone — misleading, so dropped.
        Assert.DoesNotContain("lastmod", xml);
    }
}

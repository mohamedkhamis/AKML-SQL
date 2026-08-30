using AkmlSql.Site.Docs;
using AkmlSql.Site.Seo;
using Xunit;

namespace AkmlSql.Site.Tests.Seo;

/// <summary>
/// Spec 034 T031 (SEO): sitemap.xml generation — static routes plus every docs route,
/// absolute URLs from the configured base.
/// SEO-003: lastmod now comes from the git dates in docs-metadata.json. The original C10 note
/// omitted it because the only timestamp available was the source file mtime (= checkout date
/// after a fresh clone); that premise no longer holds. A document with no dates still gets no
/// lastmod — an absent element is honest, a fabricated date is not.
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
    public void Build_OmitsLastMod_ForDocumentsWithNoDates()
    {
        var xml = Sitemap.Build("https://akmlsql.com", [MakeDoc("architecture")]);

        // No metadata entry -> no date to state. Never fabricate one.
        Assert.DoesNotContain("lastmod", xml);
    }

    [Fact]
    public void Build_EmitsLastMod_FromTheDocumentGitDates()
    {
        var doc = MakeDoc("formatting");
        doc.Dates = new DocDates(new DateOnly(2026, 3, 24), new DateOnly(2026, 7, 16));

        var xml = Sitemap.Build("https://akmlsql.com", [doc]);

        // The UPDATED date, not the added date — lastmod means "last changed".
        Assert.Contains("<loc>https://akmlsql.com/docs/formatting</loc>", xml);
        Assert.Contains("<lastmod>2026-07-16</lastmod>", xml);
        Assert.DoesNotContain("2026-03-24", xml);
    }

    [Fact]
    public void Build_DatesTheHomeAndDocsIndex_FromTheNewestDocument()
    {
        var older = MakeDoc("configuration");
        older.Dates = new DocDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 29));
        var newer = MakeDoc("formatting");
        newer.Dates = new DocDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 16));

        var xml = Sitemap.Build("https://akmlsql.com", [older, newer]);

        // The index pages are as fresh as the freshest thing they list.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(xml, "<lastmod>2026-07-16</lastmod>").Count);
    }
}

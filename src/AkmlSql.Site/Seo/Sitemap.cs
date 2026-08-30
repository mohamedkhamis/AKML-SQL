using System.Text;
using AkmlSql.Site.Docs;

namespace AkmlSql.Site.Seo;

/// <summary>Configuration binding for the <c>Site</c> section of appsettings.json.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Absolute base URL for canonical links in sitemap.xml/robots.txt (no trailing slash).
    /// Default matches the production host; override per deployment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://akml.khamis.work";

    /// <summary>Base URL with any trailing slash removed, for concatenating routes onto.</summary>
    public string CanonicalRoot => (BaseUrl ?? "").TrimEnd('/');
}

/// <summary>
/// Spec 034 T031: sitemap.xml generation — the static routes plus every docs route from the
/// startup-built catalog (contracts/site-routes.md: "sitemap.xml … includes all doc routes").
/// </summary>
public static class Sitemap
{
    private static readonly string[] StaticRoutes = ["/", "/features", "/download", "/docs"];

    /// <summary>Builds the sitemap XML document for the given absolute base URL.</summary>
    public static string Build(string baseUrl, IEnumerable<Document> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var root = (baseUrl ?? "").TrimEnd('/');

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

        // SEO-003: the newest document date also dates the docs index and, as the most recent
        // content change on the site, the home page.
        var newest = documents
            .Select(d => d.Dates?.Updated)
            .Where(d => d is not null)
            .DefaultIfEmpty(null)
            .Max();

        foreach (var route in StaticRoutes)
        {
            // /features and /download are hand-authored marketing pages with no tracked date.
            AppendUrl(builder, root + route, route is "/" or "/docs" ? newest : null);
        }

        foreach (var document in documents)
        {
            AppendUrl(builder, root + document.Route, document.Dates?.Updated);
        }

        builder.Append("</urlset>\n");
        return builder.ToString();
    }

    /// <summary>
    /// SEO-003: <c>lastmod</c> now comes from the git dates in docs-metadata.json. The original
    /// C10 note omitted it because the only timestamp available was the source file's mtime, which
    /// after a fresh clone is the checkout date — that premise no longer holds. Still omitted when
    /// a document has no metadata entry: an absent element is honest, a fabricated date is not.
    /// </summary>
    private static void AppendUrl(StringBuilder builder, string location, DateOnly? lastModified)
    {
        builder.Append("  <url>\n    <loc>").Append(EscapeXml(location)).Append("</loc>\n");
        if (lastModified is { } date)
        {
            builder.Append("    <lastmod>")
                   .Append(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                   .Append("</lastmod>\n");
        }

        builder.Append("  </url>\n");
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}

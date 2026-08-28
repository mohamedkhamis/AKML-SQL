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
    public string BaseUrl { get; set; } = "https://akmlsql.com";
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

        foreach (var route in StaticRoutes)
        {
            AppendUrl(builder, root + route);
        }

        foreach (var document in documents)
        {
            AppendUrl(builder, root + document.Route);
        }

        builder.Append("</urlset>\n");
        return builder.ToString();
    }

    private static void AppendUrl(StringBuilder builder, string location)
    {
        // No <lastmod>: the only available timestamp was the source file's mtime, which after
        // a fresh clone is the checkout date — misleading, so it is omitted entirely (C10).
        builder.Append("  <url>\n    <loc>").Append(EscapeXml(location)).Append("</loc>\n");
        builder.Append("  </url>\n");
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}

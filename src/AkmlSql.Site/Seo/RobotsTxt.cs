using System.Text;

namespace AkmlSql.Site.Seo;

/// <summary>
/// SEO-002: <c>robots.txt</c> is generated from <c>Site:BaseUrl</c> rather than checked in as a
/// static file. The static copy hardcoded a <c>Sitemap:</c> line pointing at a host the site does
/// not serve, and nothing could detect the drift — a generated file cannot disagree with the
/// configuration it is built from.
/// <para>
/// SEC-004: also disallows the admin portal. That branch is already cookie-guarded and its pages
/// carry <c>noindex</c>, so this is defence in depth, not the control.
/// </para>
/// </summary>
public static class RobotsTxt
{
    /// <summary>Paths kept out of crawl indexes (admin portal, machine endpoints).</summary>
    private static readonly string[] DisallowedPaths = ["/admin", "/dl/", "/health"];

    /// <summary>Builds the robots.txt body for the given absolute base URL.</summary>
    public static string Build(string baseUrl)
    {
        var root = (baseUrl ?? "").TrimEnd('/');

        var builder = new StringBuilder();
        builder.Append("User-agent: *\n");
        builder.Append("Allow: /\n");
        foreach (var path in DisallowedPaths)
        {
            builder.Append("Disallow: ").Append(path).Append('\n');
        }

        builder.Append('\n');
        builder.Append("Sitemap: ").Append(root).Append("/sitemap.xml\n");
        return builder.ToString();
    }
}

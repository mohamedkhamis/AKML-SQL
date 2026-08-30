using System.Globalization;

namespace AkmlSql.Site.Analytics;

/// <summary>Shared, privacy-reducing extraction of request facts for the metrics pipeline.</summary>
public static class HttpRequestFacts
{
    /// <summary>Longest referrer URL stored; anything past this is a tracking blob, not a link.</summary>
    private const int MaxReferrerLength = 512;

    /// <summary>Longest campaign value stored.</summary>
    private const int MaxCampaignLength = 128;

    /// <summary>
    /// Host part of the Referer header only (no path/query); null when absent, not an absolute
    /// URI, or SAME-ORIGIN.
    /// <para>
    /// Same-origin is excluded because a referrer table answers "who sends me traffic", and every
    /// internal click also sets a Referer. On the live site this made the site's own host the top
    /// referrer by two orders of magnitude (160 hits against 2 for the only real external source),
    /// which is worse than useless — it hid the answer.
    /// </para>
    /// </summary>
    public static string? ReferrerHost(HttpRequest request)
    {
        var host = ReferrerHost(request.Headers.Referer.ToString());
        return string.Equals(host, request.Host.Host, StringComparison.OrdinalIgnoreCase) ? null : host;
    }

    /// <summary>Host part of a Referer value only; null when empty, relative, or unparseable.</summary>
    public static string? ReferrerHost(string? referer) =>
        !string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;

    /// <summary>
    /// Full referrer URL including path, truncated. The host alone answers "who links to me";
    /// the path answers "which post" — the question that actually informs what to write next.
    /// Same-origin referrers are dropped: internal navigation is not an acquisition source and
    /// would otherwise dominate the table.
    /// </summary>
    public static string? ReferrerUrl(HttpRequest request)
    {
        var referer = request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referer) || !Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Truncate(referer, MaxReferrerLength);
    }

    /// <summary>Remote client IP string — used for hashing and geo lookup, never stored raw.</summary>
    public static string? ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Primary language tag from Accept-Language, lower-cased ("en", "ar-eg"), or null.
    /// Only the highest-priority entry is kept: the full header is close to a browser
    /// fingerprint, and the first tag answers the only question being asked — is it worth
    /// translating this site.
    /// </summary>
    public static string? Language(HttpRequest request)
    {
        var header = request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var best = header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLanguageEntry)
            .Where(entry => entry.Tag is not null)
            .OrderByDescending(entry => entry.Quality)
            .FirstOrDefault();

        return best.Tag;
    }

    /// <summary>Splits "ar-EG;q=0.9" into its tag and quality; "*" is not a language.</summary>
    private static (string? Tag, double Quality) ParseLanguageEntry(string entry)
    {
        var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tag = parts[0].ToLowerInvariant();
        if (tag.Length == 0 || tag == "*" || tag.Length > 35)
        {
            return (null, 0);
        }

        var quality = 1.0;
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(part[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                quality = parsed;
            }
        }

        return (tag, quality);
    }

    /// <summary>
    /// UTM campaign parameters from the query string. These are what connect a download back to
    /// the post, newsletter or link that produced it — the referrer alone cannot, because most
    /// clients now send a bare origin or nothing at all.
    /// </summary>
    public static CampaignInfo Campaign(HttpRequest request)
    {
        var query = request.Query;
        return new CampaignInfo(
            Read(query, "utm_source"),
            Read(query, "utm_medium"),
            Read(query, "utm_campaign"),
            Read(query, "utm_term"),
            Read(query, "utm_content"));

        static string? Read(IQueryCollection query, string key) =>
            query.TryGetValue(key, out var value) ? Truncate(value.ToString(), MaxCampaignLength) : null;
    }

    /// <summary>Trims to <paramref name="max"/> characters; null/blank collapses to null.</summary>
    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>UTM campaign parameters carried on an inbound link.</summary>
/// <param name="Source">utm_source — where the visit came from ("newsletter", "reddit").</param>
/// <param name="Medium">utm_medium — the channel ("email", "social", "cpc").</param>
/// <param name="Campaign">utm_campaign — the named push ("v1-launch").</param>
/// <param name="Term">utm_term — paid keyword.</param>
/// <param name="Content">utm_content — which creative/link variant.</param>
public sealed record CampaignInfo(
    string? Source,
    string? Medium,
    string? Campaign,
    string? Term,
    string? Content)
{
    /// <summary>No campaign parameters present.</summary>
    public static readonly CampaignInfo None = new(null, null, null, null, null);

    /// <summary>True when the link carried at least one UTM parameter.</summary>
    public bool IsPresent =>
        Source is not null || Medium is not null || Campaign is not null || Term is not null || Content is not null;
}

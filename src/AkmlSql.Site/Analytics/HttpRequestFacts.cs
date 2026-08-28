namespace AkmlSql.Site.Analytics;

/// <summary>Shared, privacy-reducing extraction of request facts for the metrics pipeline.</summary>
public static class HttpRequestFacts
{
    /// <summary>Host part of the Referer header only (no path/query); null when absent or not an absolute URI.</summary>
    public static string? ReferrerHost(HttpRequest request) => ReferrerHost(request.Headers.Referer.ToString());

    /// <summary>Host part of a Referer value only; null when empty, relative, or unparseable.</summary>
    public static string? ReferrerHost(string? referer) =>
        !string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;

    /// <summary>Remote client IP string — used only as hashing input, never stored raw.</summary>
    public static string? ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();
}

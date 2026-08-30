namespace AkmlSql.Site.Analytics;

/// <summary>
/// Logs page visits after routing: only successful (2xx) GET/HEAD responses that rendered HTML
/// for a public content path are recorded. Everything is funneled through the fire-and-forget
/// <see cref="IAnalyticsSink"/>, so the request is never blocked and tracking failures never
/// escape into it.
/// </summary>
public sealed class VisitTrackingMiddleware
{
    // Exact-match exclusions (machine endpoints + icon files that happen to live at the root).
    private static readonly string[] ExcludedPaths =
        ["/search-index.json", "/sitemap.xml", "/robots.txt", "/favicon.svg", "/favicon.ico", "/health"];

    // Segment-boundary prefix exclusions: "/dl" excludes "/dl" and "/dl/x.exe" but NOT "/dlx".
    private static readonly string[] ExcludedPrefixes =
        ["/admin", "/dl", "/docs-assets", "/css", "/js", "/_framework", "/favicon"];

    private readonly RequestDelegate _next;
    private readonly IAnalyticsSink _sink;
    private readonly GeoLookup _geo;
    private readonly ILogger<VisitTrackingMiddleware> _logger;

    public VisitTrackingMiddleware(
        RequestDelegate next,
        IAnalyticsSink sink,
        GeoLookup geo,
        ILogger<VisitTrackingMiddleware> logger)
    {
        _next = next;
        _sink = sink;
        _geo = geo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Server-side handling time, so a slow page shows up in the metrics rather than only in
        // someone's complaint. Started before _next so it covers the whole downstream pipeline.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await _next(context);
        var elapsedMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        try
        {
            var request = context.Request;
            if (ShouldTrack(request.Path.Value, request.Method, context.Response.StatusCode, context.Response.ContentType))
            {
                var ip = HttpRequestFacts.ClientIp(context);
                var userAgent = UserAgentDetailsParser.Parse(request.Headers.UserAgent);

                _sink.EnqueueVisit(new VisitInfo(
                    DateTimeOffset.UtcNow,
                    request.Path.Value ?? "/",
                    HttpRequestFacts.ReferrerHost(request),
                    // Keep the coarse family in its original column so existing history and the
                    // bot filter stay comparable; the parsed detail goes alongside it.
                    userAgent.Browser,
                    ip)
                {
                    ReferrerUrl = HttpRequestFacts.ReferrerUrl(request),
                    UserAgent = userAgent,
                    // Resolved from the FULL address here; the store persists only the prefix.
                    Location = _geo.Locate(ip),
                    Language = HttpRequestFacts.Language(request),
                    Campaign = HttpRequestFacts.Campaign(request),
                    DurationMs = elapsedMs,
                });
            }
            else if (ShouldTrackNotFound(request.Path.Value, request.Method, context.Response.StatusCode))
            {
                // ADM-008: a 404 is the one failure the owner can act on — it names a link
                // someone followed that no longer resolves.
                _sink.EnqueueNotFound(new NotFoundInfo(
                    DateTimeOffset.UtcNow,
                    request.Path.Value ?? "/",
                    HttpRequestFacts.ReferrerHost(request)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit tracking failed for {Path}.", context.Request.Path.Value);
        }
    }

    /// <summary>
    /// Full request/response gate: 2xx status, GET or HEAD, HTML content type, and a trackable path.
    /// </summary>
    public static bool ShouldTrack(string? path, string method, int statusCode, string? contentType)
    {
        if (statusCode < 200 || statusCode >= 300)
        {
            return false;
        }

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        if (contentType is null || !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsTrackablePath(path);
    }

    /// <summary>
    /// ADM-008: gate for 404 recording — a GET/HEAD for a trackable path that returned 404.
    /// Asset and machine paths are excluded by the same classifier as visits, so a missing
    /// favicon or a probe for /wp-login.php under an excluded root does not fill the table.
    /// Content type is deliberately NOT checked: a 404 for a non-HTML path is still a broken
    /// link worth knowing about.
    /// </summary>
    public static bool ShouldTrackNotFound(string? path, string method, int statusCode)
    {
        if (statusCode != StatusCodes.Status404NotFound)
        {
            return false;
        }

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        return IsTrackablePath(path);
    }

    /// <summary>
    /// Path classifier (unit-tested): public content pages only. Excludes the admin portal, the
    /// download tracker, static asset roots, framework files, and machine endpoints.
    /// Case-insensitive to match ASP.NET routing.
    /// </summary>
    public static bool IsTrackablePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
        {
            return false;
        }

        foreach (var exact in ExcludedPaths)
        {
            if (string.Equals(path, exact, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var prefix in ExcludedPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

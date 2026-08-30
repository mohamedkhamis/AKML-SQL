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
    private readonly ILogger<VisitTrackingMiddleware> _logger;

    public VisitTrackingMiddleware(RequestDelegate next, IAnalyticsSink sink, ILogger<VisitTrackingMiddleware> logger)
    {
        _next = next;
        _sink = sink;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        try
        {
            var request = context.Request;
            if (ShouldTrack(request.Path.Value, request.Method, context.Response.StatusCode, context.Response.ContentType))
            {
                _sink.EnqueueVisit(new VisitInfo(
                    DateTimeOffset.UtcNow,
                    request.Path.Value ?? "/",
                    HttpRequestFacts.ReferrerHost(request),
                    UserAgentBuckets.FromUserAgent(request.Headers.UserAgent),
                    HttpRequestFacts.ClientIp(context)));
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

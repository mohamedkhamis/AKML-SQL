namespace AkmlSql.Site.Analytics;

// Site metrics models: events flow from the request pipeline (visit tracking middleware,
// /dl download endpoint) through IAnalyticsSink into AnalyticsStore; AnalyticsSummary is the
// read shape consumed by the /admin dashboard.

/// <summary>One page-view event. <see cref="IpAddress"/> is hashed per-day by the store and never persisted raw.</summary>
public sealed record VisitInfo(DateTimeOffset Utc, string Path, string? ReferrerHost, string? UaFamily, string? IpAddress);

/// <summary>One installer download event. <see cref="IpAddress"/> is hashed per-day by the store and never persisted raw.</summary>
public sealed record DownloadInfo(DateTimeOffset Utc, string File, string? ReferrerHost, string? UaFamily, string? IpAddress);

/// <summary>
/// ADM-008: one request that produced a 404. Visit tracking records only 2xx responses, so broken
/// inbound links and stale bookmarks were invisible — exactly the thing the owner can fix.
/// </summary>
public sealed record NotFoundInfo(DateTimeOffset Utc, string Path, string? ReferrerHost);

/// <summary>Fire-and-forget metrics queue. Implementations must never throw and never block the caller.</summary>
public interface IAnalyticsSink
{
    /// <summary>Queues a page visit for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueVisit(VisitInfo visit);

    /// <summary>Queues an installer download for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueDownload(DownloadInfo download);

    /// <summary>Queues a 404 for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueNotFound(NotFoundInfo notFound);
}

/// <summary>Aggregate count for one key (page path, file name, referrer host).</summary>
public sealed record CountRow(string Key, long Count);

/// <summary>One day of the visit time series (zero-filled).</summary>
public sealed record DailyCount(DateOnly Day, long Count);

/// <summary>
/// Read model for the /admin dashboard. Windows are rolling and inclusive of today:
/// "7d" = today plus the previous 6 days; the N-day window follows <see cref="Days"/>.
/// </summary>
public sealed class AnalyticsSummary
{
    /// <summary>Requested window length in days (drives <see cref="VisitsWindow"/>, <see cref="DailyVisits"/>, top-pages and referrer tables).</summary>
    public required int Days { get; init; }

    /// <summary>Page views on the current UTC day.</summary>
    public required long VisitsToday { get; init; }

    /// <summary>Page views over the last 7 days (today inclusive).</summary>
    public required long VisitsLast7Days { get; init; }

    /// <summary>Page views over the last <see cref="Days"/> days (today inclusive).</summary>
    public required long VisitsWindow { get; init; }

    /// <summary>All-time installer downloads.</summary>
    public required long DownloadsTotal { get; init; }

    /// <summary>Installer downloads over the last 7 days (today inclusive).</summary>
    public required long DownloadsLast7Days { get; init; }

    /// <summary>Most-visited pages within the window (path, views), descending.</summary>
    public required IReadOnlyList<CountRow> TopPages { get; init; }

    /// <summary>All-time downloads per file (file, count), descending.</summary>
    public required IReadOnlyList<CountRow> DownloadsByFile { get; init; }

    /// <summary>Daily visit counts for the window, oldest first, zero-filled for quiet days.</summary>
    public required IReadOnlyList<DailyCount> DailyVisits { get; init; }

    /// <summary>Top referrer hosts within the window (host, views), descending; empty/null referrers excluded.</summary>
    public required IReadOnlyList<CountRow> TopReferrers { get; init; }

    // --- ADM-001/002/005: values the pipeline already recorded but the dashboard never showed ---

    /// <summary>
    /// ADM-002: distinct visitors today, counted from the per-day salted IP hash. That hash exists
    /// precisely so unique counting is possible without storing an IP; nothing used it before.
    /// Only meaningful within a single day — the salt is re-mixed per day by design.
    /// </summary>
    public required long UniqueVisitorsToday { get; init; }

    /// <summary>Distinct visitors on each day of the window, oldest first (same caveat as above).</summary>
    public required IReadOnlyList<DailyCount> DailyUniqueVisitors { get; init; }

    /// <summary>
    /// ADM-001: crawler page views within the window. Bots are excluded from every other figure
    /// here — counting them as visitors inflated every headline number — but shown separately
    /// rather than silently discarded.
    /// </summary>
    public required long BotVisitsWindow { get; init; }

    /// <summary>Browser mix within the window (user-agent family, views), descending; bots excluded.</summary>
    public required IReadOnlyList<CountRow> BrowserMix { get; init; }

    /// <summary>ADM-005: daily installer downloads for the window, oldest first, zero-filled.</summary>
    public required IReadOnlyList<DailyCount> DailyDownloads { get; init; }

    /// <summary>Installer downloads over the last <see cref="Days"/> days (today inclusive).</summary>
    public required long DownloadsWindow { get; init; }

    /// <summary>
    /// ADM-008: most-requested missing paths within the window — broken inbound links and stale
    /// bookmarks, which visit tracking (2xx only) could never surface.
    /// </summary>
    public required IReadOnlyList<CountRow> TopNotFound { get; init; }
}

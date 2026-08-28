namespace AkmlSql.Site.Analytics;

// Site metrics models: events flow from the request pipeline (visit tracking middleware,
// /dl download endpoint) through IAnalyticsSink into AnalyticsStore; AnalyticsSummary is the
// read shape consumed by the /admin dashboard.

/// <summary>One page-view event. <see cref="IpAddress"/> is hashed per-day by the store and never persisted raw.</summary>
public sealed record VisitInfo(DateTimeOffset Utc, string Path, string? ReferrerHost, string? UaFamily, string? IpAddress);

/// <summary>One installer download event. <see cref="IpAddress"/> is hashed per-day by the store and never persisted raw.</summary>
public sealed record DownloadInfo(DateTimeOffset Utc, string File, string? ReferrerHost, string? UaFamily, string? IpAddress);

/// <summary>Fire-and-forget metrics queue. Implementations must never throw and never block the caller.</summary>
public interface IAnalyticsSink
{
    /// <summary>Queues a page visit for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueVisit(VisitInfo visit);

    /// <summary>Queues an installer download for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueDownload(DownloadInfo download);
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
}

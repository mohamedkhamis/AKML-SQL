namespace AkmlSql.Site.Analytics;

using AkmlSql.Site.Consent;
using AkmlSql.Site.Telemetry;

// Site metrics models: events flow from the request pipeline (visit tracking middleware,
// /dl download endpoint) through IAnalyticsSink into AnalyticsStore; AnalyticsSummary is the
// read shape consumed by the /admin dashboard.

/// <summary>
/// One page-view event.
/// <para>
/// <see cref="IpAddress"/> is the full client address. Spec 038 changed what happens to it: it is
/// persisted in full <b>only when the visitor has consented</b> (FR-038/FR-043). Without consent the
/// <c>ip</c> column stays NULL and the store writes only the per-day salted hash and the truncated
/// <see cref="IpAnonymizer.ToPrefix">network prefix</see>, as it always did.
/// </para>
/// <para>
/// The optional members carry the enrichment added for analysis (device/OS/browser detail,
/// location, language, campaign, full referrer, response timing). All are nullable so a caller
/// that has none of them — a test, or a future non-HTTP source — still produces a valid record.
/// </para>
/// </summary>
public sealed record VisitInfo(DateTimeOffset Utc, string Path, string? ReferrerHost, string? UaFamily, string? IpAddress)
{
    /// <summary>Full referrer URL including path; null for same-origin or absent referrers.</summary>
    public string? ReferrerUrl { get; init; }

    /// <summary>Parsed user-agent detail; defaults to the unknown shape.</summary>
    public UserAgentDetails UserAgent { get; init; } = UserAgentDetailsParser.Unknown;

    /// <summary>Location derived from the full IP at write time.</summary>
    public GeoLocation Location { get; init; } = GeoLocation.Unknown;

    /// <summary>Primary Accept-Language tag, lower-cased ("en", "ar-eg").</summary>
    public string? Language { get; init; }

    /// <summary>UTM parameters carried on the inbound link.</summary>
    public CampaignInfo Campaign { get; init; } = CampaignInfo.None;

    /// <summary>Server-side handling time in milliseconds, for spotting slow pages.</summary>
    public int? DurationMs { get; init; }

    /// <summary>
    /// Spec 038 (US5): the visitor's persistent id from the <c>akml.vid</c> cookie. Non-null only
    /// when <see cref="Consent"/> is <see cref="ConsentState.Granted"/>; the store enforces that
    /// independently of this caller.
    /// </summary>
    public string? VisitorId { get; init; }

    /// <summary>
    /// The visitor's recorded consent choice. Written to every row so the unattributed share is a
    /// query rather than an inference (FR-047).
    /// </summary>
    public ConsentState Consent { get; init; } = ConsentState.Unknown;
}

/// <summary>
/// One installer download event. Spec 038: <see cref="IpAddress"/> is persisted in full only with
/// the visitor's consent; otherwise only the per-day hash, the truncated prefix and the derived
/// location are stored.
/// <para>
/// Carries the same acquisition context as a visit so "which campaign produced installs?" is
/// answerable directly, without joining back through sessions.
/// </para>
/// </summary>
public sealed record DownloadInfo(DateTimeOffset Utc, string File, string? ReferrerHost, string? UaFamily, string? IpAddress)
{
    /// <summary>Full referrer URL including path; null for same-origin or absent referrers.</summary>
    public string? ReferrerUrl { get; init; }

    /// <summary>Parsed user-agent detail.</summary>
    public UserAgentDetails UserAgent { get; init; } = UserAgentDetailsParser.Unknown;

    /// <summary>Location derived from the full IP at write time.</summary>
    public GeoLocation Location { get; init; } = GeoLocation.Unknown;

    /// <summary>Primary Accept-Language tag.</summary>
    public string? Language { get; init; }

    /// <summary>UTM parameters carried on the inbound link.</summary>
    public CampaignInfo Campaign { get; init; } = CampaignInfo.None;

    /// <summary>Spec 038 (US5): persistent visitor id; non-null only with consent.</summary>
    public string? VisitorId { get; init; }

    /// <summary>Spec 038 (US5): the visitor's recorded consent choice, written on every row.</summary>
    public ConsentState Consent { get; init; } = ConsentState.Unknown;

    /// <summary>
    /// Spec 038 (US3): the release this file belongs to, resolved from the manifest AT WRITE TIME.
    /// Resolving at read time was rejected — the manifest is mutable, and a release later removed
    /// from it would retroactively orphan its historical downloads.
    /// </summary>
    public string? ReleaseVersion { get; init; }
}

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

    /// <summary>Queues a batch of anonymous client error reports for background persistence (dropped silently when the queue is full).</summary>
    void EnqueueClientErrors(ClientErrorBatch batch);
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
    /// Automated page views within the window: crawlers AND scripted clients (curl, wget,
    /// PowerShell, and friends). Excluded from every visitor figure — counting them inflated
    /// every headline number, the session shape and the top-pages ranking — but reported here
    /// rather than silently discarded, because a spike explains an otherwise quiet week.
    /// <para>
    /// Downloads deliberately do NOT apply this exclusion: fetching an installer with curl is a
    /// real acquisition. Only crawlers are dropped there.
    /// </para>
    /// </summary>
    public required long AutomatedVisitsWindow { get; init; }

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

    // --- Enrichment dimensions ---------------------------------------------

    /// <summary>
    /// Visits by country ("Egypt", "United Kingdom"); empty without a geo database. Country is
    /// the only location recorded — see <see cref="GeoLocation"/> for why nothing finer is.
    /// </summary>
    public required IReadOnlyList<CountRow> Countries { get; init; }

    /// <summary>Visits by form factor: desktop / mobile / tablet.</summary>
    public required IReadOnlyList<CountRow> Devices { get; init; }

    /// <summary>Visits by operating system, with version ("Windows 10/11", "iOS 17.2").</summary>
    public required IReadOnlyList<CountRow> OperatingSystems { get; init; }

    /// <summary>Visits by primary Accept-Language tag.</summary>
    public required IReadOnlyList<CountRow> Languages { get; init; }

    /// <summary>Visits by UTM campaign source/medium/campaign, most specific available.</summary>
    public required IReadOnlyList<CountRow> Campaigns { get; init; }

    /// <summary>Visits by full referrer URL — which page linked here, not just which host.</summary>
    public required IReadOnlyList<CountRow> ReferrerUrls { get; init; }

    /// <summary>Slowest pages by mean server handling time, in milliseconds.</summary>
    public required IReadOnlyList<CountRow> SlowestPages { get; init; }

    /// <summary>Pages that most often begin a session — where people actually arrive.</summary>
    public required IReadOnlyList<CountRow> EntryPages { get; init; }

    /// <summary>Pages that most often end a session — where people leave.</summary>
    public required IReadOnlyList<CountRow> ExitPages { get; init; }

    /// <summary>Distinct sessions in the window.</summary>
    public required long Sessions { get; init; }

    /// <summary>Percentage of sessions with exactly one page view (0-100).</summary>
    public required double BounceRatePercent { get; init; }

    /// <summary>Mean pages per session.</summary>
    public required double PagesPerSession { get; init; }

    /// <summary>Mean session length in seconds (single-page sessions count as 0).</summary>
    public required double AverageSessionSeconds { get; init; }
}

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Read models for the portal's downloads and people views (spec 038 US3; contract
/// metrics-read-model).
/// <para>
/// Nothing here is a stored table. An <c>individuals</c> table would duplicate facts the event rows
/// already hold and would need its own retention and deletion path; deriving it means a deletion
/// request is a delete on two event tables with nothing left behind.
/// </para>
/// </summary>
public static class Individuals
{
    /// <summary>Rows returned per page of the people list.</summary>
    public const int DefaultPageSize = 50;
}

/// <summary>Downloads grouped by country, including the explicit unknown bucket.</summary>
/// <param name="Country">Display name, or null for the Unknown bucket.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2, or null.</param>
/// <param name="Count">Downloads in the window.</param>
/// <param name="SharePercent">Share of the window total, to one decimal.</param>
public sealed record DownloadCountryRow(string? Country, string? CountryCode, long Count, double SharePercent)
{
    /// <summary>Label for display; never blank, so a row is never invisible.</summary>
    public string Label => string.IsNullOrWhiteSpace(Country) ? "Unknown" : Country;
}

/// <summary>Downloads grouped by the release the file belongs to.</summary>
/// <param name="ReleaseVersion">Version, or null for downloads of a file no manifest entry names.</param>
/// <param name="File">Installer file name.</param>
/// <param name="Count">Downloads in the window.</param>
/// <param name="SharePercent">Share of the window total, to one decimal.</param>
public sealed record DownloadVersionRow(string? ReleaseVersion, string? File, long Count, double SharePercent)
{
    /// <summary>Label for display; unattributed downloads are named, never dropped.</summary>
    public string Label => string.IsNullOrWhiteSpace(ReleaseVersion) ? "Unattributed" : ReleaseVersion;
}

/// <summary>One individual as the people list shows them.</summary>
public sealed record IndividualRow(
    string VisitorId,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    string? Country,
    string? CountryCode,
    string? IpAddress,
    string? NetworkPrefix,
    string? Device,
    string? Os,
    string? Browser,
    long VisitCount,
    long DownloadCount,
    bool IsReturning)
{
    /// <summary>Whether this individual has ever downloaded an installer within the window.</summary>
    public bool Downloaded => DownloadCount > 0;

    /// <summary>Shortened id for display; the full value is the link target.</summary>
    public string ShortId => VisitorId.Length <= 8 ? VisitorId : VisitorId[..8];
}

/// <summary>One entry in an individual's interleaved activity stream.</summary>
/// <param name="Utc">When it happened.</param>
/// <param name="Kind"><c>visit</c> or <c>download</c>.</param>
/// <param name="Target">Page path, or installer file name.</param>
/// <param name="ReleaseVersion">Release version for a download; null otherwise.</param>
/// <param name="ReferrerUrl">Where they came from, when known.</param>
/// <param name="Campaign">Campaign that brought them, when known.</param>
public sealed record IndividualActivity(
    DateTimeOffset Utc,
    string Kind,
    string Target,
    string? ReleaseVersion,
    string? ReferrerUrl,
    string? Campaign);

/// <summary>One individual plus their history, in time order.</summary>
/// <param name="Summary">The same shape the list shows.</param>
/// <param name="Activity">
/// Visits and downloads interleaved into ONE stream. Two separate tables would not answer the
/// question being asked — "what did this person do" — which is inherently chronological.
/// </param>
public sealed record IndividualDetail(IndividualRow Summary, IReadOnlyList<IndividualActivity> Activity);

/// <summary>Filters applied to the people list; every visible figure honours them (FR-025).</summary>
/// <param name="Days">Reporting window.</param>
/// <param name="CountryCode">Restrict to one country, or null for all.</param>
/// <param name="Downloaded">Restrict to people who did or did not download, or null for all.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record IndividualFilter(
    int Days,
    string? CountryCode = null,
    bool? Downloaded = null,
    int Page = 0,
    int PageSize = Individuals.DefaultPageSize);

/// <summary>
/// What the individual-level figures can and cannot see.
/// <para>
/// Required alongside any people view. Without it the list reads as the whole audience, which it
/// never is: the owner chose to ask every visitor and block none, so most traffic is unattributed
/// by design (FR-047, contract M3.1).
/// </para>
/// </summary>
public sealed record CoverageSummary(
    long AttributedVisits,
    long UnattributedVisits,
    long AutomatedVisits,
    long DistinctIndividuals,
    long NewIndividuals,
    long ReturningIndividuals)
{
    /// <summary>Total human visits in the window, attributed or not.</summary>
    public long TotalVisits => AttributedVisits + UnattributedVisits;

    /// <summary>Percentage of visits that cannot be tied to an individual, to one decimal.</summary>
    public double UnattributedSharePercent =>
        TotalVisits <= 0 ? 0 : Math.Round(UnattributedVisits * 100.0 / TotalVisits, 1);
}

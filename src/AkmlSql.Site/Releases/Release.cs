namespace AkmlSql.Site.Releases;

/// <summary>
/// One installer release from the checked-in <c>wwwroot/releases.json</c> feed
/// (spec 034; schema contract: specs/034-blazor-product-site/contracts/releases-json.md).
/// Field names intentionally mirror <c>AkmlSql.Core.Update.UpdateManifest</c> (camelCase).
/// </summary>
public sealed record Release
{
    /// <summary>Product version, SemVer-ish <c>1.YY.MMDD.HHmm</c> (e.g. <c>1.0.0</c>).</summary>
    public required string Version { get; init; }

    /// <summary>Release date displayed on the download page (FR-003).</summary>
    public required DateOnly ReleasedAt { get; init; }

    /// <summary>Hosts this release installs into, e.g. <c>SSMS 22</c>, <c>VS 2026</c> (FR-003).</summary>
    public required IReadOnlyList<string> SupportedHosts { get; init; }

    /// <summary>Installer artifact location (host downloads folder or a future GitHub Release asset).</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>SHA-256 hex digest (64 chars) of the installer, displayed for verification.</summary>
    public required string Sha256Hash { get; init; }

    /// <summary>Optional release-notes page URL.</summary>
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>Optional short summary of what shipped.</summary>
    public string? NotesSummary { get; init; }

    /// <summary>Optional minimum Windows version (carried from the updater schema).</summary>
    public string? MinimumOsVersion { get; init; }

    /// <summary>
    /// Optional CDN mirror for the installer (absolute http/https, e.g. a GitHub Releases
    /// asset). When set, the tracked <c>/dl/</c> endpoint redirects here instead of streaming
    /// the file from this server — the download is still counted first.
    /// </summary>
    public string? CdnUrl { get; init; }

    /// <summary>Derived by <see cref="ReleasesManifest"/>: true only for the newest entry. Never stored.</summary>
    public bool IsLatest { get; init; }
}

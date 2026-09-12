using AkmlSql.Site.Analytics;

namespace AkmlSql.Site.Releases;

/// <summary>
/// DL-001/DL-003: resolves whether the installer a release advertises can actually be obtained, and
/// how big it is.
/// <para>
/// The download page previously rendered whatever <c>releases.json</c> claimed, so a manifest entry
/// pointing at a missing file turned the site's primary call-to-action into a 404 — the worst
/// failure the site has, and a silent one. Size is read from the file rather than added to the
/// manifest schema so it cannot disagree with what visitors actually download.
/// </para>
/// <para>
/// <b>Spec 038 (US1)</b>: availability is now CDN-aware. Previously <see cref="IsLocal"/> was decided
/// purely from the <c>downloadUrl</c> prefix — true for every current release — so the page demanded
/// the local file even for releases that <see cref="DownloadEndpoint"/> serves by redirecting to their
/// GitHub mirror without ever touching the downloads folder. The page's "can I offer this?" and the
/// endpoint's "will I serve this?" had drifted apart, which is the exact disagreement this class
/// exists to prevent. A release with a CDN mirror is now offered without any filesystem probe.
/// </para>
/// </summary>
public sealed class ReleaseAvailability
{
    /// <summary>Manifest URL prefix that means "served from the local downloads folder".</summary>
    public const string LocalPrefix = "downloads/";

    private readonly string _downloadsFolder;
    private readonly Func<string, string?> _resolveFile;

    public ReleaseAvailability(string downloadsFolder)
        : this(downloadsFolder, resolveFile: null)
    {
    }

    /// <summary>
    /// Test seam (spec 038 T022): <paramref name="resolveFile"/> replaces the filesystem probe so a
    /// test can COUNT probes. SC-003 — "render cost does not vary with manifest size" — is gated by
    /// counting, not by wall-clock timing, which would be flaky in CI and get muted.
    /// </summary>
    internal ReleaseAvailability(string downloadsFolder, Func<string, string?>? resolveFile)
    {
        _downloadsFolder = downloadsFolder ?? "";
        _resolveFile = resolveFile
            ?? (relativePath => DownloadEndpoint.ResolveFilePath(_downloadsFolder, relativePath));
    }

    /// <summary>True when the release is served from the local folder (rather than an absolute URL).</summary>
    public static bool IsLocal(Release release) =>
        release is not null && release.DownloadUrl.StartsWith(LocalPrefix, StringComparison.Ordinal);

    /// <summary>True when the release carries a CDN mirror, so no local file is required.</summary>
    public static bool HasCdnMirror(Release release) =>
        release is not null && !string.IsNullOrWhiteSpace(release.CdnUrl);

    /// <summary>
    /// Tracked download URL for a release: local manifest URLs route through <c>/dl/{file}</c> so
    /// the download is counted; absolute http(s) URLs (e.g. a GitHub asset) pass through untouched.
    /// </summary>
    public static string TrackedUrl(Release release) =>
        IsLocal(release) ? "/dl/" + release.DownloadUrl[LocalPrefix.Length..] : release.DownloadUrl;

    /// <summary>
    /// True when the release can actually be downloaded.
    /// <para>
    /// Three cases, cheapest first:
    /// a CDN mirror means yes with <b>no</b> filesystem access (and deliberately no network access —
    /// a per-render request to GitHub would put a third-party hop on the site's primary page, exactly
    /// what <see cref="GeoLookup"/> is offline to avoid); a non-local absolute URL means yes, because
    /// we cannot check it and need not; a local release means yes only if its file is present.
    /// </para>
    /// </summary>
    public bool IsDownloadable(Release release)
    {
        if (release is null)
        {
            return false;
        }

        if (HasCdnMirror(release))
        {
            return true;
        }

        return !IsLocal(release) || ResolveFile(release) is not null;
    }

    /// <summary>Size in bytes of a locally hosted installer, or null when unknown/remote/missing.</summary>
    public long? SizeBytes(Release release)
    {
        var path = release is not null && IsLocal(release) ? ResolveFile(release) : null;
        if (path is null)
        {
            return null;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Human-readable size ("66.3 MB"), or null when the size is unknown — a CDN-only release shows
    /// no size rather than a guessed one.
    /// </summary>
    public string? DisplaySize(Release release) => SizeBytes(release) is { } bytes ? FormatSize(bytes) : null;

    /// <summary>Formats a byte count for display; shared shape with the admin file listing.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024L => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>
    /// Full path of the release's installer, or null when it is missing or escapes the folder.
    /// Reuses the download endpoint's canonical resolver so the page's "is it there?" answer and
    /// the endpoint's "will I serve it?" answer can never disagree.
    /// </summary>
    private string? ResolveFile(Release release) =>
        _resolveFile(release.DownloadUrl[LocalPrefix.Length..]);
}

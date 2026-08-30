using AkmlSql.Site.Analytics;

namespace AkmlSql.Site.Releases;

/// <summary>
/// DL-001/DL-003: resolves whether the installer a release advertises is actually present in the
/// downloads folder, and how big it is.
/// <para>
/// The download page previously rendered whatever <c>releases.json</c> claimed, so a manifest
/// entry pointing at a missing file turned the site's primary call-to-action into a 404 — the
/// worst failure the site has, and a silent one. Size is read from the file rather than added to
/// the manifest schema so it cannot disagree with what visitors actually download.
/// </para>
/// </summary>
public sealed class ReleaseAvailability
{
    /// <summary>Manifest URL prefix that means "served from the local downloads folder".</summary>
    public const string LocalPrefix = "downloads/";

    private readonly string _downloadsFolder;

    public ReleaseAvailability(string downloadsFolder) => _downloadsFolder = downloadsFolder ?? "";

    /// <summary>True when the release is served from the local folder (rather than an absolute URL).</summary>
    public static bool IsLocal(Release release) =>
        release is not null && release.DownloadUrl.StartsWith(LocalPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Tracked download URL for a release: local manifest URLs route through <c>/dl/{file}</c> so
    /// the download is counted; absolute http(s) URLs (e.g. a GitHub asset) pass through untouched.
    /// </summary>
    public static string TrackedUrl(Release release) =>
        IsLocal(release) ? "/dl/" + release.DownloadUrl[LocalPrefix.Length..] : release.DownloadUrl;

    /// <summary>
    /// True when the release can actually be downloaded: either it is hosted elsewhere (we cannot
    /// and need not check) or its file is present in the downloads folder.
    /// </summary>
    public bool IsDownloadable(Release release)
    {
        if (release is null)
        {
            return false;
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

    /// <summary>Human-readable size ("66.3 MB"), or null when the size is unknown.</summary>
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
        DownloadEndpoint.ResolveFilePath(_downloadsFolder, release.DownloadUrl[LocalPrefix.Length..]);
}

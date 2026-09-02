namespace AkmlSql.Site.Analytics;

using AkmlSql.Site.Releases;

/// <summary>Configuration binding for the <c>Downloads</c> section of appsettings.json.</summary>
public sealed class DownloadsOptions
{
    public const string SectionName = "Downloads";

    /// <summary>
    /// Folder holding the installer files served via <c>/dl/{file}</c>. Lives OUTSIDE the app
    /// root so deploying the site never touches release binaries.
    /// </summary>
    public string Folder { get; set; } = @"C:\inetpub\akml.khamis.work-downloads";
}

/// <summary>
/// Tracked installer download endpoint: <c>GET /dl/{**file}</c> streams a file from the
/// configured downloads folder after logging the download. Path handling is canonical —
/// the requested path is combined with the folder, normalized with <see cref="Path.GetFullPath(string)"/>,
/// and rejected unless the result stays strictly under the folder (traversal, backslashes,
/// absolute paths, and URL-encoded tricks all collapse to 404).
/// </summary>
public static class DownloadEndpoint
{
    /// <summary>Registers the <c>/dl/{**file}</c> route.</summary>
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/dl/{**file}", (
            HttpContext http,
            string? file,
            Microsoft.Extensions.Options.IOptions<DownloadsOptions> options,
            IAnalyticsSink sink,
            GeoLookup geo,
            ReleasesManifest manifest) => Handle(file, http, options.Value, sink, geo, manifest));

    /// <summary>
    /// Canonical path resolution: returns the full file path to serve, or null (→ 404) when the
    /// request escapes the downloads folder, points at a directory, or names a missing file.
    /// </summary>
    public static string? ResolveFilePath(string rootFolder, string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return null;
        }

        string candidate;
        try
        {
            var root = Path.GetFullPath(rootFolder);
            candidate = Path.GetFullPath(Path.Combine(root, requestPath));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (Directory.Exists(candidate) || !File.Exists(candidate))
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// CDN lookup: when the manifest entry for the requested file carries a <c>cdnUrl</c>, the
    /// endpoint 302-redirects there (GitHub Releases etc.) instead of streaming — the request
    /// still hits this server first, so the download is counted. Restricted to simple filenames
    /// (any slash/backslash/traversal segment falls through to the canonical local path logic,
    /// which 404s it).
    /// </summary>
    public static string? ResolveCdnUrl(ReleasesManifest? manifest, string? requestPath)
    {
        if (manifest is null
            || string.IsNullOrWhiteSpace(requestPath)
            || requestPath.Contains('/')
            || requestPath.Contains('\\')
            || requestPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var release in manifest.Releases)
        {
            if (release.CdnUrl is not null
                && string.Equals(Path.GetFileName(release.DownloadUrl), requestPath, StringComparison.OrdinalIgnoreCase))
            {
                return release.CdnUrl;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers the <c>POST /dl-count/{**file}</c> beacon route: JS-upgraded download links go
    /// straight to the CDN (no /dl redirect hop for the visitor) and report the click here so
    /// the metric is still counted (DL-004).
    /// </summary>
    public static void MapCount(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/dl-count/{**file}", (
            HttpContext http,
            string? file,
            Microsoft.Extensions.Options.IOptions<DownloadsOptions> options,
            IAnalyticsSink sink,
            GeoLookup geo,
            ReleasesManifest manifest) => HandleCount(file, http, options.Value, sink, geo, manifest));

    /// <summary>
    /// Beacon handler: counts a CDN download only for a file the site actually offers (present
    /// in the manifest with a CDN mirror, or streamable locally) — anything else is a 404, so
    /// the counter cannot be inflated with invented filenames.
    /// </summary>
    public static IResult HandleCount(
        string? file,
        HttpContext http,
        DownloadsOptions options,
        IAnalyticsSink sink,
        GeoLookup? geo = null,
        ReleasesManifest? manifest = null)
    {
        if (ResolveCdnUrl(manifest, file) is null && ResolveFilePath(options.Folder, file) is null)
        {
            return Results.NotFound();
        }

        LogDownload(http, file!, sink, geo);
        return Results.NoContent();
    }

    /// <summary>
    /// Handler body, factored out for tests: 404 for anything that fails canonical resolution,
    /// 302 to the CDN mirror when the manifest provides one, otherwise logs the download
    /// (best-effort) and streams the file as an attachment.
    /// </summary>
    public static IResult Handle(
        string? file,
        HttpContext http,
        DownloadsOptions options,
        IAnalyticsSink sink,
        GeoLookup? geo = null,
        ReleasesManifest? manifest = null)
    {
        // CDN fast path: count the download here, then let the CDN serve the bytes.
        var cdnUrl = ResolveCdnUrl(manifest, file);
        if (cdnUrl is not null)
        {
            LogDownload(http, file!, sink, geo);
            return Results.Redirect(cdnUrl, permanent: false);
        }

        var fullPath = ResolveFilePath(options.Folder, file);
        if (fullPath is null)
        {
            return Results.NotFound();
        }

        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deleted/locked between the existence check and the open — indistinguishable from absent.
            return Results.NotFound();
        }

        var fileName = Path.GetFileName(fullPath);

        // DL-002 (range requests don't count) lives inside LogDownload.
        LogDownload(http, fileName, sink, geo);

        http.Response.Headers.CacheControl = "no-cache";

        // DL-002: enableRangeProcessing + a last-modified stamp, so a dropped connection resumes
        // instead of restarting from zero. Without them the whole installer is re-fetched.
        DateTimeOffset? lastModified = null;
        try
        {
            lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Serve without the validator rather than failing the download.
        }

        return Results.File(
            stream,
            "application/octet-stream",
            fileDownloadName: fileName,
            lastModified: lastModified,
            entityTag: null,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Best-effort download logging, shared by the CDN-redirect and local-stream branches.
    /// DL-002: a range request is a resumed transfer, not a new download — counting it would
    /// inflate the metric every time a 66 MB installer drops its connection.
    /// </summary>
    private static void LogDownload(HttpContext http, string fileName, IAnalyticsSink sink, GeoLookup? geo)
    {
        if (http.Request.Headers.ContainsKey("Range"))
        {
            return;
        }

        try
        {
            var ip = HttpRequestFacts.ClientIp(http);
            var userAgent = UserAgentDetailsParser.Parse(http.Request.Headers.UserAgent);

            sink.EnqueueDownload(new DownloadInfo(
                DateTimeOffset.UtcNow,
                Path.GetFileName(fileName),
                HttpRequestFacts.ReferrerHost(http.Request),
                userAgent.Browser,
                ip)
            {
                ReferrerUrl = HttpRequestFacts.ReferrerUrl(http.Request),
                UserAgent = userAgent,
                Location = geo?.Locate(ip) ?? GeoLocation.Unknown,
                Language = HttpRequestFacts.Language(http.Request),
                Campaign = HttpRequestFacts.Campaign(http.Request),
            });
        }
        catch
        {
            // Metrics must never break the download.
        }
    }
}

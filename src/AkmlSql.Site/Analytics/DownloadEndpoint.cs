namespace AkmlSql.Site.Analytics;

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
            GeoLookup geo) => Handle(file, http, options.Value, sink, geo));

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
    /// Handler body, factored out for tests: 404 for anything that fails canonical resolution,
    /// otherwise logs the download (best-effort) and streams the file as an attachment.
    /// </summary>
    public static IResult Handle(
        string? file,
        HttpContext http,
        DownloadsOptions options,
        IAnalyticsSink sink,
        GeoLookup? geo = null)
    {
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

        // DL-002: a range request is a resumed transfer, not a new download — counting it would
        // inflate the metric every time a 66 MB installer drops its connection.
        var isRangeRequest = http.Request.Headers.ContainsKey("Range");
        if (!isRangeRequest)
        {
            try
            {
                var ip = HttpRequestFacts.ClientIp(http);
                var userAgent = UserAgentDetailsParser.Parse(http.Request.Headers.UserAgent);

                sink.EnqueueDownload(new DownloadInfo(
                    DateTimeOffset.UtcNow,
                    fileName,
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
}

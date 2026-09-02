using AkmlSql.Site.Analytics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// /dl endpoint: canonical path validation (traversal, backslashes, rooted paths, URL-encoded
/// segments all rejected), 404 for missing files/directories, and the happy path — streamed
/// bytes, attachment headers, no-cache, and exactly one logged download.
/// </summary>
public sealed class DownloadEndpointTests
{
    [Theory]
    [InlineData("../web.config")]
    [InlineData("..\\web.config")]
    [InlineData("sub/../../web.config")]
    [InlineData("../../etc/passwd")]
    [InlineData("%2e%2e%2fweb.config")]   // decoded by routing before binding; raw form resolves to a non-existent literal name
    [InlineData("%2e%2e%5cweb.config")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveFilePath_RejectsTraversalAndEmptyInput(string? requestPath)
    {
        using var dir = new TempDirectory();

        Assert.Null(DownloadEndpoint.ResolveFilePath(dir.Path, requestPath));
    }

    [Fact]
    public void ResolveFilePath_RejectsRootedAbsolutePaths()
    {
        using var dir = new TempDirectory();
        var outside = Path.Combine(dir.Path, "..", "outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "secret");
        try
        {
            Assert.Null(DownloadEndpoint.ResolveFilePath(dir.Path, Path.GetFullPath(outside)));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void ResolveFilePath_ReturnsNull_ForMissingFileAndDirectories()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));

        Assert.Null(DownloadEndpoint.ResolveFilePath(dir.Path, "missing.exe"));
        Assert.Null(DownloadEndpoint.ResolveFilePath(dir.Path, "sub"));
    }

    [Fact]
    public void ResolveFilePath_ResolvesFileInsideRoot()
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "setup.exe");
        File.WriteAllText(file, "payload");

        Assert.Equal(Path.GetFullPath(file), DownloadEndpoint.ResolveFilePath(dir.Path, "setup.exe"));
    }

    [Fact]
    public async Task Handle_StreamsFile_LogsDownload_AndSetsAttachmentHeaders()
    {
        using var dir = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(Path.Combine(dir.Path, "setup.exe"), bytes);

        var sink = new RecordingSink();
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        // FileStreamHttpResult resolves an ILogger from RequestServices when executing.
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        var result = DownloadEndpoint.Handle("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink);

        var fileResult = Assert.IsType<FileStreamHttpResult>(result);
        Assert.Equal("application/octet-stream", fileResult.ContentType);
        Assert.Equal("setup.exe", fileResult.FileDownloadName);
        Assert.Equal("no-cache", http.Response.Headers.CacheControl.ToString());

        await fileResult.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        var streamed = ((MemoryStream)http.Response.Body).ToArray();
        Assert.Equal(bytes, streamed);
        Assert.StartsWith("attachment", http.Response.Headers.ContentDisposition.ToString());

        var download = Assert.Single(sink.Downloads);
        Assert.Equal("setup.exe", download.File);
    }

    [Fact]
    public void Handle_MissingFile_Returns404AndLogsNothing()
    {
        using var dir = new TempDirectory();
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();

        var result = DownloadEndpoint.Handle("../setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink);

        var notFound = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Empty(sink.Downloads);
    }

    [Fact]
    public void DownloadsInfo_ListsFilesNewestFirst_AndToleratesMissingFolder()
    {
        using var dir = new TempDirectory();
        var older = Path.Combine(dir.Path, "a.exe");
        var newer = Path.Combine(dir.Path, "b.exe");
        File.WriteAllText(older, "1234");
        File.WriteAllText(newer, "12");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));

        var files = DownloadsInfo.List(dir.Path);

        Assert.Equal(2, files.Count);
        Assert.Equal("b.exe", files[0].Name);
        Assert.Equal(2, files[0].SizeBytes);
        Assert.Equal("a.exe", files[1].Name);
        Assert.Equal(4, files[1].SizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero), files[0].LastWriteUtc);

        Assert.Empty(DownloadsInfo.List(Path.Combine(dir.Path, "does-not-exist")));
    }

    /// <summary>In-memory <see cref="IAnalyticsSink"/> that records what would have been queued.</summary>
    private sealed class RecordingSink : IAnalyticsSink
    {
        public List<VisitInfo> Visits { get; } = [];
        public List<DownloadInfo> Downloads { get; } = [];
        public List<NotFoundInfo> NotFound { get; } = [];

        public void EnqueueVisit(VisitInfo visit) => Visits.Add(visit);

        public void EnqueueDownload(DownloadInfo download) => Downloads.Add(download);

        public void EnqueueNotFound(NotFoundInfo notFound) => NotFound.Add(notFound);
    }

    // --- CDN redirect (/dl -> GitHub Releases etc.) ---

    private static AkmlSql.Site.Releases.ReleasesManifest ManifestWithCdn(string fileName, string? cdnUrl) =>
        AkmlSql.Site.Releases.ReleasesManifest.Create(
        [
            new AkmlSql.Site.Releases.Release
            {
                Version = "1.0.0",
                ReleasedAt = new DateOnly(2026, 9, 1),
                SupportedHosts = ["SSMS 22"],
                DownloadUrl = $"downloads/{fileName}",
                Sha256Hash = new string('0', 64),
                CdnUrl = cdnUrl,
            },
        ]);

    [Fact]
    public void Handle_ManifestHasCdnUrl_RedirectsAndLogsDownload()
    {
        using var dir = new TempDirectory();
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();
        var manifest = ManifestWithCdn("setup.exe", "https://cdn.example.com/releases/v1/setup.exe");

        var result = DownloadEndpoint.Handle("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink, manifest: manifest);

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("https://cdn.example.com/releases/v1/setup.exe", redirect.Url);
        Assert.False(redirect.Permanent);
        var download = Assert.Single(sink.Downloads);
        Assert.Equal("setup.exe", download.File);
    }

    [Fact]
    public void Handle_ManifestWithoutCdnUrl_StreamsLocalFile()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "setup.exe"), "payload");
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        var manifest = ManifestWithCdn("setup.exe", null);

        var result = DownloadEndpoint.Handle("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink, manifest: manifest);

        Assert.IsType<FileStreamHttpResult>(result);
    }

    [Theory]
    [InlineData("../setup.exe")]
    [InlineData("sub/setup.exe")]
    [InlineData("..\\setup.exe")]
    public void Handle_CdnLookupIgnoresTraversalAndSubpaths(string? requestPath)
    {
        var manifest = ManifestWithCdn("setup.exe", "https://cdn.example.com/setup.exe");

        Assert.Null(DownloadEndpoint.ResolveCdnUrl(manifest, requestPath));
    }

    [Fact]
    public void ResolveCdnUrl_MatchesFilenameCaseInsensitively_AndRejectsUnknownFiles()
    {
        var manifest = ManifestWithCdn("AKMLSQLSetup-1.0.0.exe", "https://cdn.example.com/x.exe");

        Assert.Equal("https://cdn.example.com/x.exe", DownloadEndpoint.ResolveCdnUrl(manifest, "akmlsqlsetup-1.0.0.EXE"));
        Assert.Null(DownloadEndpoint.ResolveCdnUrl(manifest, "other.exe"));
    }

    // --- /dl-count beacon (DL-004) ---

    [Fact]
    public void HandleCount_CdnMirroredFile_Returns204AndLogsDownload()
    {
        using var dir = new TempDirectory();
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();
        var manifest = ManifestWithCdn("setup.exe", "https://cdn.example.com/setup.exe");

        var result = DownloadEndpoint.HandleCount("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink, manifest: manifest);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var download = Assert.Single(sink.Downloads);
        Assert.Equal("setup.exe", download.File);
    }

    [Fact]
    public void HandleCount_LocallyPresentFileWithoutCdn_Returns204AndLogsDownload()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "setup.exe"), "payload");
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();

        var result = DownloadEndpoint.HandleCount("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Single(sink.Downloads);
    }

    [Theory]
    [InlineData("invented.exe")]
    [InlineData("../setup.exe")]
    [InlineData(null)]
    public void HandleCount_UnknownOrInvalidFile_Returns404AndLogsNothing(string? requestPath)
    {
        using var dir = new TempDirectory();
        var sink = new RecordingSink();
        var http = new DefaultHttpContext();
        var manifest = ManifestWithCdn("setup.exe", "https://cdn.example.com/setup.exe");

        var result = DownloadEndpoint.HandleCount(requestPath, http, new DownloadsOptions { Folder = dir.Path }, sink, manifest: manifest);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Empty(sink.Downloads);
    }
}

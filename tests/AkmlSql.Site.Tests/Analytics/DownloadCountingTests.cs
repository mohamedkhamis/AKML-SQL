using AkmlSql.Site.Analytics;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T025 (US1): download counting integrity — FR-005 and SC-009.
/// <para>
/// The installer is ~95 MB, so dropped connections and resumed transfers are routine. DL-002 added
/// range support precisely so a dropped download resumes instead of restarting; the counting guard
/// that stops each resumed chunk being counted as a fresh acquisition had no test until now.
/// </para>
/// </summary>
public sealed class DownloadCountingTests
{
    private sealed class RecordingSink : IAnalyticsSink
    {
        public List<VisitInfo> Visits { get; } = [];
        public List<DownloadInfo> Downloads { get; } = [];
        public List<NotFoundInfo> NotFound { get; } = [];
        public List<ClientErrorBatch> ClientErrors { get; } = [];

        public void EnqueueVisit(VisitInfo visit) => Visits.Add(visit);
        public void EnqueueDownload(DownloadInfo download) => Downloads.Add(download);
        public void EnqueueNotFound(NotFoundInfo notFound) => NotFound.Add(notFound);
        public void EnqueueClientErrors(ClientErrorBatch batch) => ClientErrors.Add(batch);
    }

    private static DefaultHttpContext NewHttp()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        return http;
    }

    private static ReleasesManifest ManifestWithCdn(string fileName, string cdnUrl) =>
        ReleasesManifest.Create(
        [
            new Release
            {
                Version = "1.26.0910.2248",
                ReleasedAt = new DateOnly(2026, 9, 10),
                SupportedHosts = ["SSMS 22"],
                DownloadUrl = "downloads/" + fileName,
                Sha256Hash = new string('a', 64),
                CdnUrl = cdnUrl,
            },
        ]);

    [Fact]
    public void Handle_WithARangeHeader_StreamsButDoesNotCount()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(Path.Combine(dir.Path, "setup.exe"), new byte[4096]);

        var sink = new RecordingSink();
        var http = NewHttp();
        http.Request.Headers.Range = "bytes=1024-2047";

        DownloadEndpoint.Handle("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink);

        // FR-005: a range request is a RESUMED transfer, not a new download. Counting it would
        // inflate the metric every time a 95 MB installer drops its connection.
        Assert.Empty(sink.Downloads);
    }

    [Fact]
    public void Handle_WithoutARangeHeader_CountsExactlyOnce()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(Path.Combine(dir.Path, "setup.exe"), new byte[4096]);

        var sink = new RecordingSink();

        DownloadEndpoint.Handle("setup.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, sink);

        var download = Assert.Single(sink.Downloads);
        Assert.Equal("setup.exe", download.File);
    }

    [Fact]
    public void HandleCount_WithARangeHeader_DoesNotCount()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(Path.Combine(dir.Path, "setup.exe"), new byte[16]);

        var sink = new RecordingSink();
        var http = NewHttp();
        http.Request.Headers.Range = "bytes=0-15";

        DownloadEndpoint.HandleCount("setup.exe", http, new DownloadsOptions { Folder = dir.Path }, sink);

        // The beacon path shares the same guard, so a resumed CDN transfer cannot inflate the count
        // either.
        Assert.Empty(sink.Downloads);
    }

    [Fact]
    public void CdnRedirect_CountsOnceAndTheBeaconIsTheOtherPath_NotAnExtraCount()
    {
        // Each visitor takes exactly ONE of these two paths, never both:
        //   no JS  -> /dl/{file} 302s to the CDN and counts here
        //   JS     -> download-track.js rewrites the href straight to the CDN and beacons /dl-count
        // This pins that each path counts exactly one download, so neither can double-count.
        using var dir = new TempDirectory();
        const string Cdn = "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1/setup.exe";
        var manifest = ManifestWithCdn("setup.exe", Cdn);

        var redirectSink = new RecordingSink();
        DownloadEndpoint.Handle("setup.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, redirectSink, manifest: manifest);
        Assert.Single(redirectSink.Downloads);

        var beaconSink = new RecordingSink();
        DownloadEndpoint.HandleCount("setup.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, beaconSink, manifest: manifest);
        Assert.Single(beaconSink.Downloads);
    }

    [Fact]
    public void MetricsFailure_DoesNotBreakTheDownload()
    {
        // Spec 038 T031 / FR-041: a metrics outage must be invisible to the visitor.
        using var dir = new TempDirectory();
        File.WriteAllBytes(Path.Combine(dir.Path, "setup.exe"), new byte[64]);

        var result = DownloadEndpoint.Handle(
            "setup.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, new ThrowingSink());

        // The file is still served even though every enqueue threw.
        Assert.NotNull(result);
    }

    private sealed class ThrowingSink : IAnalyticsSink
    {
        public void EnqueueVisit(VisitInfo visit) => throw new InvalidOperationException("metrics down");
        public void EnqueueDownload(DownloadInfo download) => throw new InvalidOperationException("metrics down");
        public void EnqueueNotFound(NotFoundInfo notFound) => throw new InvalidOperationException("metrics down");
        public void EnqueueClientErrors(ClientErrorBatch batch) => throw new InvalidOperationException("metrics down");
    }
}

using AkmlSql.Site.Analytics;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T039 (US2): FR-015 — the visibility setting governs what the page <b>advertises</b>,
/// never what is <b>reachable</b>.
/// <para>
/// Links to installers are pasted into issues, emails and chat threads and live for years. Hiding a
/// release from the download page must not break a link somebody already shared, and the download
/// must still be counted. The strongest form of this guarantee is structural, so it is asserted that
/// way below: <see cref="DownloadEndpoint"/> has no reference to the settings store at all, and
/// therefore cannot consult it however the code is later edited.
/// </para>
/// </summary>
public sealed class DownloadEndpointIgnoresVisibilityTests
{
    private sealed class RecordingSink : IAnalyticsSink
    {
        public List<DownloadInfo> Downloads { get; } = [];

        public void EnqueueVisit(VisitInfo visit) { }
        public void EnqueueDownload(DownloadInfo download) => Downloads.Add(download);
        public void EnqueueNotFound(NotFoundInfo notFound) { }
        public void EnqueueClientErrors(ClientErrorBatch batch) { }
    }

    private static DefaultHttpContext NewHttp()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        return http;
    }

    [Fact]
    public void AHiddenReleasesDirectLink_StillServesAndStillCounts()
    {
        using var dir = new TempDirectory();

        // An older installer the page would NOT advertise under "latest only".
        File.WriteAllBytes(Path.Combine(dir.Path, "AKMLSQLSetup-old.exe"), new byte[128]);

        var sink = new RecordingSink();

        var result = DownloadEndpoint.Handle(
            "AKMLSQLSetup-old.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, sink);

        Assert.IsType<FileStreamHttpResult>(result);
        var download = Assert.Single(sink.Downloads);
        Assert.Equal("AKMLSQLSetup-old.exe", download.File);
    }

    [Fact]
    public void AHiddenReleasesCdnLink_StillRedirectsAndStillCounts()
    {
        using var dir = new TempDirectory();
        const string Cdn = "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v0/old.exe";

        var manifest = ReleasesManifest.Create(
        [
            new Release
            {
                Version = "1.0.0",
                ReleasedAt = new DateOnly(2026, 8, 28),
                SupportedHosts = ["SSMS 22"],
                DownloadUrl = "downloads/old.exe",
                Sha256Hash = new string('a', 64),
                CdnUrl = Cdn,
            },
        ]);

        var sink = new RecordingSink();

        var result = DownloadEndpoint.Handle(
            "old.exe", NewHttp(), new DownloadsOptions { Folder = dir.Path }, sink, manifest: manifest);

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal(Cdn, redirect.Url);
        Assert.Single(sink.Downloads);
    }

    [Fact]
    public void TheDownloadEndpoint_CannotConsultTheSettingsStoreAtAll()
    {
        // Structural guarantee (contract R2.4). A behavioural test alone would pass even if someone
        // later wired the setting in and only broke the hidden-release case; this fails the moment
        // the dependency appears.
        var endpointType = typeof(DownloadEndpoint);
        var settingsType = typeof(AkmlSql.Site.Settings.SiteSettingsStore);

        var referencesSettings = endpointType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(m => m.GetParameters())
            .Any(p => settingsType.IsAssignableFrom(p.ParameterType));

        Assert.False(
            referencesSettings,
            "DownloadEndpoint must not take a SiteSettingsStore: visibility governs advertising, not reachability (FR-015).");

        var fieldsReferenceSettings = endpointType
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Any(f => settingsType.IsAssignableFrom(f.FieldType));

        Assert.False(fieldsReferenceSettings);
    }
}

using AkmlSql.Site.Releases;
using Xunit;

namespace AkmlSql.Site.Tests.Releases;

/// <summary>
/// Spec 038 T020 (US1): availability resolution.
/// <para>
/// The defect these pin: every one of the 16 live releases carries BOTH a local
/// <c>downloads/…</c> path and a GitHub <c>cdnUrl</c>. The old rule decided "is this local?" purely
/// from the <c>downloadUrl</c> prefix, so it demanded the local file for all of them — while
/// <c>DownloadEndpoint</c> checks the CDN first and 302s to GitHub without touching the folder. The
/// moment an installer is cleaned up, the page silently stops offering a release whose download link
/// works perfectly.
/// </para>
/// </summary>
public sealed class ReleaseAvailabilityTests
{
    private static readonly DateOnly Released = new(2026, 9, 10);

    private static Release NewRelease(string downloadUrl, string? cdnUrl = null) => new()
    {
        Version = "1.26.0910.2248",
        ReleasedAt = Released,
        SupportedHosts = ["SSMS 22"],
        DownloadUrl = downloadUrl,
        Sha256Hash = new string('a', 64),
        CdnUrl = cdnUrl,
    };

    private const string CdnUrl =
        "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.26.0910.2248/AKMLSQLSetup-1.26.0910.2248.exe";

    /// <summary>A probe that records every path it was asked about and always reports "absent".</summary>
    private sealed class CountingProbe
    {
        private readonly string? _result;

        public CountingProbe(string? result = null) => _result = result;

        public List<string> Probed { get; } = [];

        public string? Resolve(string relativePath)
        {
            Probed.Add(relativePath);
            return _result;
        }
    }

    [Fact]
    public void IsDownloadable_WithACdnMirrorAndNoLocalFile_IsTrue()
    {
        var probe = new CountingProbe(result: null); // the local file does NOT exist
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        var release = NewRelease("downloads/AKMLSQLSetup-1.26.0910.2248.exe", CdnUrl);

        // This is the bug: the old rule returned false here and silently hid a working release.
        Assert.True(availability.IsDownloadable(release));
    }

    [Fact]
    public void IsDownloadable_WithACdnMirror_DoesNotTouchTheFilesystemAtAll()
    {
        var probe = new CountingProbe(result: null);
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        availability.IsDownloadable(NewRelease("downloads/setup.exe", CdnUrl));

        // Contract R1.1/R4.2: a CDN-backed release costs zero probes. With all 16 current releases
        // CDN-backed, this removes essentially all per-render filesystem work.
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public void IsDownloadable_LocalOnlyWithThePresentFile_IsTrue()
    {
        var probe = new CountingProbe(result: @"C:\downloads\setup.exe");
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        Assert.True(availability.IsDownloadable(NewRelease("downloads/setup.exe")));
        Assert.Equal(["setup.exe"], probe.Probed);
    }

    [Fact]
    public void IsDownloadable_LocalOnlyWithTheFileMissing_IsFalse()
    {
        var probe = new CountingProbe(result: null);
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        // FR-006: never offer a release that cannot actually be retrieved.
        Assert.False(availability.IsDownloadable(NewRelease("downloads/setup.exe")));
    }

    [Fact]
    public void IsDownloadable_WithAnAbsoluteNonCdnUrl_IsTrueWithoutProbing()
    {
        var probe = new CountingProbe(result: null);
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        // Hosted elsewhere: we cannot check it and need not. Existing behaviour, preserved.
        Assert.True(availability.IsDownloadable(NewRelease("https://example.com/setup.exe")));
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public void IsDownloadable_WithNullRelease_IsFalse()
    {
        var availability = new ReleaseAvailability(@"C:\downloads");

        Assert.False(availability.IsDownloadable(null!));
    }

    [Fact]
    public void IsDownloadable_WithAnEmptyCdnUrl_FallsBackToTheLocalProbe()
    {
        var probe = new CountingProbe(result: null);
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        // Whitespace is not a mirror — it must not short-circuit the check.
        Assert.False(availability.IsDownloadable(NewRelease("downloads/setup.exe", "   ")));
        Assert.Single(probe.Probed);
    }

    [Fact]
    public void DisplaySize_ForACdnOnlyRelease_IsNullRatherThanGuessed()
    {
        var probe = new CountingProbe(result: null);
        var availability = new ReleaseAvailability(@"C:\downloads", probe.Resolve);

        // Contract R1.4: no local file means no size. The page omits the row rather than inventing
        // a number that would disagree with what the visitor actually downloads.
        Assert.Null(availability.DisplaySize(NewRelease("downloads/setup.exe", CdnUrl)));
    }

    [Fact]
    public void DisplaySize_ForALocalRelease_ReadsTheFile()
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "setup.exe");
        File.WriteAllBytes(file, new byte[2048]);

        var availability = new ReleaseAvailability(dir.Path);

        Assert.Equal("2 KB", availability.DisplaySize(NewRelease("downloads/setup.exe")));
    }

    [Fact]
    public void TrackedUrl_RoutesLocalReleasesThroughTheCountingEndpoint()
    {
        // Counting depends on the request reaching this server first, even when it then redirects.
        Assert.Equal("/dl/setup.exe", ReleaseAvailability.TrackedUrl(NewRelease("downloads/setup.exe", CdnUrl)));
        Assert.Equal("https://example.com/x.exe", ReleaseAvailability.TrackedUrl(NewRelease("https://example.com/x.exe")));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(69483456, "66.3 MB")]
    public void FormatSize_MatchesTheDisplayUsedAcrossThePageAndPortal(long bytes, string expected)
    {
        Assert.Equal(expected, ReleaseAvailability.FormatSize(bytes));
    }
}

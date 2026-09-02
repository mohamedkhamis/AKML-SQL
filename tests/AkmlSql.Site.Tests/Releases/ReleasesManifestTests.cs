using AkmlSql.Site.Releases;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AkmlSql.Site.Tests.Releases;

/// <summary>
/// Spec 034 T010 (US1): manifest loader tests against the failure behavior contract in
/// specs/034-blazor-product-site/contracts/releases-json.md — valid file parses newest-first
/// with IsLatest derived; missing/unreadable file, invalid JSON, and empty releases all
/// produce the unavailable fallback state; schema-violating entries are skipped while valid
/// ones survive.
/// </summary>
public sealed class ReleasesManifestTests
{
    private const string ValidJson = """
        {
          "product": "AKML SQL",
          "generatedAt": "2026-08-27T00:00:00Z",
          "releases": [
            {
              "version": "1.0.0",
              "releasedAt": "2026-08-20",
              "supportedHosts": ["SSMS 22", "VS 2026"],
              "downloadUrl": "downloads/AKMLSQLSetup-1.0.0.exe",
              "sha256Hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "releaseNotesUrl": "https://github.com/mohamedkhamis/AKML-SQL/releases",
              "notesSummary": "First public release.",
              "minimumOsVersion": "10.0"
            },
            {
              "version": "1.1.0",
              "releasedAt": "2026-08-27",
              "supportedHosts": ["SSMS 22", "VS 2026"],
              "downloadUrl": "downloads/AKMLSQLSetup-1.1.0.exe",
              "sha256Hash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            }
          ]
        }
        """;

    [Fact]
    public void ValidManifest_ParsesNewestFirst_WithIsLatestDerived()
    {
        var manifest = ReleasesManifest.Parse(ValidJson);

        Assert.True(manifest.IsAvailable);
        Assert.Equal("AKML SQL", manifest.Product);
        Assert.Equal(2, manifest.Releases.Count);

        // Input lists 1.0.0 first; the loader must re-order newest-first by release date.
        Assert.Equal("1.1.0", manifest.Releases[0].Version);
        Assert.Equal("1.0.0", manifest.Releases[1].Version);
        Assert.True(manifest.Releases[0].IsLatest);
        Assert.False(manifest.Releases[1].IsLatest);

        var latest = manifest.Latest;
        Assert.NotNull(latest);
        Assert.Equal("1.1.0", latest.Version);
        Assert.Equal(new DateOnly(2026, 8, 27), latest.ReleasedAt);
        Assert.Equal(["SSMS 22", "VS 2026"], latest.SupportedHosts);
        Assert.Equal("downloads/AKMLSQLSetup-1.1.0.exe", latest.DownloadUrl);
        Assert.Equal(64, latest.Sha256Hash.Length);
    }

    [Fact]
    public void ValidManifest_OptionalFields_DefaultToNull()
    {
        var manifest = ReleasesManifest.Parse(ValidJson);

        var latest = manifest.Latest!;
        Assert.Null(latest.ReleaseNotesUrl);
        Assert.Null(latest.NotesSummary);
        Assert.Null(latest.MinimumOsVersion);

        var older = manifest.Releases[1];
        Assert.Equal("First public release.", older.NotesSummary);
        Assert.Equal("10.0", older.MinimumOsVersion);
    }

    [Fact]
    public void MissingFile_ReturnsUnavailableFallback()
    {
        // An empty web root: no releases.json on disk.
        using var env = new StubWebHostEnvironment();

        var manifest = ReleasesManifest.Load(env);

        Assert.False(manifest.IsAvailable);
        Assert.Null(manifest.Latest);
        Assert.Empty(manifest.Releases);
    }

    [Fact]
    public void InvalidJson_ReturnsUnavailableFallback()
    {
        var manifest = ReleasesManifest.Parse("{ this is not json");

        Assert.False(manifest.IsAvailable);
        Assert.Null(manifest.Latest);
        Assert.Empty(manifest.Releases);
    }

    [Fact]
    public void SchemaViolatingEntries_AreSkipped_WhileValidOnesSurvive()
    {
        const string json = """
            {
              "product": "AKML SQL",
              "releases": [
                {
                  "version": "9.9.9",
                  "releasedAt": "2026-09-01",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/good.exe",
                  "sha256Hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                },
                {
                  "version": "9.9.8",
                  "releasedAt": "2026-08-30",
                  "supportedHosts": [],
                  "downloadUrl": "downloads/no-hosts.exe",
                  "sha256Hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                },
                {
                  "version": "9.9.7",
                  "releasedAt": "2026-08-29",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/bad-hash.exe",
                  "sha256Hash": "not-hex"
                },
                {
                  "version": "",
                  "releasedAt": "2026-08-28",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/no-version.exe",
                  "sha256Hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                },
                {
                  "version": "9.9.6",
                  "releasedAt": "not-a-date",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/bad-date.exe",
                  "sha256Hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                },
                {
                  "version": "9.9.5",
                  "releasedAt": "2026-08-27",
                  "supportedHosts": ["SSMS 22"],
                  "sha256Hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                }
              ]
            }
            """;

        var manifest = ReleasesManifest.Parse(json);

        Assert.True(manifest.IsAvailable);
        var surviving = Assert.Single(manifest.Releases);
        Assert.Equal("9.9.9", surviving.Version);
        Assert.True(surviving.IsLatest);
    }

    [Fact]
    public void EmptyReleasesArray_ReturnsUnavailableFallback()
    {
        var manifest = ReleasesManifest.Parse("""{ "product": "AKML SQL", "releases": [] }""");

        Assert.False(manifest.IsAvailable);
        Assert.Null(manifest.Latest);
        Assert.Empty(manifest.Releases);
    }

    [Fact]
    public void UnavailableSingleton_IsEmptyFallback()
    {
        Assert.False(ReleasesManifest.Unavailable.IsAvailable);
        Assert.Null(ReleasesManifest.Unavailable.Latest);
        Assert.Empty(ReleasesManifest.Unavailable.Releases);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    public void Parse_NonObjectRoot_ReturnsUnavailableFallback(string json)
    {
        // C1: valid JSON but not an object — TryGetProperty would throw
        // InvalidOperationException, escaping the JsonException catch and permanently
        // failing the /download singleton. Must collapse to the fallback instead.
        var manifest = ReleasesManifest.Parse(json);

        Assert.False(manifest.IsAvailable);
        Assert.Null(manifest.Latest);
        Assert.Empty(manifest.Releases);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/installers/AKMLSQLSetup.exe")]
    [InlineData("//evil.example.com/AKMLSQLSetup.exe")] // protocol-relative
    [InlineData("downloads/AKML:Setup.exe")] // relative path carrying a scheme-like colon
    public void DownloadUrl_NotHttpOrStrictSiteRelative_RejectsEntry(string downloadUrl)
    {
        // S1: a hostile manifest value must never reach the Download button href.
        var manifest = ReleasesManifest.Parse(ManifestWith(Entry(downloadUrl)));

        Assert.False(manifest.IsAvailable);
        Assert.Empty(manifest.Releases);
    }

    [Theory]
    [InlineData("https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.0.0/AKMLSQLSetup.exe")]
    [InlineData("http://cdn.example.com/AKMLSQLSetup.exe")]
    [InlineData("downloads/AKMLSQLSetup-1.0.0.exe")]
    [InlineData("/downloads/AKMLSQLSetup-1.0.0.exe")]
    public void DownloadUrl_HttpHttpsOrSiteRelative_AcceptsEntry(string downloadUrl)
    {
        var manifest = ReleasesManifest.Parse(ManifestWith(Entry(downloadUrl)));

        var release = Assert.Single(manifest.Releases);
        Assert.Equal(downloadUrl, release.DownloadUrl);
    }

    [Fact]
    public void ReleaseNotesUrl_Invalid_TreatedAsNull_EntrySurvives()
    {
        // S1: the optional notes URL degrades to null instead of dropping the whole entry.
        const string json = """
            {
              "product": "AKML SQL",
              "releases": [
                {
                  "version": "1.0.0",
                  "releasedAt": "2026-08-27",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/AKMLSQLSetup-1.0.0.exe",
                  "sha256Hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "releaseNotesUrl": "javascript:alert(1)"
                }
              ]
            }
            """;

        var manifest = ReleasesManifest.Parse(json);

        var release = Assert.Single(manifest.Releases);
        Assert.Null(release.ReleaseNotesUrl);
    }

    [Fact]
    public void CdnUrl_AbsoluteHttps_Parsed()
    {
        const string json = """
            {
              "product": "AKML SQL",
              "releases": [
                {
                  "version": "1.0.0",
                  "releasedAt": "2026-08-27",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/AKMLSQLSetup-1.0.0.exe",
                  "sha256Hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "cdnUrl": "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.0.0/AKMLSQLSetup-1.0.0.exe"
                }
              ]
            }
            """;

        var release = Assert.Single(ReleasesManifest.Parse(json).Releases);
        Assert.Equal("https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.0.0/AKMLSQLSetup-1.0.0.exe", release.CdnUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("downloads/AKMLSQLSetup-1.0.0.exe")] // site-relative is not a CDN mirror
    [InlineData("//evil.example.com/x.exe")]
    [InlineData("file:///C:/x.exe")]
    public void CdnUrl_NotAbsoluteHttp_TreatedAsNull_EntrySurvives(string cdnUrl)
    {
        var json = $$"""
            {
              "product": "AKML SQL",
              "releases": [
                {
                  "version": "1.0.0",
                  "releasedAt": "2026-08-27",
                  "supportedHosts": ["SSMS 22"],
                  "downloadUrl": "downloads/AKMLSQLSetup-1.0.0.exe",
                  "sha256Hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "cdnUrl": "{{cdnUrl}}"
                }
              ]
            }
            """;

        var release = Assert.Single(ReleasesManifest.Parse(json).Releases);
        Assert.Null(release.CdnUrl);
    }

    private static string Entry(string downloadUrl) => $$"""
        {
          "version": "1.0.0",
          "releasedAt": "2026-08-27",
          "supportedHosts": ["SSMS 22"],
          "downloadUrl": "{{downloadUrl}}",
          "sha256Hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        }
        """;

    private static string ManifestWith(params string[] entries) =>
        "{ \"product\": \"AKML SQL\", \"releases\": [ " + string.Join(", ", entries) + " ] }";

    /// <summary>
    /// Minimal IWebHostEnvironment stub whose web root is an empty temp directory,
    /// so ReleasesManifest.Load sees no releases.json on disk.
    /// </summary>
    private sealed class StubWebHostEnvironment : IWebHostEnvironment, IDisposable
    {
        private readonly string _webRootPath = Path.Combine(Path.GetTempPath(), "akml-site-tests-" + Guid.NewGuid().ToString("N"));

        public StubWebHostEnvironment()
        {
            Directory.CreateDirectory(_webRootPath);
            WebRootFileProvider = new PhysicalFileProvider(_webRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "AkmlSql.Site.Tests";
        public string WebRootPath { get => _webRootPath; set => throw new NotSupportedException(); }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public void Dispose()
        {
            (WebRootFileProvider as IDisposable)?.Dispose();
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
        }
    }
}

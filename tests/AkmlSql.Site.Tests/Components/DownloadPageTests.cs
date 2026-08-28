using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 034 T011 (US1): bUnit render tests for the /download page — latest release card
/// (version, date, supported hosts, SHA-256, download link), the previous-releases list,
/// and the friendly fallback with the repo /releases/latest link when the manifest is
/// broken (contracts/releases-json.md failure behavior).
/// </summary>
public sealed class DownloadPageTests
{
    private const string RepoLatestReleasesUrl = "https://github.com/mohamedkhamis/AKML-SQL/releases/latest";

    private static Release MakeRelease(
        string version,
        string releasedAt,
        string sha,
        string? notesSummary = null,
        string? releaseNotesUrl = null) =>
        new()
        {
            Version = version,
            ReleasedAt = DateOnly.Parse(releasedAt),
            SupportedHosts = ["SSMS 22", "VS 2026"],
            DownloadUrl = $"downloads/AKMLSQLSetup-{version}.exe",
            Sha256Hash = sha,
            NotesSummary = notesSummary,
            ReleaseNotesUrl = releaseNotesUrl,
            MinimumOsVersion = "10.0",
        };

    private static ReleasesManifest TwoReleaseManifest() =>
        ReleasesManifest.Create(
            [
                MakeRelease(
                    "1.1.0",
                    "2026-08-27",
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    notesSummary: "Format Styles window and autocomplete gate.",
                    releaseNotesUrl: "https://github.com/mohamedkhamis/AKML-SQL/releases/tag/v1.1.0"),
                MakeRelease(
                    "1.0.0",
                    "2026-08-20",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            ],
            product: "AKML SQL");

    private static BunitContext NewCtx(ReleasesManifest manifest)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(manifest);
        return ctx;
    }

    [Fact]
    public void LatestReleaseCard_RendersVersionDateHostsHashAndDownloadLink()
    {
        using var ctx = NewCtx(TwoReleaseManifest());

        var cut = ctx.Render<Download>();

        Assert.Contains("1.1.0", cut.Markup);
        Assert.Contains("August 27, 2026", cut.Markup);
        Assert.Contains("SSMS 22", cut.Markup);
        Assert.Contains("VS 2026", cut.Markup);
        Assert.Contains("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", cut.Markup);
        Assert.Contains("Format Styles window and autocomplete gate.", cut.Markup);

        var downloadLink = cut.Find("a[href='/dl/AKMLSQLSetup-1.1.0.exe']");
        Assert.NotNull(downloadLink);

        var notesLink = cut.Find("a[href='https://github.com/mohamedkhamis/AKML-SQL/releases/tag/v1.1.0']");
        Assert.NotNull(notesLink);
    }

    [Fact]
    public void OlderReleases_AreListedBelowLatest()
    {
        using var ctx = NewCtx(TwoReleaseManifest());

        var cut = ctx.Render<Download>();

        Assert.Contains("1.0.0", cut.Markup);
        Assert.Contains("August 20, 2026", cut.Markup);
        Assert.Contains("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", cut.Markup);

        var olderLink = cut.Find("a[href='/dl/AKMLSQLSetup-1.0.0.exe']");
        Assert.NotNull(olderLink);
    }

    [Fact]
    public void BrokenManifest_RendersFriendlyFallbackWithRepoLink()
    {
        using var ctx = NewCtx(ReleasesManifest.Unavailable);

        var cut = ctx.Render<Download>();

        Assert.Contains("No public release available yet", cut.Markup, StringComparison.OrdinalIgnoreCase);

        var repoLink = cut.Find($"a[href='{RepoLatestReleasesUrl}']");
        Assert.NotNull(repoLink);
    }

    [Fact]
    public void BrokenManifest_DoesNotRenderReleaseCards()
    {
        using var ctx = NewCtx(ReleasesManifest.Unavailable);

        var cut = ctx.Render<Download>();

        Assert.DoesNotContain("SHA-256", cut.Markup);
    }

    [Fact]
    public void LatestReleaseCard_RendersCopyHashButton_WithDataTarget()
    {
        // U22: the copy affordance targets the digest element by id; js/copy-hash.js
        // reveals it (CSS keeps it hidden for no-JS users).
        using var ctx = NewCtx(TwoReleaseManifest());

        var cut = ctx.Render<Download>();

        var button = cut.Find("button.copy-hash-btn");
        Assert.Equal("latest-sha256", button.GetAttribute("data-copy-target"));
        Assert.NotNull(cut.Find("code#latest-sha256"));
    }

    [Fact]
    public void LatestReleaseCard_TrimsTrailingPointZero_InMinimumOsVersion()
    {
        // U23: "Windows 10.0 or later" reads like an API version — display "Windows 10".
        using var ctx = NewCtx(TwoReleaseManifest());

        var cut = ctx.Render<Download>();

        Assert.Contains("Windows 10 or later", cut.Markup);
        Assert.DoesNotContain("Windows 10.0 or later", cut.Markup);
    }

    [Fact]
    public void DownloadLink_AbsoluteUrl_IsNotRewritten()
    {
        // Only site-relative downloads/... URLs go through the /dl tracker; absolute
        // http(s) URLs (e.g. GitHub-hosted assets) are rendered as-is.
        var release = MakeRelease("1.2.0", "2026-08-28", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")
            with { DownloadUrl = "https://example.com/assets/setup.exe" };
        using var ctx = NewCtx(ReleasesManifest.Create([release]));

        var cut = ctx.Render<Download>();

        Assert.NotNull(cut.Find("a[href='https://example.com/assets/setup.exe']"));
        Assert.DoesNotContain("/dl/", cut.Markup);
    }
}

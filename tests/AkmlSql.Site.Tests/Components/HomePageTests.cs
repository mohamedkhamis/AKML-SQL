using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Site redesign: bUnit tests for the Home landing page — hero with dual CTA, the framed
/// product screenshot (explicit dimensions, no CLS), and icon-bearing feature cards.
/// </summary>
public sealed class HomePageTests
{
    /// <summary>
    /// The hero and the closing CTA name the current version, so the page needs the manifest.
    /// A real manifest rather than a stub: the page must render correctly for BOTH a populated feed
    /// and the unavailable fallback, and the individual tests below choose which.
    /// </summary>
    private static BunitContext NewCtx(bool withRelease = true)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(withRelease
            ? ReleasesManifest.Create(
                [
                    new Release
                    {
                        Version = "1.26.0912.2043",
                        ReleasedAt = new DateOnly(2026, 9, 13),
                        SupportedHosts = ["SSMS 22", "VS 2026"],
                        DownloadUrl = "downloads/AKMLSQLSetup-1.26.0912.2043.exe",
                        Sha256Hash = new string('a', 64),
                    },
                ],
                product: "AKML SQL")
            : ReleasesManifest.Unavailable);

        return ctx;
    }

    [Fact]
    public void Hero_RendersPrimaryAndSecondaryCtas()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        var primary = cut.Find(".hero a.btn-primary[href='/download']");
        Assert.False(string.IsNullOrWhiteSpace(primary.TextContent));
        var secondary = cut.Find(".hero a.btn-secondary[href='/docs']");
        Assert.False(string.IsNullOrWhiteSpace(secondary.TextContent));
    }

    [Fact]
    public void Hero_RendersScreenshot_WithExplicitDimensions()
    {
        // width/height attributes let the browser reserve the aspect ratio before the
        // image loads — no layout shift (contracts/site-routes.md CLS clause).
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        var img = cut.Find(".hero-visual .screenshot-frame img");
        Assert.Equal("1920", img.GetAttribute("width"));
        Assert.Equal("889", img.GetAttribute("height"));
        Assert.StartsWith("img/screenshots/", img.GetAttribute("src"));
        Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")));
    }

    [Fact]
    public void FeatureCards_RenderStrokeIcons()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        var icons = cut.FindAll(".feature-card .feature-card-icon svg");
        Assert.Equal(7, icons.Count);
        // Every icon is decorative (the card heading names the feature).
        Assert.All(icons, svg => Assert.Equal("true", svg.GetAttribute("aria-hidden")));
    }

    [Fact]
    public void Hero_LeadsWithValue_NotACompetitorComparison()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();
        var tagline = cut.Find(".hero-tagline").TextContent;

        // The old tagline listed seven features in one sentence and ended on "replicating and
        // extending the Redgate SQL Prompt feature set" -- defining the product by someone else's,
        // in the last thing anyone reads. The comparison now has its own section.
        Assert.DoesNotContain("Redgate", tagline, StringComparison.OrdinalIgnoreCase);

        var words = tagline.Split((string[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(words <= 30, $"Hero tagline is {words} words: \"{tagline.Trim()}\"");
    }

    [Fact]
    public void Hero_StatesTheFreeOpenSourceFact()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        // The strongest thing this project can say against a paid alternative, so it belongs in the
        // hero rather than only in the footer.
        Assert.Contains("MIT license", cut.Find(".hero-tagline-sub").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hero_PrimaryCta_NamesTheCurrentVersion()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        Assert.Contains("1.26.0912.2043", cut.Find(".hero a.btn-primary").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoRelease_TheHeroStillRenders_WithoutAVersion()
    {
        using var ctx = NewCtx(withRelease: false);

        var cut = ctx.Render<Home>();

        // contracts/releases-json.md: a missing or broken manifest must never produce an error page.
        // The home page now reads the manifest too, so it inherits that obligation.
        Assert.NotNull(cut.Find(".hero a.btn-primary[href='/download']"));
        Assert.Empty(cut.FindAll(".hero .btn-sub"));
        Assert.Contains("MIT license", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProofStrip_CarriesVerifiableNumbers_ThatLinkToTheDocs()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();

        // "Comprehensive" and "powerful" are what every tool's home page says. These are checkable,
        // which is the point of linking them.
        var strip = cut.Find(".proof-strip").TextContent;
        Assert.Contains("130+", strip, StringComparison.Ordinal);
        Assert.Contains("7-stage", strip, StringComparison.Ordinal);
        Assert.Contains("MIT", strip, StringComparison.Ordinal);

        Assert.NotNull(cut.Find(".proof-strip a[href='/docs/analysis-rules']"));
        Assert.NotNull(cut.Find(".proof-strip a[href='/docs/formatting']"));
    }

    [Fact]
    public void TheComparisonIsMadeProperly_InItsOwnSection()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Home>();
        var comparison = cut.Find(".comparison").TextContent;

        // Moved out of the hero's trailing clause: here it can say what is the same, what is
        // different, and why that matters.
        Assert.Contains("SQL Prompt", comparison, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIT", comparison, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("out-of-process", comparison, StringComparison.OrdinalIgnoreCase);
    }
}

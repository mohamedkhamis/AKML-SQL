using AkmlSql.Site.Components;
using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>Spec 044: approved Product Studio content, honest support and progressive image tour.</summary>
public sealed class HomePageTests
{
    private static BunitContext NewCtx(bool withRelease = true)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(withRelease
            ? ReleasesManifest.Create([
                new Release
                {
                    Version = "1.26.0912.2043",
                    ReleasedAt = new DateOnly(2026, 9, 13),
                    SupportedHosts = ["SSMS 22"],
                    DownloadUrl = "downloads/AKMLSQLSetup-1.26.0912.2043.exe",
                    Sha256Hash = new string('a', 64),
                }], product: "AKML SQL")
            : ReleasesManifest.Unavailable);
        return ctx;
    }

    [Fact]
    public void Hero_ExplainsValue_AndOffersDownloadAndProductTour()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        Assert.Contains("Good SQL.", cut.Find("h1").TextContent);
        Assert.Contains("Less friction.", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find(".hero a.btn-primary[href='/download']"));
        Assert.NotNull(cut.Find(".hero a[href='#product-tour']"));
        var tagline = cut.Find(".hero-tagline").TextContent;
        Assert.DoesNotContain("Redgate", tagline, StringComparison.OrdinalIgnoreCase);
        Assert.True(tagline.Split((string[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 30);
        Assert.Contains("MIT license", cut.Find(".hero-tagline-sub").TextContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hero_UsesManifestVersion_AndSurvivesUnavailableManifest(bool withRelease)
    {
        using var ctx = NewCtx(withRelease);
        var cut = ctx.Render<Home>();
        var cta = cut.Find(".hero a.btn-primary[href='/download']");
        Assert.Equal(withRelease, cta.TextContent.Contains("1.26.0912.2043", StringComparison.Ordinal));
        Assert.Equal(withRelease ? 1 : 0, cut.FindAll(".hero .btn-sub").Count);
        Assert.Contains("MIT license", cut.Markup);
    }

    [Fact]
    public void Hero_ImageReservesSpace_AndPrioritizesOnlyTheHero()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        foreach (var theme in new[] { "dark", "light" })
        {
            var link = cut.Find($".hero .screenshot-{theme}");
            var image = link.QuerySelector("img")!;
            Assert.Equal("1920", image.GetAttribute("width"));
            Assert.Equal("889", image.GetAttribute("height"));
            Assert.Equal("high", image.GetAttribute("fetchpriority"));
            Assert.Equal(image.GetAttribute("src"), link.GetAttribute("href"));
            Assert.Contains("Northwind", image.GetAttribute("alt"));
        }
        Assert.All(cut.FindAll(".studio-tour img"), image => Assert.Equal("lazy", image.GetAttribute("loading")));
    }

    [Fact]
    public void DatabaseSupport_DistinguishesPostgresBrowserFromSsms()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        var sql = cut.FindAll(".database").Single(el => el.QuerySelector("h3")!.TextContent == "Microsoft SQL Server");
        var pg = cut.FindAll(".database").Single(el => el.QuerySelector("h3")!.TextContent == "PostgreSQL");
        Assert.Contains("SSMS 22 + BROWSER", sql.TextContent);
        Assert.Contains("BROWSER + AKML ENGINE", pg.TextContent);
        Assert.Contains("PostgreSQL requires an AKML engine with PostgreSQL enabled", cut.Find("#support").TextContent);
        Assert.Contains("SSMS extension targets SQL Server", cut.Find("#support").TextContent);
        Assert.DoesNotContain("Visual Studio", cut.Find(".hero").TextContent);
    }

    [Fact]
    public void SetupPaths_ResolveToDownloadAndDedicatedBrowserGuide()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        Assert.NotNull(cut.Find("#start a[href='/download']"));
        Assert.NotNull(cut.Find("#start a[href='/docs/topics/browser-setup']"));
        Assert.Contains("connected AKML engine", cut.Find("#start").TextContent);
    }

    [Fact]
    public void Tour_HasNativeImageLinks_AndAllExamplesVisibleBeforeJavascript()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        Assert.True(cut.Find(".tour-controls").HasAttribute("hidden"));
        var buttons = cut.FindAll(".studio-tour-button");
        Assert.Equal(5, buttons.Count);
        foreach (var button in buttons)
        {
            var panel = cut.Find("#" + button.GetAttribute("aria-controls"));
            Assert.False(panel.HasAttribute("hidden"));
            Assert.Equal("button", button.GetAttribute("type"));
            foreach (var link in panel.QuerySelectorAll(".screenshot-variant"))
            {
                var image = link.QuerySelector("img")!;
                Assert.Equal(image.GetAttribute("src"), link.GetAttribute("href"));
                Assert.Equal("false", link.GetAttribute("data-enhance-nav"));
                Assert.False(string.IsNullOrWhiteSpace(image.GetAttribute("alt")));
            }
        }
        Assert.Contains("demo values", cut.Find("#tour-postgres").TextContent);
        Assert.Contains("not executed", cut.Find("#tour-editor").TextContent);
    }

    [Fact]
    public void BenefitsAndFaq_ExplainPracticalUse_WithoutBlanketAiClaims()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<Home>();
        var benefits = cut.Find("#why").TextContent;
        Assert.Contains("completion", benefits);
        Assert.Contains("analysis", benefits);
        Assert.Contains("history", benefits);
        Assert.Equal(4, cut.FindAll(".faq details summary").Count);
        Assert.Contains("Provider setup and charges may apply", cut.Find(".ai-band").TextContent);
        Assert.DoesNotContain("everything it stores stays", cut.Markup);
        Assert.DoesNotContain("replicates the SQL Prompt feature set", cut.Markup);
    }

    [Fact]
    public void Screenshot_ExplicitPaths_DoNotChangeLegacyNameConvention()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<ProductScreenshot>(p => p.Add(x => x.Name, "02-format-styles")
            .Add(x => x.Width, 1046).Add(x => x.Height, 673).Add(x => x.Alt, "Styles")
            .Add(x => x.Caption, "Preview").Add(x => x.LightPath, "img/custom.png"));
        Assert.Equal("img/custom.png", cut.Find(".screenshot-light").GetAttribute("href"));
        Assert.Equal("img/screenshots/dark/02-format-styles-crop.png", cut.Find(".screenshot-dark").GetAttribute("href"));
    }
}

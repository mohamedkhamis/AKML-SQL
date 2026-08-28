using AkmlSql.Site.Components.Pages;
using Bunit;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Site redesign: bUnit tests for the Home landing page — hero with dual CTA, the framed
/// product screenshot (explicit dimensions, no CLS), and icon-bearing feature cards.
/// </summary>
public sealed class HomePageTests
{
    [Fact]
    public void Hero_RendersPrimaryAndSecondaryCtas()
    {
        using var ctx = new BunitContext();

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
        using var ctx = new BunitContext();

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
        using var ctx = new BunitContext();

        var cut = ctx.Render<Home>();

        var icons = cut.FindAll(".feature-card .feature-card-icon svg");
        Assert.Equal(7, icons.Count);
        // Every icon is decorative (the card heading names the feature).
        Assert.All(icons, svg => Assert.Equal("true", svg.GetAttribute("aria-hidden")));
    }
}

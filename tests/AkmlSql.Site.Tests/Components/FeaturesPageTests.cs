using AkmlSql.Site.Components.Pages;
using Bunit;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Site redesign: bUnit tests for the /features page — alternating media sections with
/// lazy-loaded, dimensioned screenshots, plus the icon capability card grid.
/// </summary>
public sealed class FeaturesPageTests
{
    [Fact]
    public void MediaRows_ShowTheSelectedSsmsFeatures_WithAlternatingLayout()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<Features>();

        var rows = cut.FindAll(".media-row");
        Assert.Equal(7, rows.Count);
        // At least one row flips text/media (CSS reorders via .media-row-flip).
        Assert.NotEmpty(cut.FindAll(".media-row.media-row-flip"));
    }

    [Fact]
    public void MediaRowImages_AreLazyLoaded_WithExplicitDimensions()
    {
        // contracts/site-routes.md: marketing screenshots lazy-load and carry width/height
        // so the page does not shift as they stream in.
        using var ctx = new BunitContext();

        var cut = ctx.Render<Features>();

        var images = cut.FindAll(".media-row .screenshot-frame img");
        Assert.Equal(14, images.Count);
        foreach (var img in images)
        {
            Assert.Equal("lazy", img.GetAttribute("loading"));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("width")));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("height")));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")));
            Assert.StartsWith("img/screenshots/", img.GetAttribute("src"));
            Assert.EndsWith("-crop.png", img.GetAttribute("src"));
        }

        foreach (var frame in cut.FindAll(".media-row .screenshot-frame"))
        {
            var dark = frame.QuerySelector(".screenshot-dark img")!;
            var light = frame.QuerySelector(".screenshot-light img")!;
            Assert.Equal(dark.GetAttribute("src")!.Replace("/dark/", "/light/"), light.GetAttribute("src"));
            Assert.Equal(dark.GetAttribute("width"), light.GetAttribute("width"));
            Assert.Equal(dark.GetAttribute("height"), light.GetAttribute("height"));
        }
    }

    [Fact]
    public void Screenshots_OpenInThePageViewer_NotANewTab()
    {
        // A click used to open the bare PNG in a new browser tab. js/screenshot-viewer.js now
        // opens an in-page gallery; the markup keeps a real href (no-JS and Ctrl-click still open
        // the image), opts the link out of Blazor's enhanced navigation, and no longer claims a
        // new tab.
        using var ctx = new BunitContext();

        var cut = ctx.Render<Features>();

        var links = cut.FindAll(".media-row .screenshot-frame .screenshot-variant");
        Assert.Equal(14, links.Count);
        foreach (var link in links)
        {
            Assert.Null(link.GetAttribute("target"));
            Assert.Equal("false", link.GetAttribute("data-enhance-nav"));
            Assert.Equal(link.QuerySelector("img")!.GetAttribute("src"), link.GetAttribute("href"));
            // The link's name is the screenshot's alt plus the action — an aria-label would
            // replace the descriptive alt for screen-reader users.
            Assert.Null(link.GetAttribute("aria-label"));
            Assert.False(string.IsNullOrWhiteSpace(link.QuerySelector("img")!.GetAttribute("alt")));
            Assert.Equal("View larger", link.QuerySelector(".visually-hidden")!.TextContent);
        }

        foreach (var frame in cut.FindAll(".media-row .screenshot-frame"))
        {
            // The viewer reads the caption from this hook and the section text around it.
            Assert.False(string.IsNullOrWhiteSpace(frame.QuerySelector(".screenshot-caption-text")?.TextContent));
            Assert.Equal("Click to enlarge", frame.QuerySelector(".screenshot-hint")!.TextContent.Trim());
            Assert.NotNull(frame.Closest(".media-row")!.QuerySelector(".media-text h2"));
        }
    }

    [Fact]
    public void CapabilityCards_RenderStrokeIcons()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<Features>();

        var icons = cut.FindAll(".feature-list .feature-card .feature-card-icon svg");
        Assert.Equal(7, icons.Count);
    }
}

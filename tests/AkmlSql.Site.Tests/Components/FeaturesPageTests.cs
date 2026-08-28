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
    public void MediaRows_RenderThreeSections_WithAlternatingLayout()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<Features>();

        var rows = cut.FindAll(".media-row");
        Assert.Equal(3, rows.Count);
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
        Assert.Equal(3, images.Count);
        foreach (var img in images)
        {
            Assert.Equal("lazy", img.GetAttribute("loading"));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("width")));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("height")));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")));
            Assert.StartsWith("img/screenshots/", img.GetAttribute("src"));
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

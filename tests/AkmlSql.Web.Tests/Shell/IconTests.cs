using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T010, research R62) — <see cref="Icon"/> renders one symbol of the vendored Lucide
/// sprite. A decorative icon is hidden from assistive technology; an icon that carries meaning on its
/// own gets an accessible name.
/// </summary>
public sealed class IconTests : BunitContext
{
    [Fact]
    public void Decorative_icon_references_the_sprite_symbol_and_is_hidden_from_assistive_technology()
    {
        var cut = Render<Icon>(p => p.Add(i => i.Name, "play"));

        cut.MarkupMatches(
            "<svg class=\"akml-icon\" width=\"16\" height=\"16\" aria-hidden=\"true\" focusable=\"false\">" +
            "<use href=\"lib/icons/akml-icons.svg#akml-icon-play\"></use></svg>");
    }

    [Fact]
    public void Labelled_icon_is_an_image_with_an_accessible_name()
    {
        var cut = Render<Icon>(p => p.Add(i => i.Name, "play").Add(i => i.Label, "Run"));

        var svg = cut.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Run", svg.GetAttribute("aria-label"));
        Assert.Null(svg.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Size_sets_width_and_height()
    {
        var cut = Render<Icon>(p => p.Add(i => i.Name, "x").Add(i => i.Size, 12));

        var svg = cut.Find("svg");
        Assert.Equal("12", svg.GetAttribute("width"));
        Assert.Equal("12", svg.GetAttribute("height"));
    }

    [Fact]
    public void Extra_class_is_appended()
    {
        var cut = Render<Icon>(p => p.Add(i => i.Name, "x").Add(i => i.Class, "akml-row-glyph"));

        Assert.Equal("akml-icon akml-row-glyph", cut.Find("svg").GetAttribute("class"));
    }
}

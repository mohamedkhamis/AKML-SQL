using AkmlSql.Core;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 040 (T172, X-02, FR-061, research R27) — every AKML window title is
/// "AKML SQL – ‹Name›", with an en dash (U+2013), never a hyphen or an em dash.
/// </summary>
public class WindowTitlesTests
{
    [Fact]
    public void Options_window_title_is_product_en_dash_name()
    {
        Assert.Equal("AKML SQL – Options", WindowTitles.For("Options"));
    }

    [Theory]
    [InlineData("Format styles", "AKML SQL – Format styles")]
    [InlineData("Import summary: Khamis Style", "AKML SQL – Import summary: Khamis Style")]
    [InlineData("Split table — dbo.Orders", "AKML SQL – Split table — dbo.Orders")]
    public void Name_follows_the_product_and_an_en_dash_unchanged(string name, string expected)
    {
        Assert.Equal(expected, WindowTitles.For(name));
    }

    [Fact]
    public void Title_is_built_from_the_product_name_with_an_en_dash_not_a_hyphen_or_em_dash()
    {
        var title = WindowTitles.For("About");

        Assert.StartsWith(Constants.ProductName, title);
        Assert.StartsWith(WindowTitles.Prefix, title);
        Assert.Equal(" – ", WindowTitles.Separator);
        Assert.DoesNotContain(" - ", title);
        Assert.DoesNotContain("—", title);
    }
}

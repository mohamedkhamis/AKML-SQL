using AkmlSql.Web.Shared;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T184, research R63, FR-080) — the navigation bar marks where the user is: exactly one of the five places
/// is highlighted and announced as the current page, the editor's two addresses and every Settings address included,
/// and the mark follows navigation.
/// </summary>
public sealed class NavMenuTests : BunitContext
{
    private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

    private IRenderedComponent<NavMenu> At(string path)
    {
        Nav.NavigateTo(path);
        return Render<NavMenu>();
    }

    private static string[] Highlighted(IRenderedComponent<NavMenu> nav) =>
        nav.FindAll("a.akml-nav-link.active").Select(a => a.GetAttribute("data-testid")!).ToArray();

    private static string[] Current(IRenderedComponent<NavMenu> nav) =>
        nav.FindAll("a[aria-current=page]").Select(a => a.GetAttribute("data-testid")!).ToArray();

    [Theory]
    [InlineData("/", "nav-editor")]
    [InlineData("/editor", "nav-editor")]
    [InlineData("/?autorun=1", "nav-editor")]
    [InlineData("/snippets", "nav-snippets")]
    [InlineData("/styles", "nav-styles")]
    [InlineData("/history", "nav-history")]
    [InlineData("/settings", "nav-settings")]
    [InlineData("/settings/code-analysis", "nav-settings")]
    [InlineData("/diagnostics", "nav-settings")]
    public void Exactly_the_current_place_is_highlighted_and_announced(string path, string expected)
    {
        var nav = At(path);

        Assert.Equal(new[] { expected }, Highlighted(nav));
        Assert.Equal(new[] { expected }, Current(nav));
    }

    [Fact]
    public void Every_place_has_its_test_id()
    {
        var nav = At("/");

        Assert.Equal(new[] { "nav-editor", "nav-snippets", "nav-styles", "nav-history", "nav-settings" },
            nav.FindAll("a.akml-nav-link").Select(a => a.GetAttribute("data-testid")));
    }

    [Fact]
    public void The_highlight_follows_navigation()
    {
        var nav = At("/history");

        Nav.NavigateTo("/editor");
        nav.WaitForAssertion(() => Assert.Equal(new[] { "nav-editor" }, Highlighted(nav)));

        Nav.NavigateTo("/settings/editor");   // a Settings section named like the editor is still Settings
        nav.WaitForAssertion(() => Assert.Equal(new[] { "nav-settings" }, Highlighted(nav)));
    }

    [Fact]
    public void An_unknown_address_highlights_nothing()
    {
        var nav = At("/spike");

        Assert.Empty(Highlighted(nav));
        Assert.Empty(Current(nav));
    }
}

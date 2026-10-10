using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Workspace;

/// <summary>
/// Spec 041 (T117, research R42, FR-041) — the navigation bar can be hidden on the editor page only; a strip
/// "Show navigation bar" brings it back. Every other page always shows it. The editor page fills the window
/// (no padding around it).
/// </summary>
public sealed class MainLayoutNavTests : IDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly FakeWorkspaceLayoutStore _layout = new(WorkspaceLayout.Default with { NavVisible = false });

    public MainLayoutNavTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton<IWorkspaceLayoutStore>(_layout);
    }

    public void Dispose() => _ctx.Dispose();

    private void GoTo(string uri) => _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    [Theory]
    [InlineData("/")]
    [InlineData("/editor")]
    [InlineData("/editor?autorun=1")]
    public void On_the_editor_page_a_hidden_nav_leaves_a_strip(string route)
    {
        GoTo(route);
        var cut = _ctx.Render<WorkspaceNav>();

        Assert.Empty(cut.FindAll("nav.akml-nav"));
        var strip = cut.Find("[data-testid=workspace-edge-nav]");
        Assert.Equal("Show navigation bar", strip.TextContent.Trim());
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/history")]
    [InlineData("/styles")]
    public void Every_other_page_shows_the_nav(string route)
    {
        GoTo(route);
        var cut = _ctx.Render<WorkspaceNav>();

        Assert.NotNull(cut.Find("nav.akml-nav"));
        Assert.Empty(cut.FindAll("[data-testid=workspace-edge-nav]"));
    }

    [Fact]
    public void The_strip_brings_the_nav_back_and_remembers_it()
    {
        GoTo("/editor");
        var cut = _ctx.Render<WorkspaceNav>();

        cut.Find("[data-testid=workspace-edge-nav]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("nav.akml-nav")));
        Assert.Contains(_layout.Sets, s => s.Field == WorkspaceLayoutFields.NavVisible && Equals(s.Value, true));
    }

    [Fact]
    public void Leaving_the_editor_page_shows_the_nav()
    {
        GoTo("/editor");
        var cut = _ctx.Render<WorkspaceNav>();
        Assert.Empty(cut.FindAll("nav.akml-nav"));

        GoTo("/settings");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("nav.akml-nav")));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("editor", true)]
    [InlineData("editor?autorun=1", true)]
    [InlineData("Editor/", true)]
    [InlineData("settings", false)]
    [InlineData("editorx", false)]
    [InlineData("settings/ai", false)]
    public void The_editor_route_is_the_root_or_editor(string baseRelative, bool expected)
    {
        Assert.Equal(expected, WorkspaceRoutes.IsEditorRoute(baseRelative));
    }

    [Fact]
    public void The_main_area_has_no_padding_on_the_editor_page()
    {
        Assert.Equal("akml-main akml-main--workspace", WorkspaceRoutes.MainClass("editor"));
        Assert.Equal("akml-main", WorkspaceRoutes.MainClass("settings"));
    }
}

using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T130, research R43, FR-050, FR-051) — one Settings page: <c>/settings</c> opens General, each
/// <c>/settings/{id}</c> its section, the old routes still land where they used to (<c>/settings/ai</c>,
/// <c>/diagnostics</c>), anything else opens General; the section list navigates; the navigation bar has five
/// places.
/// </summary>
public sealed class SettingsPageRoutingTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string[] SectionsShown(IRenderedComponent<AkmlSql.Web.Pages.Settings> cut) =>
        cut.FindAll("[data-testid^=settings-section-]").Select(e => e.GetAttribute("data-testid")!["settings-section-".Length..]).ToArray();

    [Theory]
    [InlineData("settings", "general")]
    [InlineData("settings/general", "general")]
    [InlineData("settings/editor", "editor")]
    [InlineData("settings/format", "format")]
    [InlineData("settings/queries", "queries")]
    [InlineData("settings/code-analysis", "code-analysis")]
    [InlineData("settings/ai-assistance", "ai-assistance")]
    [InlineData("settings/connections", "connections")]
    [InlineData("settings/schema-cache", "schema-cache")]
    [InlineData("settings/diagnostics", "diagnostics")]
    [InlineData("settings/ai", "ai-assistance")]
    [InlineData("diagnostics", "diagnostics")]
    [InlineData("settings/no-such-section", "general")]
    public void Each_route_opens_its_section_and_only_that_one(string route, string expected)
    {
        var cut = _host.Open(route);

        Assert.Equal(new[] { expected }, SectionsShown(cut));
        Assert.Equal("page", cut.Find($"[data-testid=settings-nav-{expected}]").GetAttribute("aria-current"));
    }

    [Fact]
    public void The_section_list_has_the_nine_sections_and_navigates()
    {
        var cut = _host.Open("settings");

        Assert.Equal(9, cut.FindAll("[data-testid^=settings-nav-]").Count);
        cut.Find("[data-testid=settings-nav-queries]").Click();

        Assert.EndsWith("/settings/queries", _host.Nav.Uri);
    }

    [Fact]
    public async Task At_settings_ai_the_active_provider_radios_are_the_first_radios()
    {
        await _host.Stores.Vault.SetKeyAsync("ollama", new AiProviderConfig { DisplayName = "Ollama", Model = "llama3", Endpoint = "http://localhost:11434" }, string.Empty);

        var cut = _host.Open("settings/ai");

        cut.WaitForAssertion(() => Assert.Equal("active", cut.FindAll("input[type=radio]").First().GetAttribute("name")));
        Assert.Equal(new[] { "ai-assistance" }, SectionsShown(cut));
    }

    [Fact]
    public void Connections_has_no_Restore_defaults_and_every_other_section_has_one()
    {
        foreach (var id in new[] { "general", "editor", "format", "queries", "code-analysis", "ai-assistance", "schema-cache", "diagnostics" })
        {
            var cut = _host.Open($"settings/{id}");
            Assert.NotNull(cut.Find($"[data-testid=settings-restore-{id}]"));
        }
        Assert.Empty(_host.Open("settings/connections").FindAll("[data-testid=settings-restore-connections]"));
    }

    [Fact]
    public void The_navigation_bar_has_five_places()
    {
        _host.Nav.NavigateTo("/settings");
        var nav = _host.Ctx.Render<NavMenu>();

        Assert.Equal(new[] { "Editor", "Snippets", "Format styles", "History", "Settings" },
            nav.FindAll("a.akml-nav-link").Select(a => a.TextContent.Trim()));
    }

    // Spec 041 (T203, FR-085) — Settings uses the one page header (its Schema cache and Diagnostics sections share it).
    [Fact]
    public void Settings_has_the_shared_page_header()
    {
        var cut = _host.Open("settings/schema-cache");

        var header = cut.Find("[data-testid=page-header]");
        Assert.Contains("akml-page-header", header.ClassList);
        Assert.Equal("Settings", header.QuerySelector("[data-testid=page-title]")!.TextContent.Trim());
        Assert.NotNull(header.QuerySelector("[data-testid=page-actions] [data-testid=settings-export]"));
        Assert.Single(cut.FindAll("[data-testid=page-header]"));
    }
}

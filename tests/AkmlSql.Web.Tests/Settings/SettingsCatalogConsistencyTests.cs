using AkmlSql.Web.Services.Settings;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T136, research R55, FR-061) — the catalog the filter searches and the rows the sections render are the
/// same list, and the filter on the page: matching rows under their section names, sections with no match greyed
/// in the list, the open section back in full when the box is cleared.
/// </summary>
public sealed class SettingsCatalogConsistencyTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Every_catalog_entry_is_rendered_and_every_rendered_row_is_in_the_catalog()
    {
        foreach (var section in SettingsCatalog.Sections)
        {
            var cut = _host.Open($"settings/{section.Id}");
            var rendered = cut.FindAll("[data-setting]").Select(e => e.GetAttribute("data-setting")).OrderBy(x => x).ToArray();
            var catalog = SettingsCatalog.Entries.Where(e => e.SectionId == section.Id).Select(e => e.Id).OrderBy(x => x).ToArray();

            Assert.True(catalog.SequenceEqual(rendered),
                $"{section.Id}: catalog [{string.Join(", ", catalog)}] vs rendered [{string.Join(", ", rendered)}]");
            Assert.All(rendered, id => Assert.NotNull(cut.Find($"[data-testid=setting-{id}]")));
        }
    }

    [Fact]
    public void Timeout_shows_only_the_matching_row_under_its_section_and_greys_sections_without_a_match()
    {
        var cut = _host.Open("settings");

        cut.Find("[data-testid=settings-filter]").Input("timeout");

        Assert.Equal(new[] { "queries-timeout" }, cut.FindAll("[data-setting]").Select(e => e.GetAttribute("data-setting")));
        Assert.Equal("Queries", Assert.Single(cut.FindAll(".akml-settings-section-title")).TextContent.Trim());
        Assert.Equal("true", cut.Find("[data-testid=settings-nav-general]").GetAttribute("aria-disabled"));
        Assert.Null(cut.Find("[data-testid=settings-nav-queries]").GetAttribute("aria-disabled"));
    }

    [Fact]
    public void A_section_title_match_shows_its_tables_and_forms()
    {
        var cut = _host.Open("settings");

        cut.Find("[data-testid=settings-filter]").Input("connections");

        Assert.NotNull(cut.Find("[data-testid=settings-section-connections]"));
        Assert.NotNull(cut.Find("[data-testid=setting-connections-engines]"));
        Assert.NotNull(cut.Find("[data-testid=setting-connections-sql]"));
    }

    [Fact]
    public void Clearing_the_filter_brings_the_open_section_back_in_full()
    {
        var cut = _host.Open("settings/queries");
        int full = cut.FindAll("[data-setting]").Count;

        cut.Find("[data-testid=settings-filter]").Input("timeout");
        Assert.Single(cut.FindAll("[data-setting]"));
        cut.Find("[data-testid=settings-filter]").Input("");

        Assert.Equal(full, cut.FindAll("[data-setting]").Count);
        Assert.Equal(new[] { "settings-section-queries" }, cut.FindAll("[data-testid^=settings-section-]").Select(e => e.GetAttribute("data-testid")));
    }

    [Fact]
    public void Nothing_matching_says_so()
    {
        var cut = _host.Open("settings");

        cut.Find("[data-testid=settings-filter]").Input("zzzz-no-such-setting");

        Assert.Contains("No setting matches", cut.Find("[data-testid=settings-no-match]").TextContent);
    }
}

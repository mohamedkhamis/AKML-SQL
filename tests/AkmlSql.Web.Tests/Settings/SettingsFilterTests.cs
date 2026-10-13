using AkmlSql.Web.Services.Settings;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T136, research R55, FR-051, FR-061) — the nine sections in SSMS Options order, and the filter that
/// searches every section: a setting matches on its label, help or keywords (ordinal, ignoring case); each
/// section counts its matches so the list can grey the ones with none.
/// </summary>
public sealed class SettingsFilterTests
{
    [Fact]
    public void The_nine_sections_are_in_order_with_the_contract_ids_and_titles()
    {
        Assert.Equal(new[]
        {
            ("general", "General"),
            ("editor", "Editor"),
            ("format", "Format"),
            ("queries", "Queries"),
            ("code-analysis", "Code analysis"),
            ("ai-assistance", "AI assistance"),
            ("connections", "Connections"),
            ("schema-cache", "Schema cache"),
            ("diagnostics", "Diagnostics"),
        }, SettingsCatalog.Sections.Select(s => (s.Id, s.Title)));
    }

    [Theory]
    [InlineData("ai", "ai-assistance")]
    [InlineData("ai-assistance", "ai-assistance")]
    [InlineData("Queries", "queries")]
    [InlineData("schema-cache", "schema-cache")]
    [InlineData("nope", "general")]
    [InlineData(null, "general")]
    [InlineData("", "general")]
    public void A_route_resolves_to_its_section_and_anything_else_to_General(string? route, string expected)
    {
        Assert.Equal(expected, SettingsCatalog.Resolve(route).Id);
    }

    [Fact]
    public void Every_entry_belongs_to_a_section_and_ids_are_unique()
    {
        var sections = SettingsCatalog.Sections.Select(s => s.Id).ToHashSet();
        Assert.All(SettingsCatalog.Entries, e => Assert.Contains(e.SectionId, sections));
        Assert.Equal(SettingsCatalog.Entries.Count, SettingsCatalog.Entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Timeout_matches_only_the_command_timeout()
    {
        var filter = new SettingsFilter("TIMEOUT");

        var matches = SettingsCatalog.Entries.Where(filter.Matches).Select(e => e.Id).ToArray();

        Assert.Equal(new[] { "queries-timeout" }, matches);
        Assert.Equal(1, filter.MatchCount("queries"));
        Assert.Equal(0, filter.MatchCount("general"));
    }

    [Fact]
    public void A_keyword_matches_too()
    {
        var filter = new SettingsFilter("dark");

        Assert.Contains(SettingsCatalog.Entries.Where(filter.Matches), e => e.Id == "theme");
    }

    [Fact]
    public void An_empty_filter_is_inactive_and_matches_everything()
    {
        Assert.False(SettingsFilter.None.IsActive);
        Assert.False(new SettingsFilter("   ").IsActive);
        Assert.All(SettingsCatalog.Entries, e => Assert.True(SettingsFilter.None.Matches(e)));
    }

    [Fact]
    public void A_section_title_match_counts_for_its_tables_and_forms()
    {
        // Sections whose content is a table or a form (Connections, Schema cache) match on their title.
        var filter = new SettingsFilter("schema cache");

        Assert.True(filter.TitleMatches(SettingsCatalog.Resolve("schema-cache")));
        Assert.True(filter.MatchCount("schema-cache") > 0);
        Assert.False(filter.TitleMatches(SettingsCatalog.Resolve("general")));
    }
}

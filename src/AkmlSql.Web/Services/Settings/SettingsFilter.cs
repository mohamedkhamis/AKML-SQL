using System;
using System.Linq;

namespace AkmlSql.Web.Services.Settings;

/// <summary>
/// Spec 041 (research R55, FR-061) — the Settings filter, cascaded to every <c>SettingRow</c>: a new instance per
/// change, so the rows re-render. A setting matches when its label, help or keywords contain the text
/// (ordinal, ignoring case); a section whose title matches shows its tables and forms too.
/// </summary>
public sealed record SettingsFilter(string Text)
{
    public static SettingsFilter None { get; } = new(string.Empty);

    public bool IsActive => !string.IsNullOrWhiteSpace(Text);

    private string Needle => Text.Trim();

    private bool Contains(string? haystack) =>
        !string.IsNullOrEmpty(haystack) && haystack!.IndexOf(Needle, StringComparison.OrdinalIgnoreCase) >= 0;

    public bool Matches(SettingEntry entry) =>
        !IsActive || Contains(entry.Label) || Contains(entry.Help) || Contains(entry.Keywords);

    /// <summary>For a row with no catalog entry: its own label and help.</summary>
    public bool Matches(string label, string? help) => !IsActive || Contains(label) || Contains(help);

    public bool TitleMatches(SettingsSectionInfo section) => IsActive && Contains(section.Title);

    /// <summary>How many of the section's settings match (all of them when its title does).</summary>
    public int MatchCount(string sectionId)
    {
        var section = SettingsCatalog.Resolve(sectionId);
        bool title = TitleMatches(section);
        return SettingsCatalog.Entries.Count(e => e.SectionId == sectionId && (title || Matches(e)));
    }
}

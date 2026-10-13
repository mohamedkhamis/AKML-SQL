using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services.Settings;

/// <summary>A section of the Settings page.</summary>
public sealed record SettingsSectionInfo(string Id, string Title);

/// <summary>One simple preference: what the filter searches and what <c>SettingRow</c> renders as <c>setting-{Id}</c>.</summary>
public sealed record SettingEntry(string Id, string SectionId, string Label, string Help, string Keywords = "");

/// <summary>
/// Spec 041 (research R55, FR-051, contracts/ui.md §6) — the Settings page's sections, in the order of SSMS's
/// Options window, and every simple preference on them. The filter searches this table; a consistency test
/// keeps it in step with the <c>setting-{id}</c> rows the sections render.
/// </summary>
public static class SettingsCatalog
{
    public const string General = "general";
    public const string Editor = "editor";
    public const string Format = "format";
    public const string Queries = "queries";
    public const string CodeAnalysis = "code-analysis";
    public const string AiAssistance = "ai-assistance";
    public const string Connections = "connections";
    public const string SchemaCache = "schema-cache";
    public const string Diagnostics = "diagnostics";

    public static IReadOnlyList<SettingsSectionInfo> Sections { get; } = new[]
    {
        new SettingsSectionInfo(General, "General"),
        new SettingsSectionInfo(Editor, "Editor"),
        new SettingsSectionInfo(Format, "Format"),
        new SettingsSectionInfo(Queries, "Queries"),
        new SettingsSectionInfo(CodeAnalysis, "Code analysis"),
        new SettingsSectionInfo(AiAssistance, "AI assistance"),
        new SettingsSectionInfo(Connections, "Connections"),
        new SettingsSectionInfo(SchemaCache, "Schema cache"),
        new SettingsSectionInfo(Diagnostics, "Diagnostics"),
    };

    /// <summary>Old routes kept working (<c>/settings/ai</c> was the AI page).</summary>
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ai"] = AiAssistance,
    };

    public static IReadOnlyList<SettingEntry> Entries { get; } = new[]
    {
        new SettingEntry("theme", General, "Theme", "Light, dark or high contrast; System follows Windows.", "dark light high contrast colour color appearance"),

        new SettingEntry("editor-font-size", Editor, "Font size", "The editor's text size, 9 to 32 pixels.", "zoom text"),
        new SettingEntry("editor-word-wrap", Editor, "Word wrap", "Wrap long lines at the edge of the editor instead of scrolling sideways."),
        new SettingEntry("editor-tab-size", Editor, "Tab size", "Spaces a tab stands for, 1 to 8.", "indent"),
        new SettingEntry("editor-reset-session", Editor, "Reset editor session", "Clears the document kept between visits; it cannot be undone.", "clear document"),

        new SettingEntry("format-style", Format, "Style", "The style Format SQL uses. Edit styles on the Format styles page.", "profile formatting"),

        new SettingEntry("queries-max-rows", Queries, "Default row limit", "The most rows a query returns to the grid. The toolbar can change it for this session.", "rows limit"),
        new SettingEntry("queries-timeout", Queries, "Command timeout", "Seconds a query may run before it is stopped. The toolbar can change it for this session.", "seconds"),
        new SettingEntry("queries-show-column-types", Queries, "Show column types", "Show each column's data type under its name in the results grid.", "grid header"),
        new SettingEntry("queries-retain-line-breaks", Queries, "Retain line breaks on copy or save", "Keep line breaks inside values when copying or saving results; off replaces them with a space.", "grid clipboard csv"),

        new SettingEntry("analysis-enabled", CodeAnalysis, "Enable code analysis", "Check scripts against the analysis rules when you click Analyse.", "rules analyse analyze"),
        new SettingEntry("analysis-reanalyse-after-format", CodeAnalysis, "Re-analyse after format", "Analyse the document again after Format SQL.", "analyze"),
        new SettingEntry("analysis-problems-filter", CodeAnalysis, "Default Problems filters", "The severities the Problems tab shows when it opens.", "info warning error severity"),
        new SettingEntry("analysis-suppressed-rules", CodeAnalysis, "Suppressed rules", "Rules turned off everywhere. Undo brings a rule back.", "disabled ignore"),

        new SettingEntry("ai-providers", AiAssistance, "Providers", "The AI services you use and which one is active. Keys never leave this browser.", "openai anthropic ollama key model"),
        new SettingEntry("ai-privacy", AiAssistance, "Privacy", "How much of your schema is sent with an AI request.", "schema disclosure"),
        new SettingEntry("ai-ghost-text", AiAssistance, "Ghost text", "Suggest the rest of a line as you type.", "inline completion suggestion"),

        new SettingEntry("connections-engines", Connections, "Engine connections", "The AKML SQL engines this browser is paired with.", "pair pin engine"),
        new SettingEntry("connections-sql", Connections, "SQL Server connections", "Saved SQL Server connections. Passwords are never saved.", "server database login"),

        new SettingEntry("schema-cache-databases", SchemaCache, "Cached databases", "Schema kept in this browser for completion and the Schema panel.", "refresh clear"),

        new SettingEntry("diagnostics-engine", Diagnostics, "Engine", "The paired engine: its version, how it runs and the account it runs as.", "service account version"),
        new SettingEntry("diagnostics-levels", Diagnostics, "Levels shown", "Which log entries the table shows.", "log info warn error debug"),
        new SettingEntry("diagnostics-log", Diagnostics, "Log", "Recent events in this browser. Export or clear them.", "export clear"),
    };

    /// <summary>The section for a route value: its id, an alias, or General.</summary>
    public static SettingsSectionInfo Resolve(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return Sections[0];
        var id = Aliases.TryGetValue(route, out var alias) ? alias : route;
        return Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Sections[0];
    }

    public static SettingEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);
}

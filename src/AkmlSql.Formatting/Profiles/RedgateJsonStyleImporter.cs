using System.Text.Json;

namespace AkmlSql.Formatting.Profiles;

public static class RedgateOptionStatus
{
    public const string Mapped = "mapped";
    public const string MappedPendingRender = "mapped-pending-render";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";
}

public sealed record RedgateOptionReport(string Path, string Value, string Status, string? Reason);

public sealed class RedgateStyleImportResult
{
    public bool Success { get; init; }
    public string? ParseError { get; init; }
    public FormattingProfile Profile { get; init; } = new();
    public IReadOnlyList<RedgateOptionReport> Options { get; init; } = [];
    public int MappedCount => Options.Count(o => o.Status is RedgateOptionStatus.Mapped or RedgateOptionStatus.MappedPendingRender);
    public int UnsupportedCount => Options.Count(o => o.Status == RedgateOptionStatus.Unsupported);
    public int UnknownCount => Options.Count(o => o.Status == RedgateOptionStatus.Unknown);
}

/// <summary>
/// Spec 031 FR-001..FR-007 — imports modern SQL Prompt JSON style files (10.5+, one file per
/// style, camelCase sections) against the vendored Redgate schema
/// (specs/031-redgate-style-import/reference/formattingstyle-schema.json).
/// Distinct from <see cref="SqlPromptImporter"/>, which parses AKML's own spec-020 XML exports.
/// </summary>
public static class RedgateJsonStyleImporter
{
    /// <summary>
    /// Spec 031 Task 7 (SC-004) — every Redgate option path this importer knows how to classify
    /// (mapped or explicitly unsupported), i.e. <c>RedgateOptionMap.Entries.Keys</c>. Exposed
    /// publicly so the schema-completeness test (and any future consumer) can walk the vendored
    /// Redgate schema without needing <c>InternalsVisibleTo</c> access to <see cref="RedgateOptionMap"/>.
    /// </summary>
    public static IReadOnlyCollection<string> KnownOptionPaths => RedgateOptionMap.Entries.Keys;

    public static RedgateStyleImportResult Import(string jsonContent, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(jsonContent);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            return new RedgateStyleImportResult { Success = false, ParseError = ex.Message };
        }

        using (doc)
        {
            var profile = new FormattingProfile();

            // 1. Materialize Redgate defaults for every mapped option (FR-002).
            foreach (var (path, entry) in RedgateOptionMap.Entries)
                entry.Apply?.Invoke(profile, entry.DefaultValue);

            // 2. Flatten the file to leaf key/value pairs.
            var fileValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flatten(doc.RootElement, prefix: "", fileValues);

            // 3. Metadata (not options).
            fileValues.TryGetValue("metadata.name", out var name);
            fileValues.TryGetValue("metadata.id", out var id);
            fileValues.Remove("metadata.name");
            fileValues.Remove("metadata.id");

            profile.Metadata.Name = string.IsNullOrWhiteSpace(name) ? (fallbackName ?? "Imported style") : name!;
            if (!string.IsNullOrWhiteSpace(id)) profile.Metadata.Id = id!;
            profile.Metadata.BasedOn = "SQL Prompt Import";
            profile.Metadata.IsBuiltIn = false;
            profile.Metadata.Created = DateTime.UtcNow;
            profile.Metadata.Modified = DateTime.UtcNow;

            // 4. Overlay file values + classify every file key (FR-001/FR-007).
            var reports = new List<RedgateOptionReport>(fileValues.Count);
            foreach (var (path, value) in fileValues)
            {
                if (!RedgateOptionMap.Entries.TryGetValue(path, out var entry))
                {
                    reports.Add(new RedgateOptionReport(path, value, RedgateOptionStatus.Unknown,
                        "Not in the vendored Redgate schema (+ documented additions); Redgate default behavior assumed."));
                    continue;
                }
                if (entry.Apply is null)
                {
                    reports.Add(new RedgateOptionReport(path, value, RedgateOptionStatus.Unsupported, entry.UnsupportedReason));
                    continue;
                }
                entry.Apply(profile, value);
                var status = FormatterHonoringTable.IsRendered(path)
                    ? RedgateOptionStatus.Mapped
                    : RedgateOptionStatus.MappedPendingRender;
                reports.Add(new RedgateOptionReport(path, value, status,
                    status == RedgateOptionStatus.MappedPendingRender ? "Stored losslessly; rendering ships in spec 031 phase 3." : null));
            }

            // 5. Post-pass: SP11 threshold-implies-enabled quirk (FR-003).
            RedgateOptionMap.ApplyThresholdImpliesEnabled(profile, fileValues);

            profile.Metadata.Description =
                $"Imported from SQL Prompt JSON style ({reports.Count(r => r.Status != RedgateOptionStatus.Unknown && r.Status != RedgateOptionStatus.Unsupported)} options mapped)";

            return new RedgateStyleImportResult { Success = true, Profile = profile, Options = reports };
        }
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
                Flatten(prop.Value, path, into);
            else
                into[path] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText(),
                };
        }
    }
}

internal sealed class RedgateMappingEntry
{
    public required string DefaultValue { get; init; }
    public Action<FormattingProfile, string>? Apply { get; init; }
    public string? UnsupportedReason { get; init; }
}

internal static partial class RedgateOptionMap
{
    /// <summary>Filled across three partial files: Whitespace/Lists/Parens/Casing, Dml/Ddl/ControlFlow/Cte/Variables, Join/Insert/FunctionCalls/Case/Operators.</summary>
    internal static readonly Dictionary<string, RedgateMappingEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    static RedgateOptionMap()
    {
        RegisterWhitespaceListsParensCasing(); // Task 4
        RegisterDmlDdlControlFlowCteVariables(); // Task 5
        RegisterJoinInsertFunctionCaseOperators(); // Task 6
    }

    static partial void RegisterWhitespaceListsParensCasing();
    static partial void RegisterDmlDdlControlFlowCteVariables();
    static partial void RegisterJoinInsertFunctionCaseOperators();

    internal static void ApplyThresholdImpliesEnabled(FormattingProfile profile, Dictionary<string, string> fileValues)
    {
        // FR-003: enable a collapse iff its threshold key is present AND its gating bool key is absent.
        if (fileValues.ContainsKey("dml.collapseStatementsShorterThan") && !fileValues.ContainsKey("dml.collapseShortStatements"))
            profile.Dml.CollapseShortStatements = true;
        if (fileValues.ContainsKey("dml.collapseSubqueriesShorterThan") && !fileValues.ContainsKey("dml.collapseShortSubqueries"))
            profile.Dml.CollapseShortSubqueries = true;
        if (fileValues.ContainsKey("ddl.collapseStatementsShorterThan") && !fileValues.ContainsKey("ddl.collapseShortStatements"))
            profile.Ddl.CollapseShortDdl = true;
        if (fileValues.ContainsKey("controlFlow.collapseStatementsShorterThan") && !fileValues.ContainsKey("controlFlow.collapseShortStatements"))
            profile.ControlFlow.CollapseShortIfElse = true;
    }
}

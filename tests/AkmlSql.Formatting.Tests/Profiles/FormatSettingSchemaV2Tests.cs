using System.Text;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 033 (T018) — schema-v2 completeness gates, aggregate-offenders style (list every
/// violation, fail once): category hierarchy, enum dropdown values, descriptions, int ranges,
/// flattened insertStatements ids, and the byte-frozen v1 id guarantee. Because these walk
/// the reflected schema, FUTURE profile properties cannot ship without metadata.
/// </summary>
public class FormatSettingSchemaV2Tests
{
    private static readonly FormatSettingSchema Schema = FormatSettingSchema.BuildDefault();

    private static readonly string[] Categories = ["global", "statements", "clauses", "expressions", "other"];

    [Fact]
    public void SchemaVersion_is_2()
    {
        Assert.Equal(2, Schema.SchemaVersion);
    }

    [Fact]
    public void Every_group_is_parented_under_one_of_the_five_categories()
    {
        var offenders = new StringBuilder();
        foreach (var g in Schema.Groups)
        {
            if (g.ParentId == null || !Categories.Contains(g.ParentId))
                offenders.AppendLine($"  {g.Id}: ParentId='{g.ParentId ?? "(null)"}'");
            if (!FormatSettingSchema.CategoryMap.ContainsKey(g.Id))
                offenders.AppendLine($"  {g.Id}: missing from CategoryMap (would silently land in 'other')");
        }
        Assert.True(offenders.Length == 0, "Groups without a valid category:\n" + offenders);
    }

    [Fact]
    public void Category_ids_are_never_emitted_as_group_rows()
    {
        // Old shells render group rows flatly; category rows would appear as empty nodes.
        var offenders = Schema.Groups.Where(g => Categories.Contains(g.Id)).Select(g => g.Id).ToList();
        Assert.True(offenders.Count == 0, "Category ids emitted as group rows: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_setting_has_a_nonempty_description()
    {
        var offenders = Schema.Settings
            .Where(s => string.IsNullOrWhiteSpace(s.Description))
            .Select(s => "  " + s.Id)
            .ToList();
        Assert.True(offenders.Count == 0,
            $"{offenders.Count} setting(s) without a [SettingMeta] Description:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Every_enum_setting_has_allowed_values_containing_its_default()
    {
        var offenders = new StringBuilder();
        foreach (var s in Schema.Settings.Where(s => s.Type == "Enum"))
        {
            if (s.AllowedEnumValues == null || s.AllowedEnumValues.Count == 0)
            {
                offenders.AppendLine($"  {s.Id}: no AllowedValues");
                continue;
            }
            var def = s.Default as string ?? s.Default?.ToString() ?? string.Empty;
            if (!s.AllowedEnumValues.Contains(def, StringComparer.Ordinal))
                offenders.AppendLine($"  {s.Id}: default '{def}' not in [{string.Join(", ", s.AllowedEnumValues)}] (exact spelling required)");
        }
        Assert.True(offenders.Length == 0, "Enum settings with missing/invalid AllowedValues:\n" + offenders);
    }

    [Fact]
    public void Every_int_setting_has_a_range_bracketing_its_default()
    {
        var offenders = new StringBuilder();
        foreach (var s in Schema.Settings.Where(s => s.Type == "Int"))
        {
            if (s.Min == null || s.Max == null)
            {
                offenders.AppendLine($"  {s.Id}: Min/Max not declared");
                continue;
            }
            var def = Convert.ToInt32(s.Default);
            if (s.Min > def || def > s.Max)
                offenders.AppendLine($"  {s.Id}: default {def} outside [{s.Min}, {s.Max}]");
        }
        Assert.True(offenders.Length == 0, "Int settings with missing/invalid ranges:\n" + offenders);
    }

    [Fact]
    public void InsertStatements_sub_objects_are_flattened_not_blobs()
    {
        string[] expected =
        [
            "insertStatements.columns.parenthesisStyle",
            "insertStatements.columns.indentContents",
            "insertStatements.columns.placeSubsequentItemsOnNewLines",
            "insertStatements.values.parenthesisStyle",
            "insertStatements.values.indentContents",
            "insertStatements.values.placeSubsequentItemsOnNewLines",
        ];
        var ids = Schema.Settings.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in expected)
            Assert.Contains(id, ids);

        // The opaque "Other" blob rows must be gone — and nothing else may regress to a blob.
        Assert.DoesNotContain(Schema.Settings, s => s.Id == "insertStatements.columns");
        Assert.DoesNotContain(Schema.Settings, s => s.Id == "insertStatements.values");
        var otherTyped = Schema.Settings.Where(s => s.Type == "Other").Select(s => s.Id).ToList();
        Assert.True(otherTyped.Count == 0, "Unclassified 'Other' settings: " + string.Join(", ", otherTyped));
    }

    [Fact]
    public void V1_group_ids_and_representative_setting_ids_are_byte_frozen()
    {
        // SqlPromptKey resolution and stored working-value ids key on these exact strings.
        string[] v1Groups =
        [
            "whitespace", "casing", "list", "parenthesis", "dml", "join", "ddl", "controlFlow",
            "case", "cte", "expression", "operators", "inStatements", "functionCalls",
            "comments", "declare", "insertStatements", "formatActions",
        ];
        var groupIds = Schema.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var g in v1Groups)
            Assert.Contains(g, groupIds);
        Assert.Equal(v1Groups.Length, Schema.Groups.Count);

        // One well-known v1 id per group (exact case) — a rename here breaks stored mappings.
        string[] v1Settings =
        [
            "whitespace.tabSize", "casing.reservedKeywords", "list.commaPosition",
            "parenthesis.collapseThreshold", "dml.collapseShortStatements", "join.onConditionIndentMode",
            "ddl.collapseThreshold", "controlFlow.collapseShortIfElse", "case.firstWhenOnNewLine",
            "cte.asOnNewLine", "operators.alignment", "inStatements.alignment",
            "functionCalls.indentParameters", "comments.multilineFormatting", "formatActions.applyCasing",
        ];
        var settingIds = Schema.Settings.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var missing = v1Settings.Where(id => !settingIds.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "Frozen v1 setting ids missing/renamed:\n  " + string.Join("\n  ", missing));
    }
}

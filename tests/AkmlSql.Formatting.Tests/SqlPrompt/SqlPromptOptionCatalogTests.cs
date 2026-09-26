using System.Text.Json;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// The option catalog is SQL Prompt's model, not AKML's: every path, type, allowed value and
/// default must be exactly Redgate's (vendored schema), or a style saved here would not open the
/// same way in SQL Prompt.
/// </summary>
public class SqlPromptOptionCatalogTests
{
    private static readonly Dictionary<string, JsonElement> SchemaLeaves = LoadSchemaLeaves();

    private static Dictionary<string, JsonElement> LoadSchemaLeaves()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "formattingstyle-schema.json"));
        var doc = JsonDocument.Parse(json);
        var leaves = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void Walk(JsonElement properties, string prefix)
        {
            foreach (var p in properties.EnumerateObject())
            {
                var path = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
                if (p.Value.TryGetProperty("properties", out var nested)) Walk(nested, path);
                else leaves[path] = p.Value;
            }
        }
        Walk(doc.RootElement.GetProperty("properties"), "");
        return leaves;
    }

    [Fact]
    public void Catalog_covers_every_schema_option_and_nothing_else_but_documented_additions()
    {
        var catalog = SqlPromptOptionCatalog.Options.ToDictionary(o => o.Path, StringComparer.Ordinal);

        var missing = SchemaLeaves.Keys.Where(p => !catalog.ContainsKey(p)).ToList();
        Assert.True(missing.Count == 0, "Schema options missing from the catalog:\n" + string.Join("\n", missing));

        var extra = catalog.Values.Where(o => !SchemaLeaves.ContainsKey(o.Path)).ToList();
        Assert.All(extra, o => Assert.True(o.IsPostSchemaAddition, $"{o.Path} is not in Redgate's schema and not marked as a documented addition."));
        Assert.Equal(114, SchemaLeaves.Count);
        Assert.Equal(115, catalog.Count);
    }

    [Fact]
    public void Types_allowed_values_and_defaults_match_the_schema_exactly()
    {
        foreach (var option in SqlPromptOptionCatalog.Options.Where(o => !o.IsPostSchemaAddition))
        {
            var schema = SchemaLeaves[option.Path];
            var type = schema.GetProperty("type").GetString();
            var expectedKind = schema.TryGetProperty("enum", out _) ? SqlPromptOptionKind.Choice
                : type == "boolean" ? SqlPromptOptionKind.Boolean
                : SqlPromptOptionKind.Integer;
            Assert.True(expectedKind == option.Kind, $"{option.Path}: kind {option.Kind}, schema says {expectedKind}");

            var schemaDefault = schema.GetProperty("default");
            var expectedDefault = schemaDefault.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.String => schemaDefault.GetString(),
                _ => schemaDefault.GetRawText(),
            };
            Assert.True(expectedDefault == option.Default, $"{option.Path}: default {option.Default}, schema says {expectedDefault}");

            if (option.Kind == SqlPromptOptionKind.Choice)
            {
                var values = schema.GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();
                Assert.True(values.SequenceEqual(option.Choices.Select(c => c.Value)),
                    $"{option.Path}: choices [{string.Join(", ", option.Choices.Select(c => c.Value))}], schema says [{string.Join(", ", values)}]");
            }
        }
    }

    [Fact]
    public void Pages_follow_the_sql_prompt_style_editor()
    {
        var pages = SqlPromptOptionCatalog.Sections.Select(s => $"{s.Category}/{s.Label}").ToList();
        Assert.Equal(
        [
            "Global/Whitespace", "Global/Lists", "Global/Parentheses", "Global/Casing",
            "Statements/Data (DML)", "Statements/Schema (DDL)", "Statements/Control flow", "Statements/CTE", "Statements/Variables",
            "Clauses/Join", "Clauses/Insert",
            "Expressions/Function calls", "Expressions/CASE", "Expressions/Operators",
        ], pages);
        Assert.All(SqlPromptOptionCatalog.Sections, s => Assert.All(s.Options, o => Assert.Equal(s.Id, o.Section)));
    }

    [Fact]
    public void Every_option_explains_itself_and_every_gate_points_at_a_real_option()
    {
        foreach (var o in SqlPromptOptionCatalog.Options)
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Label), o.Path);
            Assert.False(string.IsNullOrWhiteSpace(o.Description), o.Path);
            if (o.EnabledWhen is { } gate)
                Assert.NotNull(SqlPromptOptionCatalog.Find(gate.Path));
            if (o.Kind == SqlPromptOptionKind.Integer)
            {
                Assert.NotNull(o.Min);
                Assert.NotNull(o.Max);
            }
        }
    }

    [Fact]
    public void Once_a_page_has_a_sub_heading_every_later_option_has_one()
    {
        // Both editors start a heading only when the group changes to a named one, so an ungrouped
        // option after "List items" was drawn under LIST ITEMS (the DML page's INSERT / DISTINCT /
        // TOP options). An ungrouped option may only come before the first heading.
        foreach (var section in SqlPromptOptionCatalog.Sections)
        {
            var headed = false;
            foreach (var option in section.Options)
            {
                if (option.Group != null) headed = true;
                else Assert.False(headed, $"{section.Id}: '{option.Path}' has no sub-heading but follows one");
            }
        }
    }
}

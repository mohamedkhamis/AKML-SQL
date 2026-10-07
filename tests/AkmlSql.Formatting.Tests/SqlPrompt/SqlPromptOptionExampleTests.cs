using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// The style editors' "what does each value do" cards: every option has an example, and on it
/// every value of the option formats differently from the option's default — so no card ever
/// shows the same code as the default card under SQL Prompt's defaults.
/// </summary>
public class SqlPromptOptionExampleTests
{
    /// <summary>Options whose example cannot change offline, and why.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["casing.useObjectDefinitionCase"] = "Recases identifiers from their definitions in a connected database; there is no database behind an example.",
    };

    public static TheoryData<string> AllOptions()
    {
        var data = new TheoryData<string>();
        foreach (var o in SqlPromptOptionCatalog.Options) data.Add(o.Path);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllOptions))]
    public void Every_value_changes_the_options_example(string path)
    {
        var option = SqlPromptOptionCatalog.Find(path)!;
        var example = SqlPromptOptionExamples.For(path);
        Assert.False(string.IsNullOrWhiteSpace(example.Query.Sql));
        if (Exempt.ContainsKey(path)) return;

        var values = SqlPromptOptionExamples.ValuesFor(option);
        Assert.Equal(option.Default, values[0]);

        var unchanged = new List<string>();
        foreach (var value in values.Skip(1))
        {
            var sql = SqlPromptOptionExamples.For(path, value).Query.Sql;
            var settings = SqlPromptOptionExamples.SettingsFor(path, value);
            if (Format(sql, settings, path, option.Default) == Format(sql, settings, path, value)) unchanged.Add(value);
        }

        // Numbers too: the editors show a card per value, and a card identical to the default's
        // explains nothing.
        Assert.True(unchanged.Count == 0, $"{path}: [{string.Join(", ", unchanged)}] looked the same as the default.");
    }

    [Fact]
    public void Every_example_is_for_a_real_option()
    {
        foreach (var o in SqlPromptOptionCatalog.Options)
        {
            foreach (var value in SqlPromptOptionExamples.ValuesFor(o))
                foreach (var setting in SqlPromptOptionExamples.SettingsFor(o.Path, value))
                    Assert.NotNull(SqlPromptOptionCatalog.Find(setting.Key));
        }
    }

    [Fact]
    public void Select_examples_are_selects_that_format()
    {
        Assert.NotEmpty(SqlPromptOptionExamples.SelectExamples);
        foreach (var q in SqlPromptOptionExamples.SelectExamples)
        {
            Assert.Contains("SELECT", q.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(Format(q.Sql, [], null, null)), q.Id);
        }
    }

    [Fact]
    public void The_editor_schema_carries_the_examples()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(SqlPromptOptionCatalog.ToEditorSchemaJson());
        var root = schema.RootElement;
        var queries = root.GetProperty("exampleQueries").EnumerateArray().Select(q => q.GetProperty("id").GetString()).ToHashSet();
        Assert.All(root.GetProperty("selectExamples").EnumerateArray(), id => Assert.Contains(id.GetString(), queries));
        foreach (var s in root.GetProperty("settings").EnumerateArray())
        {
            var example = s.GetProperty("example");
            var query = example.GetProperty("query").GetString();
            Assert.True(query == SqlPromptOptionExamples.PageSampleId || queries.Contains(query), $"{s.GetProperty("id")}: unknown query {query}");
            Assert.True(example.GetProperty("values").GetArrayLength() >= 2, s.GetProperty("id").GetString());
        }
    }

    private static string Format(string sql, IReadOnlyList<KeyValuePair<string, string>> settings, string? path, string? value)
    {
        var doc = SqlPromptStyleDocument.CreateDefault("example", "example");
        foreach (var s in settings) doc.Set(s.Key, s.Value);
        if (path is not null) doc.Set(path, value!);
        return SqlPromptOptionSensitivityTests.Format(sql, doc);
    }
}

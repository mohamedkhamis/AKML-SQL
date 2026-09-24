using System.Text.Json.Nodes;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

public class SqlPromptStyleDocumentTests
{
    [Fact]
    public void Absent_options_read_as_sql_prompt_defaults()
    {
        var doc = SqlPromptStyleDocument.Parse("{}");
        Assert.Equal("spaces", doc.Get("whitespace.spacesOrTabs"));
        Assert.Equal(4, doc.GetInt("whitespace.numberOfSpacesInTabs"));
        Assert.True(doc.GetBool("lists.alignSubsequentItemsWithFirstItem"));
        Assert.Equal("expandedToStatement", doc.Get("insertStatements.columns.parenthesisStyle"));
    }

    [Fact]
    public void Setting_a_default_value_removes_the_key_and_empty_sections_like_sql_prompt()
    {
        var doc = SqlPromptStyleDocument.CreateDefault("Mine", "id-1");
        doc.Set("lists.placeCommasBeforeItems", "true");
        Assert.Equal("true", doc.Root["lists"]!["placeCommasBeforeItems"]!.ToJsonString());

        doc.Set("lists.placeCommasBeforeItems", "false");
        Assert.Null(doc.Root["lists"]);
        Assert.Equal("""{"metadata":{"id":"id-1","name":"Mine"}}""", doc.ToJson(indented: false));
    }

    [Fact]
    public void Values_are_stored_with_their_json_types_and_redgate_spelling()
    {
        var doc = SqlPromptStyleDocument.CreateDefault("x");
        doc.Set("whitespace.numberOfSpacesInTabs", "2");
        doc.Set("whitespace.spacesOrTabs", "TABSIFPOSSIBLE");
        doc.Set("whitespace.newLines.emptyLinesBetweenStatements", "3");

        var ws = doc.Root["whitespace"]!.AsObject();
        Assert.Equal(2, ws["numberOfSpacesInTabs"]!.GetValue<int>());
        Assert.Equal("tabsIfPossible", ws["spacesOrTabs"]!.GetValue<string>());
        Assert.Equal(3, ws["newLines"]!["emptyLinesBetweenStatements"]!.GetValue<int>());
    }

    [Fact]
    public void Invalid_values_are_rejected_and_out_of_range_integers_clamped()
    {
        var doc = SqlPromptStyleDocument.CreateDefault("x");
        Assert.Throws<ArgumentException>(() => doc.Set("whitespace.spacesOrTabs", "sometimes"));
        Assert.Throws<ArgumentException>(() => doc.Set("lists.alignAliases", "maybe"));
        Assert.Throws<ArgumentException>(() => doc.Set("no.such.option", "true"));

        doc.Set("whitespace.numberOfSpacesInTabs", "500");
        Assert.Equal(16, doc.GetInt("whitespace.numberOfSpacesInTabs"));
    }

    [Fact]
    public void Unknown_keys_from_a_newer_sql_prompt_survive_a_round_trip()
    {
        var doc = SqlPromptStyleDocument.Parse("""{"metadata":{"name":"n"},"lists":{"futureOption":true},"brandNewSection":{"x":1}}""");
        doc.Set("lists.alignAliases", "true");

        Assert.Equal(["lists.futureOption", "brandNewSection.x"], doc.UnknownKeys());
        var reparsed = SqlPromptStyleDocument.Parse(doc.ToJson());
        Assert.True(reparsed.Root["lists"]!["futureOption"]!.GetValue<bool>());
        Assert.Equal(1, reparsed.Root["brandNewSection"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void Keys_and_values_are_read_case_insensitively_like_sql_prompt()
    {
        var doc = SqlPromptStyleDocument.Parse("""{"Lists":{"PlaceCommasBeforeItems":true},"whitespace":{"spacesOrTabs":"Tabs"}}""");
        Assert.True(doc.GetBool("lists.placeCommasBeforeItems"));
        Assert.Equal("tabs", doc.Get("whitespace.spacesOrTabs"));
    }

    [Fact]
    public void A_bom_comments_and_trailing_commas_do_not_stop_an_import()
    {
        var doc = SqlPromptStyleDocument.Parse("\uFEFF{ // exported\n \"lists\": { \"alignAliases\": true, }, }");
        Assert.True(doc.GetBool("lists.alignAliases"));
    }

    [Fact]
    public void Redgates_misspelled_then_alignment_reads_as_the_real_value()
    {
        var doc = SqlPromptStyleDocument.Parse("""{"caseExpressions":{"thenAlignment":"intentedFromWhen"}}""");
        Assert.Equal("indentedFromWhen", doc.Get("caseExpressions.thenAlignment"));
    }

    [Theory]
    [InlineData("dml.collapseShortStatements", "dml.collapseStatementsShorterThan")]
    [InlineData("dml.collapseShortSubqueries", "dml.collapseSubqueriesShorterThan")]
    [InlineData("ddl.collapseShortStatements", "ddl.collapseStatementsShorterThan")]
    [InlineData("controlFlow.collapseShortStatements", "controlFlow.collapseStatementsShorterThan")]
    [InlineData("parentheses.collapseShortParenthesisContents", "parentheses.collapseParenthesesShorterThan")]
    [InlineData("caseExpressions.collapseShortCaseExpressions", "caseExpressions.collapseCaseExpressionsShorterThan")]
    public void A_collapse_threshold_written_without_its_switch_means_the_collapse_is_on(string @switch, string threshold)
    {
        // SQL Prompt 11 writes the threshold and drops the switch (spec 031 FR-003).
        var doc = SqlPromptStyleDocument.CreateDefault("x");
        Assert.False(doc.GetBool(@switch));
        doc.Set(threshold, "150");
        Assert.True(doc.GetBool(@switch));

        // Turning it off must stay off even though the threshold is still written.
        doc.Set(@switch, "false");
        Assert.False(doc.GetBool(@switch));
        Assert.False(SqlPromptStyleDocument.Parse(doc.ToJson()).GetBool(@switch));

        doc.Reset(@switch);
        Assert.False(doc.GetBool(@switch));
    }

    [Fact]
    public void The_users_sql_prompt_11_style_reads_as_written()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));
        var doc = SqlPromptStyleDocument.Parse(json);
        Assert.Equal("MohamedKhamis", doc.Name);
        Assert.Equal("tabsIfPossible", doc.Get("whitespace.spacesOrTabs"));
        Assert.Equal(2, doc.GetInt("whitespace.numberOfSpacesInTabs"));
        Assert.True(doc.GetBool("whitespace.newLines.alignMultilineCommentsMatchingPatterns"));
        Assert.True(doc.GetBool("dml.collapseShortStatements"));      // threshold 160, switch not written
        Assert.Equal(160, doc.GetInt("dml.collapseStatementsShorterThan"));
        Assert.Equal("toFirstListItem", doc.Get("operators.andOr.alignment"));
        Assert.Empty(doc.UnknownKeys());
    }

    [Fact]
    public void FormatsLike_ignores_metadata_and_spelling_of_defaults()
    {
        var a = SqlPromptStyleDocument.Parse("""{"metadata":{"name":"A"},"lists":{"alignAliases":false}}""");
        var b = SqlPromptStyleDocument.Parse("""{"metadata":{"name":"B"}}""");
        Assert.True(a.FormatsLike(b));
        b.Set("lists.alignAliases", "true");
        Assert.False(a.FormatsLike(b));
        Assert.Equal(["lists.alignAliases"], b.ChangedOptions().Select(o => o.Path));
    }

    [Fact]
    public void Metadata_is_written_first_like_sql_prompt_files()
    {
        var doc = SqlPromptStyleDocument.Parse("""{"lists":{"alignAliases":true}}""");
        doc.Name = "Named later";
        Assert.StartsWith("""{"metadata":""", doc.ToJson(indented: false));
        Assert.IsType<JsonObject>(doc.Root["lists"]);
    }
}

public class SqlPromptStyleDocumentFormsTests
{
    [Fact]
    public void The_explicit_form_writes_every_option_and_formats_the_same()
    {
        var doc = SqlPromptStyleDocument.Parse("""{"metadata":{"name":"x"},"dml":{"collapseStatementsShorterThan":160},"lists":{"future":1}}""");
        var explicitDoc = SqlPromptStyleDocument.Parse(doc.ToExplicitJson());

        Assert.All(SqlPromptOptionCatalog.Options, o => Assert.True(explicitDoc.IsSet(o.Path), o.Path));
        Assert.True(doc.FormatsLike(explicitDoc));
        Assert.True(explicitDoc.GetBool("dml.collapseShortStatements"));   // the collapse rule, spelled out
        Assert.Equal(["lists.future"], explicitDoc.UnknownKeys());
        Assert.Equal("x", explicitDoc.Name);
    }

    [Fact]
    public void Minimize_returns_to_sql_prompts_minimal_form_without_changing_the_formatting()
    {
        var original = SqlPromptStyleDocument.Parse("""{"metadata":{"name":"x"},"dml":{"collapseStatementsShorterThan":160},"lists":{"alignAliases":true}}""");
        var doc = SqlPromptStyleDocument.Parse(original.ToExplicitJson());
        doc.Minimize();

        Assert.True(original.FormatsLike(doc));
        Assert.False(doc.IsSet("whitespace.spacesOrTabs"));
        Assert.True(doc.IsSet("lists.alignAliases"));
        Assert.True(doc.GetBool("dml.collapseShortStatements"));
    }

    [Fact]
    public void The_editor_schema_carries_every_option_under_its_page_and_category()
    {
        using var json = System.Text.Json.JsonDocument.Parse(SqlPromptOptionCatalog.ToEditorSchemaJson());
        var root = json.RootElement;
        Assert.Equal("sqlPrompt", root.GetProperty("model").GetString());
        Assert.Equal(14, root.GetProperty("groups").GetArrayLength());
        Assert.Equal(SqlPromptOptionCatalog.Options.Count, root.GetProperty("settings").GetArrayLength());
        foreach (var g in root.GetProperty("groups").EnumerateArray())
        {
            Assert.Contains(g.GetProperty("parentId").GetString(), new[] { "global", "statements", "clauses", "expressions" });
            Assert.False(string.IsNullOrWhiteSpace(g.GetProperty("sample").GetString()));
        }
        var tabs = root.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "sqlPrompt.whitespace.spacesOrTabs");
        Assert.Equal("Enum", tabs.GetProperty("type").GetString());
        Assert.Equal("spaces", tabs.GetProperty("default").GetString());
        Assert.Equal(3, tabs.GetProperty("enumLabels").GetArrayLength());
        var threshold = root.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "sqlPrompt.dml.collapseStatementsShorterThan");
        Assert.Equal("sqlPrompt.dml.collapseShortStatements", threshold.GetProperty("enabledWhen").GetProperty("id").GetString());
        Assert.True(threshold.GetProperty("enabledWhen").GetProperty("value").GetBoolean());
    }
}

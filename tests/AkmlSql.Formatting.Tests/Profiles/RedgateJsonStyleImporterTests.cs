using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class RedgateJsonStyleImporterTests
{
    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    [Fact]
    public void Import_reads_metadata_name_and_id()
    {
        var result = RedgateJsonStyleImporter.Import(UserStyleJson);
        Assert.True(result.Success);
        Assert.Equal("MohamedKhamis", result.Profile.Metadata.Name);
        Assert.Equal("2cd71422-30f2-4360-800f-240f2897fd3e", result.Profile.Metadata.Id);
        Assert.Equal("SQL Prompt Import", result.Profile.Metadata.BasedOn);
    }

    [Fact(Skip = "Un-skip in Task 6 when the mapping table is complete")]
    public void Import_classifies_every_leaf_key_in_the_file()
    {
        var result = RedgateJsonStyleImporter.Import(UserStyleJson);
        // 65 leaf option keys (metadata.id/name are metadata, not options)
        Assert.Equal(65, result.Options.Count);
        Assert.All(result.Options, o => Assert.False(string.IsNullOrEmpty(o.Status)));
        Assert.DoesNotContain(result.Options, o => o.Status == RedgateOptionStatus.Unknown);
    }

    [Fact]
    public void Import_of_malformed_json_fails_without_profile()
    {
        var result = RedgateJsonStyleImporter.Import("<SqlPromptStyle>not json</SqlPromptStyle>");
        Assert.False(result.Success);
        Assert.NotNull(result.ParseError);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Import_of_empty_object_succeeds_with_fallback_name_and_redgate_defaults()
    {
        var result = RedgateJsonStyleImporter.Import("{}", fallbackName: "my-style-file");
        Assert.True(result.Success);
        Assert.Equal("my-style-file", result.Profile.Metadata.Name);
        Assert.Empty(result.Options); // no file keys to classify
        Assert.Equal("spaces", result.Profile.Whitespace.TabStyle);
        Assert.Equal(120, result.Profile.Whitespace.MaxLineWidth);
        Assert.Equal("none", result.Profile.Whitespace.SemicolonPlacement);
    }

    [Fact]
    public void Whitespace_lists_parens_casing_map_from_user_style()
    {
        var p = RedgateJsonStyleImporter.Import(UserStyleJson).Profile;

        Assert.Equal("tabsWhenPossible", p.Whitespace.TabStyle);      // tabsIfPossible
        Assert.Equal(2, p.Whitespace.TabSize);
        Assert.Equal(200, p.Whitespace.MaxLineWidth);
        Assert.Equal("spaceBefore", p.Whitespace.SemicolonPlacement);
        Assert.Equal(2, p.Whitespace.EmptyLineBetweenStatements);
        Assert.Equal(1, p.Whitespace.EmptyLinesAfterBatchSeparator);  // omitted -> Redgate default
        Assert.False(p.Whitespace.PreserveEmptyLines);
        Assert.False(p.Whitespace.PreserveEmptyLinesAfterBatch);
        Assert.Equal("normaliseIndent", p.Comments.MultilineFormatting); // alignMultilineCommentsMatchingPatterns=true
        Assert.True(p.Comments.RecognizeCommonPatterns);

        Assert.True(p.List.AlignItemsToTabStops);
        Assert.Equal("leading", p.List.CommaPosition);
        Assert.True(p.List.SpaceBeforeComma);
        Assert.Equal("toList", p.List.CommaAlignment);
        Assert.True(p.Whitespace.SpaceAfterComma);                    // omitted -> Redgate default true

        Assert.Equal("expandedToStatement", p.Parenthesis.Style);
        Assert.True(p.Parenthesis.IndentContents);
        Assert.True(p.Parenthesis.CollapseShort);
        Assert.Equal(100, p.Parenthesis.CollapseThreshold);
        Assert.True(p.Parenthesis.SpaceInside);

        Assert.Equal("UPPERCASE", p.Casing.ReservedKeywords);
        Assert.Equal("UPPERCASE", p.Casing.BuiltInFunctions);
        Assert.Equal("UPPERCASE", p.Casing.BuiltInDataTypes);
        Assert.True(p.Casing.SyncWithDatabase);                       // useObjectDefinitionCase
    }

    [Fact]
    public void Unknown_key_is_reported_not_dropped()
    {
        var result = RedgateJsonStyleImporter.Import("""{ "whitespace": { "notARealOption": true } }""");
        Assert.True(result.Success);
        var report = Assert.Single(result.Options);
        Assert.Equal("whitespace.notARealOption", report.Path);
        Assert.Equal(RedgateOptionStatus.Unknown, report.Status);
    }
}

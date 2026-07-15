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
        // NOTE: Task 4 Step 1 extends this test with Redgate-default spot-checks
        // (TabStyle "spaces", MaxLineWidth 120, SemicolonPlacement "none") once the mapping table exists.
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

using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

public class SqlPromptStylesTests
{
    private static SqlPromptStyleDocument UsersStyle() =>
        SqlPromptStyleDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json")));

    [Fact]
    public void A_sql_prompt_style_survives_save_and_load_through_the_profile_store()
    {
        var dir = Directory.CreateTempSubdirectory("akml-sqlprompt-");
        try
        {
            var manager = new ProfileManager(Path.Combine(dir.FullName, "builtin"), Path.Combine(dir.FullName, "custom"));
            var document = UsersStyle();
            manager.Save(SqlPromptStyles.ToProfile(document));

            var loaded = manager.Load("MohamedKhamis");
            Assert.True(SqlPromptStyles.IsSqlPromptStyle(loaded));
            var roundTripped = SqlPromptStyles.ToDocument(loaded);
            Assert.True(document.FormatsLike(roundTripped));
            Assert.Equal(document.Id, roundTripped.Id);

            Assert.True(manager.TryReadRaw("MohamedKhamis", out var raw, out _));
            Assert.Contains("\"sqlPrompt\"", raw);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_stored_style_formats_with_the_sql_prompt_layout()
    {
        var profile = SqlPromptStyles.ToProfile(UsersStyle());
        var result = new FormatterPipeline().Format("select a, b from t where x = 1 and y = 2;", profile);
        Assert.True(result.ValidationPassed);
        // SQL Prompt 11 collapse quirk + the user's 160 threshold: short statements stay on one line,
        // keywords upper-cased, a space before the semicolon.
        Assert.Equal("SELECT a , b FROM t WHERE x = 1 AND y = 2 ;", result.FormattedText.TrimEnd());
    }

    [Fact]
    public void Older_builds_still_get_a_projection_in_the_akml_option_groups()
    {
        var profile = SqlPromptStyles.ToProfile(UsersStyle());
        Assert.Equal("UPPERCASE", profile.Casing.ReservedKeywords);
        Assert.Equal("leading", profile.List.CommaPosition);
        Assert.Equal(2, profile.Whitespace.TabSize);
    }

    [Fact]
    public void Overwriting_a_style_keeps_its_description_author_and_format_actions()
    {
        var previous = new FormattingProfile();
        previous.Metadata.Description = "Team style";
        previous.Metadata.Author = "Mohamed";
        previous.FormatActions.InsertSemicolons = true;

        var profile = SqlPromptStyles.ToProfile(UsersStyle(), previous);
        Assert.Equal("Team style", profile.Metadata.Description);
        Assert.Equal("Mohamed", profile.Metadata.Author);
        Assert.True(profile.FormatActions.InsertSemicolons);
        Assert.False(profile.Metadata.IsBuiltIn);
    }

    [Theory]
    [InlineData("khamis-style.akmlstyle")]
    [InlineData("collapsed.akmlstyle")]
    [InlineData("default.akmlstyle")]
    [InlineData("compact.akmlstyle")]
    [InlineData("leading-commas.akmlstyle")]
    public void Akml_model_built_ins_open_as_sql_prompt_documents(string file)
    {
        var path = Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", file);
        var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));
        var document = SqlPromptStyles.ToDocument(profile);

        Assert.Equal(profile.Metadata.Name, document.Name);
        Assert.Empty(document.UnknownKeys());
        // Every value the projection wrote is one SQL Prompt accepts.
        foreach (var option in SqlPromptOptionCatalog.Options) _ = document.Get(option.Path);
    }

    [Fact]
    public void Khamis_style_projects_back_to_the_sql_prompt_style_it_was_made_from()
    {
        var path = Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "khamis-style.akmlstyle");
        var projected = SqlPromptStyles.ToDocument(ProfileSerializer.Deserialize(File.ReadAllText(path)));
        var original = UsersStyle();

        foreach (var p in new[]
                 {
                     "whitespace.spacesOrTabs", "whitespace.numberOfSpacesInTabs", "whitespace.whiteSpaceBeforeSemiColon",
                     "lists.placeCommasBeforeItems", "casing.reservedKeywords", "parentheses.parenthesisStyle",
                     "joinStatements.join.keywordAlignment", "operators.andOr.alignment",
                 })
            Assert.True(original.Get(p) == projected.Get(p), $"{p}: original {original.Get(p)}, projected {projected.Get(p)}");
    }

    [Fact]
    public void An_imported_source_file_beats_the_projection()
    {
        var legacy = new FormattingProfile();
        legacy.Metadata.Name = "Imported";
        var document = SqlPromptStyles.ToDocument(legacy, importedSource: """{"lists":{"alignAliases":true}}""");
        Assert.True(document.GetBool("lists.alignAliases"));
        Assert.Equal("Imported", document.Name);
    }

    [Fact]
    public void Import_report_lists_every_written_option_as_applied_and_keeps_unknown_keys()
    {
        var document = SqlPromptStyleDocument.Parse("""{"lists":{"alignAliases":true,"somethingNew":1},"casing":{"reservedKeywords":"uppercase"}}""");
        var report = SqlPromptStyles.ImportReport(document);

        Assert.Contains(report, r => r.Path == "lists.alignAliases" && r.Status == RedgateOptionStatus.Mapped);
        Assert.Contains(report, r => r.Path == "casing.reservedKeywords" && r.Status == RedgateOptionStatus.Mapped);
        Assert.Contains(report, r => r.Path == "lists.somethingNew" && r.Status == RedgateOptionStatus.Unknown);
    }
}

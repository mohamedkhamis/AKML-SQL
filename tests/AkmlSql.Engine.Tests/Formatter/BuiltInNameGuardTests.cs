using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// FR-008 for every import format and for Duplicate. <see cref="ProfileManager.Save"/> accepts a
/// built-in's name (that is how a built-in is edited), so an import or a copy landing on that name
/// used to become a silent edit of the built-in — and, if the user had already edited it, to
/// overwrite those edits. The shells' overwrite prompt skips shipped names because it relies on
/// the engine refusing them.
/// </summary>
public sealed class BuiltInNameGuardTests : IDisposable
{
    private const string SqlPromptXml = """
        <SqlPromptStyle>
          <Options>
            <Option Name="KeywordCasing" Value="UPPERCASE" />
            <Option Name="TabSize" Value="4" />
          </Options>
        </SqlPromptStyle>
        """;

    private readonly string _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_builtin_{Guid.NewGuid():N}");
    private readonly string _customDir = Path.Combine(Path.GetTempPath(), $"akml_custom_{Guid.NewGuid():N}");
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public BuiltInNameGuardTests()
    {
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        var compact = new FormattingProfile();
        compact.Metadata.Name = "Compact";
        compact.Metadata.IsBuiltIn = true;
        File.WriteAllText(Path.Combine(_builtInDir, "compact.akmlstyle"), ProfileSerializer.Serialize(compact));

        _profiles = new ProfileManager(_builtInDir, _customDir);
        _handler = new FormatRequestHandler(_profiles);
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
        try { Directory.Delete(_customDir, recursive: true); } catch { }
    }

    private string[] CustomFiles() => Directory.GetFiles(_customDir);

    [Fact]
    public void An_xml_sql_prompt_style_named_like_a_built_in_is_refused_and_nothing_is_written()
    {
        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlpromptstylev2",
            FileContent = Encoding.UTF8.GetBytes(SqlPromptXml),
            TargetProfileName = "Compact",
        });

        Assert.False(response.Success);
        Assert.Contains("built-in", response.ErrorMessage);
        Assert.Empty(CustomFiles());
        Assert.False(_profiles.IsCustomizedBuiltIn("Compact"));
    }

    [Fact]
    public void An_akmlstyle_named_like_a_built_in_is_refused()
    {
        var source = new FormattingProfile();
        source.Metadata.Name = "compact";   // case differs: still the built-in's name
        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "akmlstyle",
            FileContent = Encoding.UTF8.GetBytes(ProfileSerializer.Serialize(source)),
        });

        Assert.False(response.Success);
        Assert.Empty(CustomFiles());
    }

    [Fact]
    public void Importing_over_an_edited_built_in_keeps_the_users_edits()
    {
        var edited = _profiles.Load("Compact");
        edited.Metadata.Description = "my edits";
        _profiles.Save(edited);
        Assert.True(_profiles.IsCustomizedBuiltIn("Compact"));

        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlpromptstylev2",
            FileContent = Encoding.UTF8.GetBytes(SqlPromptXml),
            TargetProfileName = "Compact",
        });

        Assert.False(response.Success);
        Assert.Equal("my edits", _profiles.Load("Compact").Metadata.Description);
    }

    [Fact]
    public void An_import_under_its_own_name_still_works()
    {
        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlpromptstylev2",
            FileContent = Encoding.UTF8.GetBytes(SqlPromptXml),
            TargetProfileName = "Team style",
        });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal("Team style", response.ProfileName);
    }

    [Fact]
    public void Duplicate_refuses_a_built_in_name_and_an_existing_style()
    {
        var mine = new FormattingProfile();
        mine.Metadata.Name = "Mine";
        _profiles.Save(mine);
        var other = new FormattingProfile();
        other.Metadata.Name = "Other";
        other.Metadata.Description = "keep me";
        _profiles.Save(other);

        var ontoBuiltIn = _handler.HandleDuplicateProfile(new DuplicateProfileRequest { SourceName = "Mine", NewName = "Compact" });
        var ontoExisting = _handler.HandleDuplicateProfile(new DuplicateProfileRequest { SourceName = "Mine", NewName = "Other" });

        Assert.False(ontoBuiltIn.Success);
        Assert.Contains("built-in", ontoBuiltIn.ErrorMessage);
        Assert.False(_profiles.IsCustomizedBuiltIn("Compact"));
        Assert.False(ontoExisting.Success);
        Assert.Equal("keep me", _profiles.Load("Other").Metadata.Description);
    }
}

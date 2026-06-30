using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Engine.Tests.Formatter;

public class FormatRequestHandlerTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly ProfileManager _profileManager;
    private readonly FormatRequestHandler _handler;

    public FormatRequestHandlerTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        _profileManager = new ProfileManager(_builtInDir, _customDir);
        _handler = new FormatRequestHandler(_profileManager);
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
        try { Directory.Delete(_customDir, recursive: true); } catch { }
    }

    // ── HandleFormat ──────────────────────────────────────────────────────

    [Fact]
    public void HandleFormat_ValidSql_ReturnsSuccess()
    {
        var request = new FormatRequest { Text = "select 1", ProfileName = null };

        var response = _handler.HandleFormat(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormat_ValidSql_FormattedTextNotEmpty()
    {
        var request = new FormatRequest { Text = "select 1", ProfileName = null };

        var response = _handler.HandleFormat(request);

        Assert.False(string.IsNullOrEmpty(response.FormattedText));
    }

    [Fact]
    public void HandleFormat_NullProfile_UsesDefaultProfile()
    {
        var request = new FormatRequest { Text = "SELECT 1", ProfileName = null };

        // Should not throw when profile name is null
        var ex = Record.Exception(() => _handler.HandleFormat(request));

        Assert.Null(ex);
    }

    [Fact]
    public void HandleFormat_UnknownProfile_FallsBackToDefault()
    {
        var request = new FormatRequest { Text = "SELECT 1", ProfileName = "NonExistentProfile" };

        // Should not throw — handler falls back to default profile
        var response = _handler.HandleFormat(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormat_EmptySql_NoException()
    {
        var request = new FormatRequest { Text = "", ProfileName = null };

        var ex = Record.Exception(() => _handler.HandleFormat(request));

        Assert.Null(ex);
    }

    // ── HandleFormatSelection ─────────────────────────────────────────────

    [Fact]
    public void HandleFormatSelection_ValidRange_ReturnsSuccess()
    {
        const string sql = "select col from tbl";
        var request = new FormatSelectionRequest
        {
            Text = sql,
            SelectionStart = 0,
            SelectionEnd = sql.Length,
            ProfileName = null
        };

        var response = _handler.HandleFormatSelection(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormatSelection_PartialRange_NoException()
    {
        const string sql = "select col from tbl";
        var request = new FormatSelectionRequest
        {
            Text = sql,
            SelectionStart = 0,
            SelectionEnd = 10,
            ProfileName = null
        };

        var ex = Record.Exception(() => _handler.HandleFormatSelection(request));

        Assert.Null(ex);
    }

    // ── HandleFormatPreview ───────────────────────────────────────────────

    [Fact]
    public void HandleFormatPreview_ValidProfileJson_ReturnsText()
    {
        var profile = new FormattingProfile();
        var profileJson = ProfileSerializer.Serialize(profile);

        var request = new FormatPreviewRequest
        {
            SampleText = "select 1",
            ProfileJson = profileJson
        };

        var response = _handler.HandleFormatPreview(request);

        Assert.False(string.IsNullOrEmpty(response.FormattedText));
    }

    [Fact]
    public void HandleFormatPreview_InvalidProfileJson_ReturnsSampleText()
    {
        var request = new FormatPreviewRequest
        {
            SampleText = "select 1",
            ProfileJson = "{ invalid json }"
        };

        // Should return original sample on failure
        var response = _handler.HandleFormatPreview(request);

        Assert.NotNull(response.FormattedText);
    }

    // ── HandleProfileList ─────────────────────────────────────────────────

    [Fact]
    public void HandleProfileList_EmptyDirectories_ReturnsEmptyList()
    {
        var response = _handler.HandleProfileList();

        Assert.NotNull(response.Profiles);
        Assert.Empty(response.Profiles);
    }

    [Fact]
    public void HandleProfileList_SavedProfile_AppearsInList()
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = "TestProfile";
        _profileManager.Save(profile);

        var response = _handler.HandleProfileList();

        Assert.Contains(response.Profiles, p => p.Name == "TestProfile");
    }

    // ── HandleDuplicateProfile (Spec 030 T020) ──────────────────────────

    [Fact]
    public void HandleDuplicateProfile_ExistingSource_CreatesCopy()
    {
        var source = new FormattingProfile();
        source.Metadata.Name = "Source";
        _profileManager.Save(source);

        var response = _handler.HandleDuplicateProfile(
            new DuplicateProfileRequest { SourceName = "Source", NewName = "Source copy" });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal("Source copy", response.NewName);
        Assert.Contains(_handler.HandleProfileList().Profiles, p => p.Name == "Source copy");
    }

    [Fact]
    public void HandleDuplicateProfile_CopyIsBasedOnSource_AndNotBuiltIn()
    {
        var source = new FormattingProfile();
        source.Metadata.Name = "Base";
        _profileManager.Save(source);

        _handler.HandleDuplicateProfile(new DuplicateProfileRequest { SourceName = "Base", NewName = "Base copy" });

        var copy = _profileManager.Load("Base copy");
        Assert.Equal("Base", copy.Metadata.BasedOn);
        Assert.False(copy.Metadata.IsBuiltIn);
    }

    [Fact]
    public void HandleDuplicateProfile_MissingSource_ReturnsFailure()
    {
        var response = _handler.HandleDuplicateProfile(
            new DuplicateProfileRequest { SourceName = "DoesNotExist", NewName = "X" });

        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    // ── HandleStyleEditorSchema (Spec 020 US3 T049) ──────────────────────

    [Fact]
    public void HandleStyleEditorSchema_NullClientVersion_ReturnsFullSchema()
    {
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest
        {
            ClientSchemaVersion = null,
            IncludeUnsupported = true,
        });

        Assert.True(response.SchemaVersion > 0, "SchemaVersion should be positive");
        Assert.False(response.Cached);
        Assert.NotNull(response.SchemaJson);
        Assert.NotEqual(string.Empty, response.SchemaJson);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public void HandleStyleEditorSchema_MatchingClientVersion_ReturnsCachedNoBody()
    {
        // First call: get the current version
        var first = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest());
        var version = first.SchemaVersion;

        // Second call with matching version
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest
        {
            ClientSchemaVersion = version,
        });

        Assert.Equal(version, response.SchemaVersion);
        Assert.True(response.Cached, "Should short-circuit when ClientSchemaVersion matches");
        Assert.Null(response.SchemaJson);
    }

    [Fact]
    public void HandleStyleEditorSchema_MismatchedClientVersion_ReturnsFullSchema()
    {
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest
        {
            ClientSchemaVersion = 999_999, // certainly not the current version
        });

        Assert.False(response.Cached);
        Assert.NotNull(response.SchemaJson);
    }

    [Fact]
    public void HandleStyleEditorSchema_SchemaJsonContainsGroupsAndSettings()
    {
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest());

        Assert.NotNull(response.SchemaJson);
        using var doc = System.Text.Json.JsonDocument.Parse(response.SchemaJson!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("groups", out var groups));
        Assert.True(root.TryGetProperty("settings", out var settings));
        Assert.True(groups.GetArrayLength() > 0, "Expected at least one group");
        Assert.True(settings.GetArrayLength() > 0, "Expected at least one setting");
    }

    [Fact]
    public void HandleStyleEditorSchema_EverySettingResolvesToAGroup()
    {
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest());

        using var doc = System.Text.Json.JsonDocument.Parse(response.SchemaJson!);
        var groupIds = doc.RootElement.GetProperty("groups")
            .EnumerateArray()
            .Select(g => g.GetProperty("id").GetString())
            .ToHashSet();

        foreach (var setting in doc.RootElement.GetProperty("settings").EnumerateArray())
        {
            var groupId = setting.GetProperty("groupId").GetString();
            Assert.True(groupId != null && groupIds.Contains(groupId),
                $"Setting '{setting.GetProperty("id").GetString()}' references unknown groupId '{groupId}'");
        }
    }

    // ── HandleProfileExportSqlPrompt (T031) ──────────────────────────────────

    [Fact]
    public void HandleProfileExportSqlPrompt_SavedProfile_WritesXmlFile()
    {
        var profile = new FormattingProfile { Casing = { ReservedKeywords = "UPPERCASE" } };
        profile.Metadata.Name = "ExportTest";
        _profileManager.Save(profile);

        var dest = Path.Combine(_customDir, "exported.sqlpromptstylev2");
        var response = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "ExportTest",
            DestinationPath = dest,
        });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.True(response.WrittenCount > 0);
        Assert.True(File.Exists(dest));

        var xml = File.ReadAllText(dest);
        Assert.Contains("<SqlPromptStyle>", xml);
        Assert.Contains("KeywordCasing", xml);
        Assert.Contains("UPPERCASE", xml);
    }

    [Fact]
    public void HandleProfileExportSqlPrompt_RoundTrips_PreservesSettings()
    {
        var profile = new FormattingProfile
        {
            Casing = { ReservedKeywords = "lowercase" },
            Whitespace = { TabSize = 2 },
            Join = { AlignJoinKeyword = "none" },
        };
        profile.Metadata.Name = "RoundTripExport";
        _profileManager.Save(profile);

        var dest = Path.Combine(_customDir, "roundtrip.sqlpromptstylev2");
        var exportResponse = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "RoundTripExport",
            DestinationPath = dest,
        });
        Assert.True(exportResponse.Success);

        var reimported = SqlPromptImporter.ImportFromFile(dest);
        Assert.Equal("lowercase", reimported.Profile.Casing.ReservedKeywords);
        Assert.Equal(2, reimported.Profile.Whitespace.TabSize);
        Assert.Equal("none", reimported.Profile.Join.AlignJoinKeyword);
    }

    [Fact]
    public void HandleProfileExportSqlPrompt_UnknownProfile_ReturnsFailure()
    {
        var dest = Path.Combine(_customDir, "wont-be-written.sqlpromptstylev2");
        var response = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "DoesNotExist",
            DestinationPath = dest,
        });

        Assert.False(response.Success);
        Assert.False(string.IsNullOrEmpty(response.ErrorMessage));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void HandleProfileExportSqlPrompt_EmptyDestination_ReturnsFailure()
    {
        var response = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "Anything",
            DestinationPath = "",
        });

        Assert.False(response.Success);
        Assert.Contains("DestinationPath", response.ErrorMessage);
    }

    [Fact]
    public void HandleProfileExportSqlPrompt_RelativeDestination_ReturnsFailure()
    {
        var response = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "Anything",
            DestinationPath = @"relative\path.sqlpromptstylev2",
        });

        Assert.False(response.Success);
        Assert.Contains("absolute", response.ErrorMessage);
    }

    [Fact]
    public void HandleProfileExportSqlPrompt_PathTraversal_ReturnsFailure()
    {
        var dest = Path.Combine(_customDir, "subdir", "..", "escape.sqlpromptstylev2");
        var response = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest
        {
            Name = "Anything",
            DestinationPath = dest,
        });

        Assert.False(response.Success);
        Assert.Contains("canonical", response.ErrorMessage);
    }
}

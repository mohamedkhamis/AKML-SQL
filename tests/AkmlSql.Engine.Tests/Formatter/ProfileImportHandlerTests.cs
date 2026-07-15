using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Spec 031 Task 8 — engine-level coverage for <see cref="FormatRequestHandler.HandleProfileImport"/>:
/// content sniffing (JSON vs XML), visible failure semantics (no partial save on failure), verbatim
/// source preservation next to the saved profile, built-in name collision rejection, and the UTF-8
/// BOM edge case (Encoding.UTF8.GetString keeps a leading U+FEFF that is NOT char.IsWhiteSpace).
/// </summary>
public class ProfileImportHandlerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akml-031-").FullName;
    private readonly string _customDir;
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public ProfileImportHandlerTests()
    {
        _customDir = Path.Combine(_dir, "custom");
        _profiles = new ProfileManager(
            builtInProfilesPath: Path.Combine(_dir, "builtin"),
            customProfilesPath: _customDir);
        _handler = new FormatRequestHandler(_profiles);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    private ProfileImportResponse Import(string content, string format = "sqlprompt") =>
        _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = format,
            FileContent = Encoding.UTF8.GetBytes(content),
        });

    // Directory.GetFiles throws DirectoryNotFoundException if the custom dir was never created
    // (only ProfileManager.Save() creates it) — a failed import that saves nothing legitimately
    // leaves the directory absent, so treat "absent" as "empty" rather than asserting it exists.
    private string[] CustomDirFiles() =>
        Directory.Exists(_customDir) ? Directory.GetFiles(_customDir) : [];

    [Fact]
    public void Json_content_with_sqlprompt_format_routes_to_json_importer()
    {
        var response = Import(UserStyleJson);
        Assert.True(response.Success);
        Assert.Equal("MohamedKhamis", response.ProfileName);
        Assert.NotNull(response.OptionReports);
        Assert.Equal(65, response.OptionReports!.Length);
        Assert.DoesNotContain(response.OptionReports, r => r.Status == "unknown");
        // Saved artifacts:
        Assert.True(File.Exists(Path.Combine(_customDir, "MohamedKhamis.akmlstyle")));
        Assert.True(File.Exists(Path.Combine(_customDir, "MohamedKhamis.source.json")));
    }

    [Fact]
    public void Malformed_content_fails_and_saves_nothing()
    {
        var response = Import("not { valid <xml> or json");
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Empty(CustomDirFiles());
    }

    [Fact]
    public void Xml_content_still_routes_to_legacy_importer()
    {
        const string xml = """<SqlPromptStyle><Options><Option Name="KeywordCasing" Value="uppercase"/></Options></SqlPromptStyle>""";
        var response = Import(xml);
        Assert.True(response.Success);
        Assert.Equal(1, response.MappedOptionsCount);
    }

    [Fact]
    public void Utf8_bom_prefixed_json_still_imports()
    {
        var bomBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(UserStyleJson)).ToArray();
        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = bomBytes,
        });
        Assert.True(response.Success);
        Assert.Equal("MohamedKhamis", response.ProfileName);
    }

    [Fact]
    public void BuiltIn_name_collision_fails_with_clear_error()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "builtin"));
        File.WriteAllText(Path.Combine(_dir, "builtin", "Default.akmlstyle"),
            ProfileSerializer.Serialize(new FormattingProfile { Metadata = { Name = "Default", IsBuiltIn = true } }));

        var response = Import("""{ "metadata": { "name": "Default" } }""");
        Assert.False(response.Success);
        Assert.Contains("built-in", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text;
using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// SQL Prompt styles through the engine — the styles folder SSMS and a paired web
/// edition share: import keeps SQL Prompt's document, ProfileGet hands editors the style as a SQL
/// Prompt document, the Format Styles window gets SQL Prompt's option model, preview formats from
/// the document, and export writes a .json SQL Prompt imports as it is.
/// </summary>
public class SqlPromptStyleHandlerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akml-sqlprompt-").FullName;
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public SqlPromptStyleHandlerTests()
    {
        var builtIn = Path.Combine(_dir, "builtin");
        Directory.CreateDirectory(builtIn);
        var classic = new FormattingProfile();
        classic.Metadata.Name = "Classic";
        classic.List.CommaPosition = "leading";
        File.WriteAllText(Path.Combine(builtIn, "classic.akmlstyle"), ProfileSerializer.Serialize(classic));
        _profiles = new ProfileManager(builtIn, Path.Combine(_dir, "custom"));
        _handler = new FormatRequestHandler(_profiles);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    private string Import()
    {
        var import = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = Encoding.UTF8.GetBytes(UserStyleJson),
        });
        Assert.True(import.Success, import.ErrorMessage);
        return import.ProfileName!;
    }

    [Fact]
    public void Import_keeps_the_sql_prompt_document_and_reports_every_option_as_applied()
    {
        var import = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = Encoding.UTF8.GetBytes(UserStyleJson),
        });

        Assert.True(import.Success);
        Assert.Equal("MohamedKhamis", import.ProfileName);
        Assert.Equal(0, import.UnmappedOptionsCount);
        Assert.All(import.OptionReports!, r => Assert.Equal(RedgateOptionStatus.Mapped, r.Status));

        var stored = _profiles.Load("MohamedKhamis");
        Assert.True(SqlPromptStyles.IsSqlPromptStyle(stored));
        Assert.True(SqlPromptStyleDocument.Parse(UserStyleJson).FormatsLike(SqlPromptStyles.ToDocument(stored)));
        Assert.Contains(_handler.HandleProfileList().Profiles, p => p.Name == "MohamedKhamis" && p.IsSqlPromptStyle);
    }

    [Fact]
    public void ProfileGet_hands_editors_the_style_as_an_explicit_sql_prompt_document()
    {
        var name = Import();
        var get = _handler.HandleProfileGet(new ProfileGetRequest { Name = name });

        Assert.True(get.IsSqlPromptStyle);
        var document = SqlPromptStyleDocument.Parse(get.SqlPromptJson!);
        Assert.All(SqlPromptOptionCatalog.Options, o => Assert.True(document.IsSet(o.Path), o.Path));
        Assert.True(document.GetBool("dml.collapseShortStatements"));   // SQL Prompt 11's implied switch, spelled out
        Assert.True(SqlPromptStyleDocument.Parse(UserStyleJson).FormatsLike(document));
    }

    [Fact]
    public void A_classic_style_is_read_in_sql_prompt_terms_and_becomes_a_sql_prompt_style_when_saved()
    {
        var get = _handler.HandleProfileGet(new ProfileGetRequest { Name = "Classic" });
        Assert.False(get.IsSqlPromptStyle);
        var document = SqlPromptStyleDocument.Parse(get.SqlPromptJson!);
        Assert.True(document.GetBool("lists.placeCommasBeforeItems"));

        document.Set("casing.reservedKeywords", "lowercase");
        var profile = ProfileSerializer.Deserialize(get.ProfileJson!);
        profile.SqlPrompt = document.Root;
        var save = _handler.HandleProfileSave(new ProfileSaveRequest { Name = "Classic", ProfileJson = ProfileSerializer.Serialize(profile) });
        Assert.True(save.Success, save.ErrorMessage);

        var after = _handler.HandleProfileGet(new ProfileGetRequest { Name = "Classic" });
        Assert.True(after.IsSqlPromptStyle);
        Assert.True(after.IsCustomizedBuiltIn);
        Assert.Equal("lowercase", SqlPromptStyleDocument.Parse(after.SqlPromptJson!).Get("casing.reservedKeywords"));
        Assert.Equal("lowercase", _profiles.Load("Classic").Casing.ReservedKeywords);   // projection refreshed
    }

    [Fact]
    public void The_format_styles_window_gets_sql_prompts_option_model_when_it_asks()
    {
        var response = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest { SqlPromptModel = true });
        Assert.Equal(SqlPromptOptionCatalog.SchemaVersion, response.SchemaVersion);
        using var json = JsonDocument.Parse(response.SchemaJson!);
        Assert.Equal("sqlPrompt", json.RootElement.GetProperty("model").GetString());

        var cached = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest { SqlPromptModel = true, ClientSchemaVersion = SqlPromptOptionCatalog.SchemaVersion });
        Assert.True(cached.Cached);
        Assert.Null(cached.SchemaJson);

        var akml = _handler.HandleStyleEditorSchema(new StyleEditorSchemaRequest());
        using var akmlJson = JsonDocument.Parse(akml.SchemaJson!);
        Assert.False(akmlJson.RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public void Preview_formats_from_the_sql_prompt_document()
    {
        var name = Import();
        var get = _handler.HandleProfileGet(new ProfileGetRequest { Name = name });
        var preview = _handler.HandleFormatPreview(new FormatPreviewRequest { SampleText = "select a, b from t;", ProfileJson = get.ProfileJson! });

        Assert.Null(preview.ValidationError);
        Assert.Equal("SELECT a , b FROM t ;", preview.FormattedText.Trim());
    }

    [Fact]
    public void Export_to_json_writes_a_style_sql_prompt_can_import_as_it_is()
    {
        var name = Import();
        var path = Path.Combine(_dir, "out", "MohamedKhamis.json");
        var export = _handler.HandleProfileExportSqlPrompt(new ProfileExportSqlPromptRequest { Name = name, DestinationPath = path });

        Assert.True(export.Success, export.ErrorMessage);
        var exported = SqlPromptStyleDocument.Parse(File.ReadAllText(path));
        var original = SqlPromptStyleDocument.Parse(UserStyleJson);
        Assert.True(original.FormatsLike(exported));
        Assert.Equal(original.Id, exported.Id);
        Assert.False(exported.IsSet("whitespace.wrapLongLines"));   // minimal, like SQL Prompt's own files
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);   // no UTF-8 BOM (ReadAllText would hide one)
    }

    [Fact]
    public void The_engine_advertises_sql_prompt_styles_to_the_web_edition()
    {
        Assert.Contains(Capabilities.StylesSqlPromptV1, Capabilities.Current);
    }
}

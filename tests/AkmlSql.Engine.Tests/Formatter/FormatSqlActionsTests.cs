using System.Text;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Handlers.Formatting;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using AkmlSql.Formatting.Profiles;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Spec 040 (T175, STY-11, FR-065, research R29) — Format SQL actions. The shell sends the user's
/// "When you run Format SQL, AKML SQL will:" choices with Format Document and Format Selection
/// (FormatRequest / FormatSelectionRequest key 5). No actions = today's output exactly; each
/// ticked action runs, and unticked ones don't.
/// </summary>
public sealed class FormatSqlActionsTests : IDisposable
{
    private readonly string _root;
    private readonly string _builtInDir;
    private readonly FormatRequestHandler _handler;

    private const string Khamis = "Khamis Style";

    public FormatSqlActionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "akml_fsa_" + Guid.NewGuid().ToString("N"));
        _builtInDir = Path.Combine(_root, "builtin");
        Directory.CreateDirectory(_builtInDir);

        // The shipped styles, with the idempotency pass off — the setting the format-parity
        // goldens were captured with, so the engine's output can be compared with them byte for byte.
        foreach (var stem in new[] { "khamis-style", "default", "minimalist" })
        {
            var profile = ProfileSerializer.Deserialize(File.ReadAllText(BuiltInStylePath(stem)));
            profile.Metadata.EnableIdempotencyCheck = false;
            File.WriteAllText(Path.Combine(_builtInDir, stem + ".akmlstyle"), ProfileSerializer.Serialize(profile));
        }

        _handler = new FormatRequestHandler(new ProfileManager(_builtInDir, Path.Combine(_root, "custom")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private FormatResponse Format(string sql, FormatSqlActionsDto? actions, string profile = Khamis) =>
        _handler.HandleFormat(new FormatRequest { Text = sql, ProfileName = profile, Actions = actions });

    // ------------------------------------------------------------ today's output

    [Theory]
    [InlineData("01-simple-select", "khamis-style", "Khamis Style")]
    [InlineData("05-case-searched", "default", "Default")]
    [InlineData("02-multi-join", "minimalist", "Minimalist")]   // its formatActions say applyLayout false — never read, so laid out
    public void Without_actions_the_output_is_exactly_todays(string corpus, string styleStem, string styleName)
    {
        var input = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "format-parity", "corpus", corpus + ".sql"));
        var golden = Normalise(File.ReadAllText(Path.Combine(RepoRoot(), "tests", "format-parity", "golden", $"{corpus}__{styleStem}.sql")));

        var none = Format(input, actions: null, styleName);
        Assert.True(none.Success);
        Assert.Equal(golden, Normalise(none.FormattedText));

        // The shell's defaults (a new FormatSqlActions) are today's behaviour too, so sending
        // them changes nobody's output.
        var defaults = Format(input, new FormatSqlActionsDto(), styleName);
        Assert.True(defaults.Success);
        Assert.Equal(golden, Normalise(defaults.FormattedText));
    }

    [Fact]
    public void Format_selection_without_actions_is_unchanged_too()
    {
        const string text = "select a from t; select b from u;";
        var none = _handler.HandleFormatSelection(new FormatSelectionRequest
        {
            Text = text, SelectionStart = 0, SelectionEnd = 5, ProfileName = Khamis,
        });
        var defaults = _handler.HandleFormatSelection(new FormatSelectionRequest
        {
            Text = text, SelectionStart = 0, SelectionEnd = 5, ProfileName = Khamis, Actions = new FormatSqlActionsDto(),
        });

        Assert.True(none.Success);
        Assert.Equal(none.FormattedText, defaults.FormattedText);
        Assert.Equal(none.OriginalStart, defaults.OriginalStart);
        Assert.Equal(none.OriginalEnd, defaults.OriginalEnd);
    }

    // ------------------------------------------------------------ semicolons

    [Fact]
    public void Semicolons_insert_adds_terminators()
    {
        var r = Format("select a from t", new FormatSqlActionsDto { Semicolons = 1 });

        Assert.True(r.Success);
        Assert.EndsWith(";", r.FormattedText.TrimEnd());
    }

    [Fact]
    public void Semicolons_remove_strips_terminators()
    {
        var r = Format("select a from t; select b from u;", new FormatSqlActionsDto { Semicolons = 2 });

        Assert.True(r.Success);
        Assert.DoesNotContain(";", r.FormattedText);
    }

    [Fact]
    public void Semicolons_leave_keeps_them_as_written()
    {
        var with = Format("select a from t;", new FormatSqlActionsDto { Semicolons = 0 });
        var without = Format("select a from t", new FormatSqlActionsDto { Semicolons = 0 });

        Assert.EndsWith(";", with.FormattedText.TrimEnd());
        Assert.DoesNotContain(";", without.FormattedText);
    }

    [Fact]
    public void Format_selection_runs_the_semicolons_action()
    {
        var r = _handler.HandleFormatSelection(new FormatSelectionRequest
        {
            Text = "select a from t",
            SelectionStart = 0,
            SelectionEnd = 15,
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { Semicolons = 1 },
        });

        Assert.True(r.Success);
        Assert.EndsWith(";", r.FormattedText.TrimEnd());
    }

    // ------------------------------------------------------------ square brackets

    [Fact]
    public void Brackets_add_wraps_identifiers()
    {
        var r = Format("select a from t", new FormatSqlActionsDto { SquareBrackets = 1 });

        Assert.True(r.Success);
        Assert.Contains("[a]", r.FormattedText);
        Assert.Contains("[t]", r.FormattedText);
    }

    [Fact]
    public void Brackets_remove_unwraps_simple_identifiers()
    {
        var r = Format("select [a] from [t]", new FormatSqlActionsDto { SquareBrackets = 2 });

        Assert.True(r.Success);
        Assert.DoesNotContain("[", r.FormattedText);
        Assert.Contains("a", r.FormattedText);
    }

    [Fact]
    public void Brackets_remove_keeps_brackets_a_name_needs()
    {
        // [Order] is a reserved word and [Order Details] has a space: unwrapping either breaks the SQL.
        var r = Format("select [a] from [Order Details] join [Order] on 1 = 1", new FormatSqlActionsDto { SquareBrackets = 2 });

        Assert.True(r.Success);
        Assert.Contains("[Order Details]", r.FormattedText);
        Assert.Contains("[Order]", r.FormattedText);
        Assert.DoesNotContain("[a]", r.FormattedText);
    }

    [Fact]
    public void Brackets_leave_keeps_them_as_written()
    {
        var r = Format("select [a], b from t", new FormatSqlActionsDto { SquareBrackets = 0 });

        Assert.Contains("[a]", r.FormattedText);
        Assert.DoesNotContain("[b]", r.FormattedText);
    }

    // ------------------------------------------------------------ casing

    [Fact]
    public void Apply_casing_off_keeps_the_original_keyword_case()
    {
        var on = Format("select a from t where b = 1", new FormatSqlActionsDto { ApplyCasing = true });
        var off = Format("select a from t where b = 1", new FormatSqlActionsDto { ApplyCasing = false });

        Assert.Contains("SELECT", on.FormattedText);
        Assert.Contains("select", off.FormattedText);
        Assert.Contains("from", off.FormattedText);
        Assert.DoesNotContain("SELECT", off.FormattedText);
        Assert.DoesNotContain("FROM", off.FormattedText);

        // Layout still ran: the clauses moved onto their own lines.
        Assert.Contains("\n", off.FormattedText.Trim());
    }

    // ------------------------------------------------------------ layout

    [Fact]
    public void Apply_layout_off_keeps_the_original_whitespace_but_still_inserts_semicolons()
    {
        const string sql = "select  a ,b\r\nfrom t   where x=1";

        var r = Format(sql, new FormatSqlActionsDto { ApplyLayout = false, ApplyCasing = false, Semicolons = 1 });

        Assert.True(r.Success);
        Assert.Equal(sql + ";", r.FormattedText);
    }

    [Fact]
    public void Apply_layout_off_with_casing_on_only_changes_the_case()
    {
        const string sql = "select  a ,b\r\nfrom t   where x=1";

        var r = Format(sql, new FormatSqlActionsDto { ApplyLayout = false, ApplyCasing = true });

        Assert.True(r.Success);
        Assert.Equal("SELECT  a ,b\r\nFROM t   WHERE x=1", r.FormattedText);
    }

    [Fact]
    public void Apply_layout_and_casing_off_with_nothing_else_leaves_the_text_alone()
    {
        const string sql = "select  a ,b\r\nfrom t   where x=1";

        var r = Format(sql, new FormatSqlActionsDto { ApplyLayout = false, ApplyCasing = false });

        Assert.True(r.Success);
        Assert.Equal(sql, r.FormattedText);
        Assert.False(r.WasModified);
    }

    [Fact]
    public void A_sql_prompt_style_honours_layout_and_casing_too()
    {
        var import = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = Encoding.UTF8.GetBytes(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"))),
        });
        Assert.True(import.Success, import.ErrorMessage);
        var style = import.ProfileName!;
        const string sql = "select  a ,b\r\nfrom t   where x=1";

        var laidOut = Format(sql, new FormatSqlActionsDto(), style);
        Assert.Contains("SELECT", laidOut.FormattedText);

        var noCasing = Format(sql, new FormatSqlActionsDto { ApplyCasing = false }, style);
        Assert.Contains("select", noCasing.FormattedText);
        Assert.DoesNotContain("SELECT", noCasing.FormattedText);
        Assert.NotEqual(sql, noCasing.FormattedText);   // still laid out

        var noLayout = Format(sql, new FormatSqlActionsDto { ApplyLayout = false }, style);
        Assert.Equal("SELECT  a ,b\r\nFROM t   WHERE x=1", noLayout.FormattedText);
    }

    // ------------------------------------------------------------ schema-aware actions

    private const string SessionId = "session-fsa";

    private static (SchemaCacheManager Cache, SessionManager Sessions) SchemaFixture()
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = SessionId,
            ConnectionString = "Data Source=(local);Initial Catalog=Northwind;Integrated Security=true",
            DatabaseName = "Northwind",
        });

        var schemaCache = new SchemaCacheManager();
        var cache = schemaCache.GetOrCreateCache(SessionId, "Northwind");
        var dbo = new SchemaEntry { SchemaName = "dbo" };
        dbo.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Products",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "ProductID", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "ProductName", TypeName = "nvarchar", MaxLength = 40 },
                new Column { ColumnId = 3, ColumnName = "UnitPrice", TypeName = "money", IsNullable = true },
            ],
        });
        cache.Schemas["dbo"] = dbo;
        return (schemaCache, sessions);
    }

    [Fact]
    public void Expand_wildcards_expands_select_star_from_the_schema_cache()
    {
        var (schemaCache, sessions) = SchemaFixture();

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = SessionId,
            Text = "select * from dbo.Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, schemaCache, sessions);

        Assert.True(r.Success);
        Assert.DoesNotContain("*", r.FormattedText);
        Assert.Contains("ProductID", r.FormattedText);
        Assert.Contains("ProductName", r.FormattedText);
        Assert.Contains("UnitPrice", r.FormattedText);
        Assert.Contains("SELECT", r.FormattedText);   // the expanded statement is still formatted
    }

    [Theory]
    [InlineData("USE Northwind\r\nSELECT * FROM dbo.Products")]
    [InlineData("USE Northwind\r\nGO\r\nSELECT * FROM dbo.Products")]
    [InlineData("SELECT 1\r\nSELECT * FROM dbo.Products")]
    public void Expand_wildcards_expands_a_star_after_other_statements(string text)
    {
        var (schemaCache, sessions) = SchemaFixture();

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = SessionId,
            Text = text,
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, schemaCache, sessions);

        Assert.True(r.Success);
        Assert.DoesNotContain("*", r.FormattedText);
        Assert.Contains("ProductName", r.FormattedText);
    }

    [Fact]
    public void Expanded_columns_that_need_brackets_get_them()
    {
        // Raw names made SQL that does not parse, and it was written into the editor anyway.
        var (schemaCache, sessions) = SchemaFixture();
        var products = schemaCache.GetCache(SessionId, "Northwind")!.FindObject("dbo", "Products")!;
        products.Columns.Add(new Column { ColumnId = 4, ColumnName = "Order Date", TypeName = "datetime" });
        products.Columns.Add(new Column { ColumnId = 5, ColumnName = "Order", TypeName = "int" });

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = SessionId,
            Text = "select * from dbo.Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, schemaCache, sessions);

        Assert.True(r.Success);
        Assert.DoesNotContain("*", r.FormattedText);
        Assert.Contains("[Order Date]", r.FormattedText);
        Assert.Contains("[Order]", r.FormattedText);
        new AkmlSql.Engine.Parser.TsqlParserService().Parse(r.FormattedText, out var errors);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(FormatSqlActionsDto.UseStyle, true)]
    [InlineData(FormatSqlActionsDto.Leave, false)]
    public void As_the_style_says_lets_the_styles_own_semicolons_apply(int semicolons, bool expectSemicolon)
    {
        // A style that inserts semicolons stopped getting them on Format SQL: the shell always sent
        // an explicit "leave". The default is now "as the style says".
        var profile = new FormattingProfile { FormatActions = { InsertSemicolons = true } };
        var options = FormatRequestHandler.ToPipelineOptions(new FormatSqlActionsDto { Semicolons = semicolons });

        var r = new AkmlSql.Formatting.Pipeline.FormatterPipeline().Format("SELECT 1", profile, options);

        Assert.True(r.Success);
        Assert.Equal(expectSemicolon, r.FormattedText.TrimEnd().EndsWith(";"));
    }

    [Fact]
    public void Unticked_expand_wildcards_leaves_the_star()
    {
        var (schemaCache, sessions) = SchemaFixture();

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = SessionId,
            Text = "select * from dbo.Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = false },
        }, schemaCache, sessions);

        Assert.Contains("*", r.FormattedText);
    }

    [Fact]
    public void Qualify_object_names_adds_the_schema()
    {
        var (schemaCache, sessions) = SchemaFixture();

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = SessionId,
            Text = "select ProductName from Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { QualifyObjectNames = true },
        }, schemaCache, sessions);

        Assert.True(r.Success);
        Assert.Contains("dbo.Products", r.FormattedText);
    }

    [Fact]
    public void Expand_wildcards_without_a_connected_session_formats_and_says_why()
    {
        var (schemaCache, sessions) = SchemaFixture();

        var r = _handler.HandleFormat(new FormatRequest
        {
            SessionId = "not-a-session",
            Text = "select * from dbo.Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, schemaCache, sessions);

        Assert.True(r.Success);
        Assert.Contains("SELECT", r.FormattedText);
        Assert.Contains("*", r.FormattedText);
        Assert.Contains(r.Diagnostics, d => d.Message.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_format_document_handler_passes_the_schema_cache_and_sessions()
    {
        var (schemaCache, sessions) = SchemaFixture();
        var ctx = new RpcContext
        {
            Sessions = sessions,
            SchemaCache = schemaCache,
            Logger = Log.Logger,
            SettingsLoader = () => new AkmlSql.Core.Config.AppSettings(),
        };

        var r = await new FormatDocumentHandler(_handler).HandleAsync(new FormatRequest
        {
            SessionId = SessionId,
            Text = "select * from dbo.Products",
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, ctx, CancellationToken.None);

        Assert.Contains("ProductName", r.FormattedText);

        var selection = await new FormatSelectionHandler(_handler).HandleAsync(new FormatSelectionRequest
        {
            SessionId = SessionId,
            Text = "select * from dbo.Products",
            SelectionStart = 0,
            SelectionEnd = 8,
            ProfileName = Khamis,
            Actions = new FormatSqlActionsDto { ExpandWildcards = true },
        }, ctx, CancellationToken.None);

        Assert.Contains("ProductName", selection.FormattedText);
    }

    // ------------------------------------------------------------ helpers

    private static string BuiltInStylePath(string stem) =>
        Path.Combine(RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", stem + ".akmlstyle");

    /// <summary>The format-parity normalisation (tests/format-parity/README.md).</summary>
    private static string Normalise(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return string.Join("\n", text.Split('\n').Select(l => l.TrimEnd()));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
    }
}

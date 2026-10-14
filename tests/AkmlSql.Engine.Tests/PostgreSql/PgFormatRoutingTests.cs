using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Formatting.Profiles;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T152, FR-140 to FR-147). Format requests of a PostgreSQL session reach the PostgreSQL formatter: the style comes as JSON
/// (the web edition) or by name, the Format SQL actions work (case only, terminators, expand wildcards, qualify names with PostgreSQL
/// quoting), the Format Styles preview takes a PostgreSQL sample, and none of it reaches the T-SQL parser. Requests go through the real router.
/// </summary>
[Collection(TsqlParserCounterCollection.Name)]
public sealed class PgFormatRoutingTests
{
    private readonly EngineComposition _engine = EngineComposition.Build();
    private readonly string _pg = "pg-" + Guid.NewGuid().ToString("N");
    private readonly string _sql = "sql-" + Guid.NewGuid().ToString("N");
    private int _requestId;

    public PgFormatRoutingTests()
    {
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo { SessionId = _pg, ConnectionString = "Host=h;Database=shop", DatabaseName = "shop", Kind = (int)DatabaseKind.PostgreSql });
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo { SessionId = _sql, ConnectionString = "Server=s;Database=shop", DatabaseName = "shop", Kind = (int)DatabaseKind.SqlServer });

        var cache = _engine.Context.SchemaCache.GetOrCreateCache(_pg, "shop");
        cache.Phase = PopulationPhase.Complete;
        Seed(cache, "public", "customers", "customer_id", "Company Name", "country");
        Seed(cache, "sales", "orders", "order_id", "customer_id", "total");
    }

    private static void Seed(DatabaseCache cache, string schema, string table, params string[] columns)
    {
        var entry = cache.Schemas.GetOrAdd(schema, s => new SchemaEntry { SchemaName = s });
        var o = new DatabaseObject { ObjectId = entry.Objects.Count + 1, SchemaName = schema, ObjectName = table, ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        for (var i = 0; i < columns.Length; i++)
            o.Columns.Add(new Column { ColumnId = i + 1, ColumnName = columns[i], TypeName = "text", IsPrimaryKey = i == 0 });
        entry.Objects.Add(o);
    }

    private async Task<TResponse> Ask<TRequest, TResponse>(int type, TRequest request) where TRequest : class
    {
        var message = new RpcMessage { MessageType = type, RequestId = ++_requestId, Payload = MessagePackSerializer.Serialize(request) };
        var response = await _engine.Router.RouteAsync(message, _engine.Context, CancellationToken.None);
        Assert.NotNull(response);
        return MessagePackSerializer.Deserialize<TResponse>(response!.Payload!);
    }

    private static string StyleJson(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", name + ".akmlstyle"));
    }

    private Task<FormatActionResponse> Action(FormatActionType type, string text, int start = 0, int length = 0) =>
        Ask<FormatActionRequest, FormatActionResponse>(MessageTypes.FormatAction,
            new FormatActionRequest { SessionId = _pg, Text = text, ActionType = (int)type, SelectionStart = start, SelectionLength = length });

    // ── the style comes with the request ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_style_sent_as_JSON_is_the_one_the_script_is_laid_out_with()
    {
        const string sql = "select a, b from t where x = 1 and y = 2";

        var compact = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _pg, Text = sql, ProfileJson = StyleJson("compact") });
        var leading = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _pg, Text = sql + ";\nselect aaaaaaaaaaaa, bbbbbbbbbbbbbbbb, cccccccccccccccc, dddddddddddddddd, eeeeeeeeeeeeeee from tttttttttttttttt where x = 1 and y = 2 and z = 3;", ProfileJson = StyleJson("leading-commas") });

        Assert.True(compact.Success && leading.Success);
        Assert.NotEqual(sql, compact.FormattedText);
        Assert.Contains("\n     , ", leading.FormattedText);     // commas first, as that style says
        Assert.Null(compact.ProfileFallbackWarning);
    }

    [Fact]
    public async Task A_style_that_cannot_be_read_falls_back_to_the_defaults_and_says_so()
    {
        var response = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument,
            new FormatRequest { SessionId = _pg, Text = "select 1", ProfileJson = "{ not json" });

        Assert.True(response.Success);
        Assert.NotNull(response.ProfileFallbackWarning);
    }

    [Fact]
    public async Task A_syntax_error_comes_back_unchanged_with_the_position()
    {
        const string sql = "select 1;\nselec 2";

        var response = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _pg, Text = sql });

        Assert.False(response.Success);
        Assert.Equal(sql, response.FormattedText);
        Assert.Equal(2, response.Diagnostics![0].Line);
        Assert.Contains("syntax error", response.Diagnostics[0].Message);
    }

    // ── Format SQL actions ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Format_SQL_actions_of_the_dialog_apply_case_and_terminators_without_the_layout()
    {
        var actions = new FormatSqlActionsDto { ApplyLayout = false, ApplyCasing = true, Semicolons = FormatSqlActionsDto.Insert };
        var response = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument,
            new FormatRequest { SessionId = _pg, Text = "select  a   from  t", ProfileJson = StyleJson("default"), Actions = actions });

        Assert.True(response.Success);
        Assert.Equal("SELECT  a   FROM  t;", response.FormattedText);
    }

    [Fact]
    public async Task Expand_wildcards_writes_the_columns_with_PostgreSQL_quoting()
    {
        var response = await Action(FormatActionType.ExpandWildcards, "select * from customers");

        Assert.True(response.Success);
        Assert.Equal("select customer_id, \"Company Name\", country from customers", response.FormattedText);
    }

    [Fact]
    public async Task Expand_wildcards_with_several_tables_qualifies_each_column()
    {
        var response = await Action(FormatActionType.ExpandWildcards, "select * from customers c join sales.orders o on o.customer_id = c.customer_id");

        Assert.True(response.Success);
        Assert.Contains("c.customer_id, c.\"Company Name\", c.country, o.order_id, o.customer_id, o.total", response.FormattedText);
    }

    [Fact]
    public async Task Expand_wildcards_leaves_a_count_star_alone_and_without_a_schema_says_why()
    {
        var counted = await Action(FormatActionType.ExpandWildcards, "select count(*) from customers");
        Assert.False(counted.WasModified);

        // No session cache for this session id: nothing to expand against.
        var noCache = await Ask<FormatActionRequest, FormatActionResponse>(MessageTypes.FormatAction,
            new FormatActionRequest { SessionId = "no-such-session", Text = "select * from customers", ActionType = (int)FormatActionType.ExpandWildcards });
        Assert.False(noCache.WasModified);
    }

    [Fact]
    public async Task Qualify_names_writes_the_schema_of_each_table_the_cache_finds_and_leaves_the_rest()
    {
        var response = await Action(FormatActionType.QualifyObjectNames, "select * from customers c join orders o on o.customer_id = c.customer_id join mystery m on true");

        Assert.True(response.Success);
        Assert.Equal("select * from public.customers c join orders o on o.customer_id = c.customer_id join mystery m on true", response.FormattedText);
    }

    [Fact]
    public async Task Case_only_and_terminator_actions_act_on_the_selection_when_there_is_one()
    {
        const string sql = "select  1 ;\nselect  2 ;\nselect  3 ;";
        var start = sql.IndexOf("select  2", StringComparison.Ordinal);

        var response = await Action(FormatActionType.CasingOnly, sql, start, "select  2".Length);

        Assert.True(response.Success);
        Assert.Equal("select  1 ;\nSELECT  2 ;\nselect  3 ;", response.FormattedText);
    }

    [Fact]
    public async Task An_action_with_no_PostgreSQL_meaning_is_refused_with_the_standard_words()
    {
        var response = await Action(FormatActionType.AddSquareBrackets, "select 1");

        Assert.False(response.Success);
        Assert.Equal("Not available for PostgreSQL", response.ErrorMessage);
    }

    // ── preview and the T-SQL parser ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_style_preview_formats_a_PostgreSQL_sample_when_asked_in_PostgreSQL()
    {
        // LIMIT and :: are not T-SQL: if the T-SQL formatter saw this it would report errors.
        var request = new FormatPreviewRequest
        {
            SessionId = string.Empty,
            SampleText = "select a::int from t order by a limit 5",
            ProfileJson = StyleJson("default"),
            Dialect = (int)DatabaseKind.PostgreSql,
        };

        var response = await Ask<FormatPreviewRequest, FormatPreviewResponse>(MessageTypes.FormatPreview, request);

        Assert.Null(response.ValidationError);
        Assert.Contains("LIMIT 5", response.FormattedText);
    }

    [Fact]
    public async Task A_SQL_Server_preview_is_formatted_as_before()
    {
        var response = await Ask<FormatPreviewRequest, FormatPreviewResponse>(MessageTypes.FormatPreview,
            new FormatPreviewRequest { SessionId = _sql, SampleText = "select top 5 a from t", ProfileJson = StyleJson("default") });

        Assert.Null(response.ValidationError);
        Assert.Contains("TOP", response.FormattedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Formatting_a_PostgreSQL_session_never_reaches_the_T_SQL_parser()
    {
        var before = TsqlParserService.TotalInvocations;

        await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _pg, Text = "select a::int from t limit 1", ProfileJson = StyleJson("default") });
        await Ask<FormatSelectionRequest, FormatSelectionResponse>(MessageTypes.FormatSelection,
            new FormatSelectionRequest { SessionId = _pg, Text = "select 1; select 2", SelectionStart = 0, SelectionEnd = 3 });
        await Action(FormatActionType.ExpandWildcards, "select * from customers");
        await Action(FormatActionType.QualifyObjectNames, "select 1 from customers");
        await Ask<FormatPreviewRequest, FormatPreviewResponse>(MessageTypes.FormatPreview,
            new FormatPreviewRequest { SampleText = "select 1", ProfileJson = StyleJson("default"), Dialect = 1 });

        Assert.Equal(before, TsqlParserService.TotalInvocations);
    }

    [Fact]
    public void The_new_request_keys_are_appended_not_inserted()
    {
        // Older peers read the same payloads: the new fields sit after every key they know.
        Assert.Equal(6, typeof(FormatRequest).GetProperty(nameof(FormatRequest.ProfileJson))!.GetCustomAttributes(typeof(KeyAttribute), false).Cast<KeyAttribute>().Single().IntKey);
        Assert.Equal(6, typeof(FormatSelectionRequest).GetProperty(nameof(FormatSelectionRequest.ProfileJson))!.GetCustomAttributes(typeof(KeyAttribute), false).Cast<KeyAttribute>().Single().IntKey);
        Assert.Equal(6, typeof(FormatActionRequest).GetProperty(nameof(FormatActionRequest.ProfileJson))!.GetCustomAttributes(typeof(KeyAttribute), false).Cast<KeyAttribute>().Single().IntKey);
        Assert.Equal(3, typeof(FormatPreviewRequest).GetProperty(nameof(FormatPreviewRequest.Dialect))!.GetCustomAttributes(typeof(KeyAttribute), false).Cast<KeyAttribute>().Single().IntKey);
    }
}

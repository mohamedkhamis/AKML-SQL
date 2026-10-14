using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Handlers.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using AkmlSql.PostgreSql.Analysis;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (tasks T160, T171, FR-151, FR-152). The analysis handler gives a PostgreSQL session to the PostgreSQL rules and a SQL Server
/// session to the T-SQL rules, and neither set ever sees the other's text; the rules listing carries both sets, each row marked with its
/// kind, but only for a client that asks (so the SSMS rules window is unchanged); the session suppression store takes PostgreSQL ids.
/// </summary>
[Collection(PostgreSql.TsqlParserCounterCollection.Name)]
public sealed class AnalysisHandlersPgTests : IDisposable
{
    private readonly CaSettingsLoader _loader = new();
    private readonly SessionSuppressionStore _suppressions = new();
    private readonly SessionManager _sessions = new();
    private readonly RpcContext _ctx;
    private readonly AnalysisEngine _tsql;
    private readonly PgAnalysisEngine _pg;
    private readonly string _pgSession = "pg-" + Guid.NewGuid().ToString("N");
    private readonly string _sqlSession = "sql-" + Guid.NewGuid().ToString("N");

    public AnalysisHandlersPgTests()
    {
        _ctx = new RpcContext
        {
            Sessions = _sessions,
            SchemaCache = new SchemaCacheManager { KindOf = _sessions.KindOf },
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
        };
        _tsql = new AnalysisEngine(new TsqlParserService(), new RuleRegistry(), _loader, _suppressions);
        _pg = new PgAnalysisEngine(new PgRuleRegistry(), _loader, _suppressions);

        _sessions.UpdateSession(new ConnectionInfo { SessionId = _pgSession, DatabaseName = "northwind", Kind = (int)DatabaseKind.PostgreSql });
        _sessions.UpdateSession(new ConnectionInfo { SessionId = _sqlSession, DatabaseName = "northwind", Kind = (int)DatabaseKind.SqlServer });
    }

    public void Dispose() => _loader.Dispose();

    private AnalysisHandler Handler(bool postgresOn = true, AppSettings? settings = null) =>
        new(_tsql, () => settings ?? new AppSettings(), postgresOn ? _pg : null);

    private Task<CodeAnalysisResponse> Analyze(AnalysisHandler handler, string session, string sql) =>
        handler.HandleAsync(new CodeAnalysisRequest { SessionId = session, RequestId = "r", DocumentText = sql, DocumentVersion = 3 }, _ctx, CancellationToken.None);

    private static string[] Ids(CodeAnalysisResponse r) => r.Issues.Select(i => i.RuleId).ToArray();

    // ── Routing by kind ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_PostgreSQL_session_is_analysed_by_the_PostgreSQL_rules_only()
    {
        var response = await Analyze(Handler(), _pgSession, "DELETE FROM orders;");

        Assert.Contains("PGBP002", Ids(response));
        Assert.All(Ids(response), id => Assert.StartsWith("PG", id));
        Assert.Equal(3, response.AnalyzedVersion);
        Assert.Equal("r", response.RequestId);
    }

    [Fact]
    public async Task A_SQL_Server_session_is_analysed_by_the_T_SQL_rules_only()
    {
        var response = await Analyze(Handler(), _sqlSession, "DELETE FROM dbo.Orders");

        Assert.Contains("PE003", Ids(response));
        Assert.DoesNotContain(Ids(response), id => id.StartsWith("PG", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostgreSQL_text_with_a_syntax_error_is_one_PostgreSQL_finding_not_a_T_SQL_parse_error()
    {
        var response = await Analyze(Handler(), _pgSession, "SELECT 1;\nselec 2;");

        Assert.Equal(new[] { PgAnalysisEngine.SyntaxRuleId }, Ids(response));
    }

    [Fact]
    public async Task The_T_SQL_parser_is_not_touched_for_a_PostgreSQL_session()
    {
        var before = TsqlParserService.TotalInvocations;

        await Analyze(Handler(), _pgSession, "SELECT * FROM customers WHERE lower(city) = 'x'; DELETE FROM orders;");

        Assert.Equal(before, TsqlParserService.TotalInvocations);

        await Analyze(Handler(), _sqlSession, "SELECT * FROM dbo.Customers");
        Assert.True(TsqlParserService.TotalInvocations > before, "the counter must be a working instrument");
    }

    [Fact]
    public async Task With_PostgreSQL_switched_off_a_PostgreSQL_session_has_no_findings()
    {
        var response = await Analyze(Handler(postgresOn: false), _pgSession, "DELETE FROM orders;");

        Assert.Empty(response.Issues);
        Assert.Equal(3, response.AnalyzedVersion);
    }

    [Fact]
    public async Task Code_analysis_off_returns_nothing_for_a_PostgreSQL_session()
    {
        var settings = new AppSettings();
        settings.CodeAnalysis.Enabled = false;

        var response = await Analyze(Handler(settings: settings), _pgSession, "DELETE FROM orders;");

        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task The_schema_cache_of_the_session_lets_the_schema_aware_rules_run()
    {
        const string sql = "SELECT order_id FROM orders WHERE order_id = 1.5;";
        Assert.DoesNotContain("PGPE005", Ids(await Analyze(Handler(), _pgSession, sql)));

        var cache = _ctx.SchemaCache.GetOrCreateCache(_pgSession, "northwind");
        var entry = cache.Schemas.GetOrAdd("public", s => new SchemaEntry { SchemaName = s });
        entry.Objects.Add(new DatabaseObject
        {
            SchemaName = "public", ObjectName = "orders", ObjectType = DbObjectType.Table, ColumnsLoaded = true,
            Columns = { new Column { ColumnId = 1, ColumnName = "order_id", TypeName = "integer" } },
        });

        Assert.Contains("PGPE005", Ids(await Analyze(Handler(), _pgSession, sql)));
    }

    [Fact]
    public async Task A_session_suppression_of_a_PostgreSQL_rule_hides_it_on_the_next_pass()
    {
        var handler = Handler();
        var suppression = new SessionSuppressionHandler(_suppressions);
        Assert.Contains("PGBP002", Ids(await Analyze(handler, _pgSession, "DELETE FROM orders;")));

        var added = await suppression.HandleAsync(new SessionSuppressionRequest { Action = SessionSuppressionActions.Add, RuleId = "PGBP002" }, _ctx, CancellationToken.None);

        Assert.True(added.Success);
        Assert.Contains("PGBP002", added.SuppressedRules);
        Assert.DoesNotContain("PGBP002", Ids(await Analyze(handler, _pgSession, "DELETE FROM orders;")));

        await suppression.HandleAsync(new SessionSuppressionRequest { Action = SessionSuppressionActions.Clear }, _ctx, CancellationToken.None);
        Assert.Contains("PGBP002", Ids(await Analyze(handler, _pgSession, "DELETE FROM orders;")));
    }

    [Fact]
    public async Task An_everywhere_override_in_the_settings_applies_to_a_PostgreSQL_rule()
    {
        var settings = new AppSettings();
        settings.CodeAnalysis.RuleOverrides["PGBP002"] = new RuleOverride { Enabled = false };

        var response = await Analyze(Handler(settings: settings), _pgSession, "DELETE FROM orders;");

        Assert.DoesNotContain("PGBP002", Ids(response));
    }

    // ── The rules listing ────────────────────────────────────────────────────────────────────────────

    private ListAnalysisRulesHandler Lister(bool postgresOn = true, AppSettings? settings = null) =>
        new(new RuleRegistry(), _loader, () => settings ?? new AppSettings(), postgresOn ? new PgRuleRegistry() : null);

    [Fact]
    public async Task By_default_the_listing_is_the_SQL_Server_rules_exactly_as_before()
    {
        var response = await Lister().HandleAsync(new ListAnalysisRulesRequest(), _ctx, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(new RuleRegistry().AllRules.Count, response.Rules.Length);
        Assert.All(response.Rules, r => Assert.Equal((int)DatabaseKind.SqlServer, r.Kind));
        Assert.DoesNotContain(response.Rules, r => r.RuleId.StartsWith("PG", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_request_that_asks_gets_both_sets_each_row_marked_with_its_kind()
    {
        var response = await Lister().HandleAsync(new ListAnalysisRulesRequest { IncludePostgreSql = true }, _ctx, CancellationToken.None);

        var tsql = response.Rules.Where(r => r.Kind == (int)DatabaseKind.SqlServer).ToList();
        var pg = response.Rules.Where(r => r.Kind == (int)DatabaseKind.PostgreSql).ToList();
        Assert.Equal(new RuleRegistry().AllRules.Count, tsql.Count);
        Assert.Equal(52, pg.Count);
        Assert.All(pg, r =>
        {
            Assert.StartsWith("PG", r.RuleId);
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(r.Description));
            Assert.StartsWith("https://", r.ReferenceUrl);
            Assert.True(r.Enabled);
        });
        Assert.Equal(response.Rules.Length, response.Rules.Select(r => r.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(8, pg.Select(r => r.Category).Distinct().Count());
    }

    [Fact]
    public async Task The_listing_reports_the_state_the_settings_give_a_PostgreSQL_rule()
    {
        var settings = new AppSettings();
        settings.CodeAnalysis.RuleOverrides["PGBP002"] = new RuleOverride { Enabled = false };
        settings.CodeAnalysis.RuleOverrides["PGPE001"] = new RuleOverride { Enabled = true, Severity = "error" };

        var response = await Lister(settings: settings).HandleAsync(new ListAnalysisRulesRequest { IncludePostgreSql = true }, _ctx, CancellationToken.None);

        Assert.False(response.Rules.Single(r => r.RuleId == "PGBP002").Enabled);
        var star = response.Rules.Single(r => r.RuleId == "PGPE001");
        Assert.Equal((int)AkmlSql.Core.Models.Analysis.DiagnosticSeverity.Warning, star.DefaultSeverity);
        Assert.Equal((int)AkmlSql.Core.Models.Analysis.DiagnosticSeverity.Error, star.EffectiveSeverity);
    }

    [Fact]
    public async Task With_PostgreSQL_switched_off_a_request_for_it_still_gets_only_the_SQL_Server_rules()
    {
        var response = await Lister(postgresOn: false).HandleAsync(new ListAnalysisRulesRequest { IncludePostgreSql = true }, _ctx, CancellationToken.None);

        Assert.All(response.Rules, r => Assert.Equal((int)DatabaseKind.SqlServer, r.Kind));
    }
}

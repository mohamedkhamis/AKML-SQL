using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (tasks T160, T172, FR-151, FR-152, FR-155). For a PostgreSQL connection the web asks the engine to analyse and never runs the
/// T-SQL rules; for SQL Server it analyses in the browser and never calls the engine. "Code analysis off" sends no request at all. The
/// browser-local per-rule overrides apply to the engine's findings too. With no engine, or one without PostgreSQL, there are no findings.
/// </summary>
public sealed class AnalyserServicePgRoutingTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    private sealed class SpyEngine : IPostgresAnalysisEngine
    {
        public readonly List<string> Analysed = new();
        public int Listed;
        public CodeIssueInfo[] Issues { get; set; } = { new CodeIssueInfo { RuleId = "PGBP002", Severity = 2, Message = "no where", Line = 1, Column = 1 } };

        public Task<CodeAnalysisResponse> AnalyseAsync(string documentText, CancellationToken ct)
        {
            Analysed.Add(documentText);
            return Task.FromResult(new CodeAnalysisResponse { RequestId = "r", AnalyzedVersion = 1, Issues = Issues });
        }

        public Task<IReadOnlyList<AnalysisRuleInfoDto>> ListRulesAsync(CancellationToken ct)
        {
            Listed++;
            return Task.FromResult<IReadOnlyList<AnalysisRuleInfoDto>>(Array.Empty<AnalysisRuleInfoDto>());
        }
    }

    private sealed class Settings : IAnalysisSettingsStore
    {
        private readonly WebAnalysisSettings _settings;
        public Settings(bool enabled = true, Dictionary<string, string>? overrides = null) =>
            _settings = new WebAnalysisSettings { Enabled = enabled, RuleOverrides = overrides ?? new Dictionary<string, string>() };
        public Task<WebAnalysisSettings> GetAsync() => Task.FromResult(_settings);
        public Task SetAsync(WebAnalysisSettings settings) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private static readonly IDialectGate Postgres = new Gate(DatabaseKind.PostgreSql);
    private static readonly IDialectGate SqlServer = new Gate(DatabaseKind.SqlServer);

    // ── AnalyserService ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task For_PostgreSQL_the_script_goes_to_the_engine_and_its_findings_come_back()
    {
        var engine = new SpyEngine();

        var response = await new AnalyserService(null, Postgres, engine).AnalyseAsync("DELETE FROM t;");

        Assert.Equal("DELETE FROM t;", Assert.Single(engine.Analysed));
        Assert.Equal("PGBP002", Assert.Single(response.Issues).RuleId);
    }

    [Fact]
    public async Task For_SQL_Server_the_script_is_analysed_in_the_browser_and_the_engine_is_never_asked()
    {
        var engine = new SpyEngine();

        var response = await new AnalyserService(null, SqlServer, engine).AnalyseAsync("SELECT * FROM t; DELETE FROM t;");

        Assert.Empty(engine.Analysed);
        Assert.NotEmpty(response.Issues);
        Assert.DoesNotContain(response.Issues, i => i.RuleId.StartsWith("PG", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_analysis_off_sends_no_request_for_a_PostgreSQL_script()
    {
        var engine = new SpyEngine();

        var response = await new AnalyserService(new Settings(enabled: false), Postgres, engine).AnalyseAsync("DELETE FROM t;");

        Assert.Empty(response.Issues);
        Assert.Empty(engine.Analysed);
    }

    [Fact]
    public async Task The_browser_local_overrides_apply_to_the_engine_s_findings()
    {
        var engine = new SpyEngine { Issues = new[]
        {
            new CodeIssueInfo { RuleId = "PGBP002", Severity = 3, Message = "a" },
            new CodeIssueInfo { RuleId = "PGPE001", Severity = 2, Message = "b" },
            new CodeIssueInfo { RuleId = "PGST003", Severity = 0, Message = "c" },
        } };
        var overrides = new Dictionary<string, string> { ["pgbp002"] = "off", ["PGPE001"] = "error" };

        var response = await new AnalyserService(new Settings(overrides: overrides), Postgres, engine).AnalyseAsync("select 1");

        Assert.DoesNotContain(response.Issues, i => i.RuleId == "PGBP002");
        Assert.Equal(3, response.Issues.Single(i => i.RuleId == "PGPE001").Severity);
        Assert.Equal(0, response.Issues.Single(i => i.RuleId == "PGST003").Severity);
    }

    [Fact]
    public async Task For_PostgreSQL_with_no_engine_to_ask_there_are_no_findings()
    {
        var response = await new AnalyserService(null, Postgres, postgres: null).AnalyseAsync("DELETE FROM t;");

        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task A_document_over_the_size_limit_is_refused_before_it_is_sent()
    {
        var engine = new SpyEngine();
        var huge = new string('x', 10 * 1024 * 1024 + 1);

        await Assert.ThrowsAsync<DocumentTooLargeException>(() => new AnalyserService(null, Postgres, engine).AnalyseAsync(huge));

        Assert.Empty(engine.Analysed);
    }

    // ── EnginePostgresAnalysis ───────────────────────────────────────────────────────────────────────

    private sealed class Bridge : IEngineBridge
    {
        public readonly List<(int Type, object Request)> Sent = new();
        public Func<int, object, object?> Answer { get; set; } = (_, _) => null;
        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities { get; set; } = { SqlConnectionService.CapabilityPostgreSql };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
            Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });

        public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
            where TRequest : class where TResponse : class
        {
            Sent.Add((requestMessageType, request));
            return Task.FromResult((TResponse)(Answer(requestMessageType, request) ?? Activator.CreateInstance(typeof(TResponse))!));
        }

        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private sealed class SessionOnly : ISqlConnectionService
    {
        public string SessionId => "session-42";
        public bool IsConnected => true;
        public string? Server => "db1:5432";
        public string? Database => "northwind";
        public DatabaseKind Kind => DatabaseKind.PostgreSql;
        public TransactionState TransactionState => TransactionState.None;
#pragma warning disable CS0067
        public event Action? StateChanged;
#pragma warning restore CS0067
        public void ReportTransactionState(TransactionState state) { }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => Task.FromResult((true, (string?)null));
        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => Task.FromResult((true, (string?)null));
        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>(), (string?)null));
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    private static EnginePostgresAnalysis Over(Bridge bridge) => new(bridge, new SessionOnly());

    [Fact]
    public async Task The_engine_gets_the_script_and_the_session_and_its_findings_are_returned()
    {
        var bridge = new Bridge
        {
            Answer = (_, _) => new CodeAnalysisResponse { RequestId = "x", AnalyzedVersion = 1, Issues = new[] { new CodeIssueInfo { RuleId = "PGEX001", Message = "m" } } },
        };

        var response = await Over(bridge).AnalyseAsync("select 1", default);

        var sent = Assert.IsType<CodeAnalysisRequest>(Assert.Single(bridge.Sent).Request);
        Assert.Equal(MessageTypes.RequestAnalyze, bridge.Sent[0].Type);
        Assert.Equal("session-42", sent.SessionId);
        Assert.Equal("select 1", sent.DocumentText);
        Assert.Equal("PGEX001", Assert.Single(response.Issues).RuleId);
    }

    [Fact]
    public async Task A_closed_bridge_an_old_engine_or_a_failure_gives_no_findings_not_an_error()
    {
        var closed = new Bridge { State = BridgeState.Disconnected };
        var old = new Bridge { EngineCapabilities = Array.Empty<string>() };
        var broken = new Bridge { Answer = (_, _) => throw new InvalidOperationException("dropped") };

        Assert.Empty((await Over(closed).AnalyseAsync("select 1", default)).Issues);
        Assert.Empty((await Over(old).AnalyseAsync("select 1", default)).Issues);
        Assert.Empty((await Over(broken).AnalyseAsync("select 1", default)).Issues);
        Assert.Empty(closed.Sent);
        Assert.Empty(old.Sent);
    }

    [Fact]
    public async Task The_rule_list_asks_for_the_PostgreSQL_set_and_keeps_only_PostgreSQL_rows()
    {
        var bridge = new Bridge
        {
            Answer = (_, _) => new ListAnalysisRulesResponse
            {
                Success = true,
                Rules = new[]
                {
                    new AnalysisRuleInfoDto { RuleId = "PE001", Kind = 0 },
                    new AnalysisRuleInfoDto { RuleId = "PGPE001", Kind = 1, Category = "Performance" },
                },
            },
        };

        var rules = await Over(bridge).ListRulesAsync(default);

        var sent = Assert.IsType<ListAnalysisRulesRequest>(Assert.Single(bridge.Sent).Request);
        Assert.True(sent.IncludePostgreSql);
        Assert.Equal("PGPE001", Assert.Single(rules).RuleId);
    }

    [Fact]
    public async Task The_rule_list_is_empty_when_the_engine_cannot_be_asked_or_fails()
    {
        Assert.Empty(await Over(new Bridge { State = BridgeState.Disconnected }).ListRulesAsync(default));
        Assert.Empty(await Over(new Bridge { Answer = (_, _) => new ListAnalysisRulesResponse { Success = false, Error = "x" } }).ListRulesAsync(default));
        Assert.Empty(await Over(new Bridge { Answer = (_, _) => throw new InvalidOperationException("dropped") }).ListRulesAsync(default));
    }
}

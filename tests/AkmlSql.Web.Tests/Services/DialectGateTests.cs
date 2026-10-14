using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (FR-090, FR-092, tasks T021/T036): the in-browser services carry T-SQL language code. With a PostgreSQL
/// connection active none of it may run on the text: each service answers "nothing" or "not available" instead. With a
/// SQL Server connection, or no gate at all, every service behaves exactly as it did before the feature.
/// </summary>
public sealed class DialectGateTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    private static readonly IDialectGate Postgres = new Gate(DatabaseKind.PostgreSql);
    private static readonly IDialectGate SqlServer = new Gate(DatabaseKind.SqlServer);

    private static IEngineBridge ClosedBridge() =>
        new EngineBridge(() => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    // ── The gate itself ────────────────────────────────────────────────────────────────────────────────────

    private sealed class Connection : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected { get; init; }
        public string? Server => "h";
        public string? Database => "d";
        public DatabaseKind Kind { get; init; }
        public event Action? StateChanged { add { } remove { } }
        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(true, DatabaseKind.PostgreSql, true)]
    [InlineData(true, DatabaseKind.SqlServer, false)]
    [InlineData(false, DatabaseKind.PostgreSql, false)]   // no active connection: SQL Server mode, as today (FR-065)
    public void The_gate_follows_the_active_connection_and_is_SQL_Server_when_there_is_none(bool connected, DatabaseKind kind, bool expectPostgres)
    {
        IDialectGate gate = new DialectGate(new Connection { IsConnected = connected, Kind = kind });

        Assert.Equal(expectPostgres, gate.IsPostgres);
        Assert.Equal(expectPostgres ? DatabaseKind.PostgreSql : DatabaseKind.SqlServer, gate.Kind);
    }

    // ── Formatter ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_formatter_returns_PostgreSQL_text_unchanged_with_the_reason()
    {
        var result = new FormatterService(Postgres).Format("select  a ,b from  t");

        Assert.True(result.Success);
        Assert.False(result.WasModified);
        Assert.Equal("select  a ,b from  t", result.FormattedText);
        Assert.Contains(result.Diagnostics, d => d.Message == DialectMessages.NotAvailable);
    }

    [Fact]
    public void The_formatter_formats_SQL_Server_text_as_before_with_or_without_a_gate()
    {
        var withGate = new FormatterService(SqlServer).Format("select a,b from t");
        var withoutGate = new FormatterService().Format("select a,b from t");

        Assert.True(withGate.WasModified);
        Assert.Equal(withoutGate.FormattedText, withGate.FormattedText);
        Assert.DoesNotContain(withGate.Diagnostics, d => d.Message == DialectMessages.NotAvailable);
    }

    // ── Analyser ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_analyser_reports_nothing_for_PostgreSQL_text()
    {
        const string text = "SELECT * FROM t; DELETE FROM t;";

        var response = await new AnalyserService(null, Postgres).AnalyseAsync(text);

        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task The_analyser_still_reports_T_SQL_findings_for_SQL_Server_text()
    {
        const string text = "SELECT * FROM t; DELETE FROM t;";

        var gated = await new AnalyserService(null, SqlServer).AnalyseAsync(text);
        var ungated = await new AnalyserService().AnalyseAsync(text);

        Assert.NotEmpty(gated.Issues);
        Assert.Equal(ungated.Issues.Length, gated.Issues.Length);
    }

    // ── Completion, quick info, signature help: the offline paths ──────────────────────────────────────────

    [Fact]
    public async Task Offline_completion_gives_no_suggestions_for_PostgreSQL_and_keywords_for_SQL_Server()
    {
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());

        var postgres = await new CompletionService(ClosedBridge(), store, Postgres).CompleteAsync(new CompletionRequest(), CancellationToken.None);
        var sqlServer = await new CompletionService(ClosedBridge(), store, SqlServer).CompleteAsync(new CompletionRequest(), CancellationToken.None);

        Assert.Empty(postgres.Items);
        Assert.Contains(sqlServer.Items, i => i.DisplayText == "SELECT");
    }

    [Fact]
    public async Task Offline_quick_info_and_signature_help_are_empty_for_PostgreSQL()
    {
        var quick = await new QuickInfoService(ClosedBridge(), gate: Postgres).GetAsync(new QuickInfoRequest { CursorOffset = 3 }, CancellationToken.None);
        var signature = await new SignatureHelpService(ClosedBridge(), gate: Postgres).GetAsync(new SignatureRequest { CursorOffset = 3 }, CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(quick.Header) && string.IsNullOrEmpty(quick.Description));
        Assert.Empty(quick.Details);
        Assert.True(string.IsNullOrEmpty(signature.FunctionName));
        Assert.Empty(signature.Overloads ?? Array.Empty<SignatureOverload>());
    }

    // ── Refactorings ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lightweight_refactorings_are_unavailable_for_PostgreSQL_and_change_nothing()
    {
        var service = new RefactoringService(ClosedBridge(), new FormatterService(Postgres), Postgres);
        const string sql = "SELECT a;";

        var preview = await service.PreviewLightweightAsync(LightweightRefactorKind.RemoveSemicolons, sql);
        var applied = await service.ApplyLightweightAsync(LightweightRefactorKind.RemoveSemicolons, sql);

        Assert.False(preview.Changed);
        Assert.Equal(sql, preview.After);
        Assert.Contains(DialectMessages.NotAvailable, preview.Warnings);
        Assert.Equal(sql, applied);
        Assert.False(service.HeavyAvailable);
    }

    [Fact]
    public async Task Lightweight_refactorings_still_work_for_SQL_Server()
    {
        var service = new RefactoringService(ClosedBridge(), new FormatterService(SqlServer), SqlServer);

        var preview = await service.PreviewLightweightAsync(LightweightRefactorKind.RemoveSemicolons, "SELECT a;");

        Assert.True(preview.Changed);
    }

    [Fact]
    public void The_message_is_the_one_the_spec_names()
    {
        Assert.Equal("Not available for PostgreSQL", DialectMessages.NotAvailable);
    }
}

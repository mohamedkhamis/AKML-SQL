using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (task T146, T154, FR-140, FR-145). For a PostgreSQL connection the web asks the engine to format, sending the active style as
/// JSON and the session id; for SQL Server it formats in the browser and never calls the engine. With no engine, or an engine that does not
/// do PostgreSQL, the script comes back unchanged with the reason. The synchronous entry point still refuses PostgreSQL text.
/// </summary>
public sealed class FormatterServicePgRoutingTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    private sealed class SpyEngine : IPostgresFormatEngine
    {
        public readonly List<(string Sql, FormattingProfile Profile)> Formatted = new();
        public readonly List<(string Sample, FormattingProfile Profile)> Previewed = new();

        public Task<FormatResult> FormatAsync(string sql, FormattingProfile profile, CancellationToken ct)
        {
            Formatted.Add((sql, profile));
            return Task.FromResult(new FormatResult { Success = true, FormattedText = "FROM THE ENGINE", WasModified = true, ValidationPassed = true });
        }

        public Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct)
        {
            Previewed.Add((sample, profile));
            return Task.FromResult(new FormatResult { Success = true, FormattedText = "PREVIEW FROM THE ENGINE", ValidationPassed = true });
        }
    }

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

    private static FormattingProfile Profile() => new() { SqlPrompt = SqlPromptStyleDocument.CreateDefault("web style", "web-style").Root };

    // ── FormatterService ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task For_PostgreSQL_the_script_and_the_active_style_go_to_the_engine()
    {
        var engine = new SpyEngine();
        var service = new FormatterService(new Gate(DatabaseKind.PostgreSql), engine);
        var profile = Profile();

        var result = await service.FormatAsync("select a::int from t limit 1", profile);

        Assert.Equal("FROM THE ENGINE", result.FormattedText);
        var call = Assert.Single(engine.Formatted);
        Assert.Equal("select a::int from t limit 1", call.Sql);
        Assert.Same(profile, call.Profile);
    }

    [Fact]
    public async Task For_SQL_Server_the_script_is_formatted_in_the_browser_and_the_engine_is_never_asked()
    {
        var engine = new SpyEngine();
        var service = new FormatterService(new Gate(DatabaseKind.SqlServer), engine);

        var result = await service.FormatAsync("select  a   from  t");

        Assert.True(result.Success);
        Assert.NotEqual("FROM THE ENGINE", result.FormattedText);
        Assert.Empty(engine.Formatted);
        Assert.Equal(new FormatterService().Format("select  a   from  t").FormattedText, result.FormattedText);
    }

    [Fact]
    public async Task For_PostgreSQL_with_no_engine_to_ask_the_script_comes_back_unchanged_with_the_reason()
    {
        var service = new FormatterService(new Gate(DatabaseKind.PostgreSql), postgres: null);

        var result = await service.FormatAsync("select  a   from  t");

        Assert.Equal("select  a   from  t", result.FormattedText);
        Assert.False(result.WasModified);
        Assert.Contains("Not available for PostgreSQL", result.Diagnostics.Single().Message);
    }

    [Fact]
    public void The_synchronous_entry_point_still_refuses_PostgreSQL_text()
    {
        var service = new FormatterService(new Gate(DatabaseKind.PostgreSql), new SpyEngine());

        var result = service.Format("select  a   from  t");

        Assert.Equal("select  a   from  t", result.FormattedText);
    }

    [Fact]
    public async Task The_style_preview_goes_to_the_engine_for_PostgreSQL_only()
    {
        var engine = new SpyEngine();

        var postgres = await new FormatterService(new Gate(DatabaseKind.PostgreSql), engine).PreviewAsync("select 1", Profile());
        var sqlServer = await new FormatterService(new Gate(DatabaseKind.SqlServer), engine).PreviewAsync("select 1", Profile());

        Assert.Equal("PREVIEW FROM THE ENGINE", postgres.FormattedText);
        Assert.NotEqual("PREVIEW FROM THE ENGINE", sqlServer.FormattedText);
        Assert.Single(engine.Previewed);
    }

    [Fact]
    public async Task A_document_over_the_size_limit_is_refused_before_it_is_sent()
    {
        var engine = new SpyEngine();
        var service = new FormatterService(new Gate(DatabaseKind.PostgreSql), engine);
        var huge = new string('x', 10 * 1024 * 1024 + 1);

        await Assert.ThrowsAsync<DocumentTooLargeException>(() => service.FormatAsync(huge));

        Assert.Empty(engine.Formatted);
    }

    // ── EnginePostgresFormat ─────────────────────────────────────────────────────────────────────────

    private static EnginePostgresFormat Over(Bridge bridge) => new(bridge, new SessionOnly("session-42"));

    private sealed class SessionOnly : ISqlConnectionService
    {
        public SessionOnly(string id) => SessionId = id;
        public string SessionId { get; }
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

    [Fact]
    public async Task The_engine_gets_the_script_the_session_and_the_style_as_JSON_and_its_answer_is_read_back()
    {
        var bridge = new Bridge
        {
            Answer = (_, _) => new FormatResponse
            {
                Success = true, FormattedText = "SELECT 1;", WasModified = true, ValidationPassed = true, ElapsedMs = 7,
                Diagnostics = new[] { new FormatDiagnosticInfo { Severity = 1, Message = "a note", Offset = 3, Line = 2 } },
            },
        };

        var result = await Over(bridge).FormatAsync("select 1", Profile(), default);

        var sent = Assert.IsType<FormatRequest>(Assert.Single(bridge.Sent).Request);
        Assert.Equal(MessageTypes.FormatDocument, bridge.Sent[0].Type);
        Assert.Equal("session-42", sent.SessionId);
        Assert.Equal("select 1", sent.Text);
        Assert.Contains("sqlPrompt", sent.ProfileJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Success && result.WasModified);
        Assert.Equal("SELECT 1;", result.FormattedText);
        var note = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticSeverity)1, note.Severity);
        Assert.Equal(("a note", 3, 2), (note.Message, note.Offset, note.Line));
    }

    [Fact]
    public async Task A_closed_bridge_or_an_engine_without_PostgreSQL_leaves_the_script_unchanged_and_says_why()
    {
        var closed = new Bridge { State = BridgeState.Disconnected };
        var old = new Bridge { EngineCapabilities = Array.Empty<string>() };

        var a = await Over(closed).FormatAsync("select 1", Profile(), default);
        var b = await Over(old).FormatAsync("select 1", Profile(), default);

        Assert.False(a.Success);
        Assert.Equal("select 1", a.FormattedText);
        Assert.Equal(EnginePostgresFormat.NoEngine, a.Diagnostics.Single().Message);
        Assert.Equal(EnginePostgresFormat.OldEngine, b.Diagnostics.Single().Message);
        Assert.Empty(closed.Sent);
        Assert.Empty(old.Sent);
    }

    [Fact]
    public async Task The_preview_request_says_it_is_PostgreSQL_and_a_validation_error_comes_back_as_a_warning()
    {
        var bridge = new Bridge { Answer = (_, _) => new FormatPreviewResponse { FormattedText = "x", ValidationError = "could not format" } };

        var result = await Over(bridge).PreviewAsync("select 1", Profile(), default);

        var sent = Assert.IsType<FormatPreviewRequest>(Assert.Single(bridge.Sent).Request);
        Assert.Equal((int)DatabaseKind.PostgreSql, sent.Dialect);
        Assert.False(result.Success);
        Assert.Contains("could not format", result.Diagnostics.Single().Message);
    }

    [Fact]
    public async Task A_bridge_that_fails_is_reported_not_thrown()
    {
        var bridge = new Bridge { Answer = (_, _) => throw new InvalidOperationException("dropped") };

        var result = await Over(bridge).FormatAsync("select 1", Profile(), default);

        Assert.False(result.Success);
        Assert.Equal("select 1", result.FormattedText);
        Assert.Contains("dropped", result.Diagnostics.Single().Message);
    }
}

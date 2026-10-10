using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T082, research R22) — the browser's side of an execute: a reply deadline sized from the script
/// (its batches and <c>GO n</c> counts, split by the same parser the engine uses), told apart from the
/// user's own cancel; a dropped bridge reported as such; Parse sent only to an engine that understands it.
/// </summary>
public sealed class QueryExecutionServiceTests
{
    private sealed class ConnectedSql : ISqlConnectionService
    {
        public string SessionId => "session-1";
        public bool IsConnected => true;
        public string? Server => "(local)";
        public string? Database => "tempdb";
#pragma warning disable CS0067
        public event Action? StateChanged;
#pragma warning restore CS0067
        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => Task.FromResult((true, (string?)null));
        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => Task.FromResult((true, (string?)null));
        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>(), (string?)null));
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>An open bridge whose reply is scripted: answer, never answer, or drop.</summary>
    private sealed class ScriptedBridge : IEngineBridge
    {
        public Func<int, object, CancellationToken, Task<object?>> Reply { get; set; } =
            (_, _, _) => Task.FromResult<object?>(new ExecuteQueryResult { Status = ExecuteStatus.Ok });

        public List<int> SentTypes { get; } = new();

        public BridgeState State => BridgeState.Open;
        public string[] EngineCapabilities { get; set; } = Array.Empty<string>();
        public string? EngineVersion => "test";
#pragma warning disable CS0067
        public event Action<BridgeState>? StateChanged;
        public event Action<DateTimeOffset?>? RetryScheduled;
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected;
#pragma warning restore CS0067

        public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
            Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });

        public async Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
            where TRequest : class
            where TResponse : class
        {
            SentTypes.Add(requestMessageType);
            return (TResponse)(await Reply(requestMessageType, request, ct))!;
        }

        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static QueryExecutionService Service(ScriptedBridge bridge) =>
        new(bridge, new ConnectedSql(), new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    private static async Task<object?> NeverReply(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return null;
    }

    [Fact]
    public async Task An_engine_that_never_answers_gives_NoReply_after_the_deadline()
    {
        var bridge = new ScriptedBridge { Reply = (_, _, ct) => NeverReply(ct) };
        var service = Service(bridge);
        service.DeadlineSlack = TimeSpan.FromMilliseconds(100);

        var result = await service.ExecuteAsync("SELECT 1;", 100, 1, "q1", CancellationToken.None);

        Assert.Equal(ExecuteStatus.NoReply, result.Status);
        Assert.Equal(QueryExecutionService.NoReplyMessage, result.ErrorMessage);
        Assert.Equal("q1", result.QueryId);
    }

    [Fact]
    public async Task The_users_own_cancel_is_Cancelled_not_NoReply()
    {
        var bridge = new ScriptedBridge { Reply = (_, _, ct) => NeverReply(ct) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await Service(bridge).ExecuteAsync("SELECT 1;", 100, 30, "q2", cts.Token);

        Assert.Equal(ExecuteStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task A_dropped_bridge_gives_Disconnected()
    {
        var bridge = new ScriptedBridge { Reply = (_, _, _) => throw new InvalidOperationException("socket closed") };

        var result = await Service(bridge).ExecuteAsync("SELECT 1;", 100, 30, "q3", CancellationToken.None);

        Assert.Equal(ExecuteStatus.Disconnected, result.Status);
        Assert.Equal(QueryExecutionService.DisconnectedMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("SELECT 1;", 1)]
    [InlineData("SELECT 1;\nGO\nSELECT 2;\nGO\nSELECT 3;", 3)]
    [InlineData("SELECT 1;\nGO 5\nSELECT 2;", 6)]
    [InlineData("SELECT 'GO';", 1)]
    [InlineData("SELECT 1;\nGO SELECT 2", 1)]   // refused by the engine: one reply
    [InlineData("", 1)]
    public void The_batch_count_includes_repeat_counts(string sql, int expected)
    {
        Assert.Equal(expected, Service(new ScriptedBridge()).CountBatchRuns(sql));
    }

    [Fact]
    public void The_deadline_is_the_timeout_per_command_plus_slack()
    {
        var service = Service(new ScriptedBridge());

        Assert.Equal(TimeSpan.FromSeconds(30 * 3) + QueryExecutionService.DefaultDeadlineSlack,
            service.ReplyDeadline("SELECT 1;\nGO 3", 30));
    }

    [Fact]
    public async Task Parse_is_sent_only_to_an_engine_that_advertises_execute_v2()
    {
        var older = new ScriptedBridge();
        var refused = await Service(older).ParseAsync("SELECT 1;", 30, "p1", CancellationToken.None);
        Assert.Equal(ExecuteStatus.Error, refused.Status);
        Assert.Equal(QueryExecutionService.ParseUnavailable, refused.ErrorMessage);
        Assert.Empty(older.SentTypes);

        var newer = new ScriptedBridge { EngineCapabilities = new[] { QueryExecutionService.CapabilityExecuteV2 } };
        var parsed = await Service(newer).ParseAsync("SELECT 1;", 30, "p2", CancellationToken.None);
        Assert.Equal(ExecuteStatus.Ok, parsed.Status);
        Assert.Equal(new[] { MessageTypes.ExecuteParse }, newer.SentTypes);
    }

    [Fact]
    public async Task Execute_sends_212()
    {
        var bridge = new ScriptedBridge();

        await Service(bridge).ExecuteAsync("SELECT 1;", 100, 30, "q4", CancellationToken.None);

        Assert.Equal(new[] { MessageTypes.ExecuteQuery }, bridge.SentTypes);
    }

    [Theory]
    [InlineData(ExecuteStatus.NoReply)]
    [InlineData(ExecuteStatus.Disconnected)]
    public void History_records_an_unanswered_or_dropped_run_as_an_error(int status)
    {
        // The statement may have run: the history keeps it, marked as an error.
        Assert.True(WebHistoryLogic.ShouldRecord(status));
        Assert.Equal(1, WebHistoryLogic.MapStatus(status));
    }
}

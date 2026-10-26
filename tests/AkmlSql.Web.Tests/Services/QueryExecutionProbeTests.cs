using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 043 (review of US1, gap G1): before a query tab closes, the app asks whether its session holds an open transaction. A
/// SQL Server session is asked of the server then (XACT_STATE: 1 open, -1 aborted, 0 none) — asking after every run would reset
/// @@ROWCOUNT and @@ERROR for the user's next batch; a PostgreSQL session answers from what every reply already said.
/// </summary>
public sealed class QueryExecutionProbeTests
{
    private sealed class Connections : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected => true;
        public string? Server => "localhost";
        public string? Database => "master";
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

    private sealed class Bridge : IEngineBridge
    {
        public ExecuteQueryResult Reply { get; set; } = new() { Status = ExecuteStatus.Ok };
        public List<ExecuteQueryRequest> Requests { get; } = new();
        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities => Array.Empty<string>();
        public string? EngineVersion => "test";
#pragma warning disable CS0067
        public event Action<BridgeState>? StateChanged;
        public event Action<DateTimeOffset?>? RetryScheduled;
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected;
#pragma warning restore CS0067
        public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
            Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });
        public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
            where TRequest : class where TResponse : class
        {
            Requests.Add((ExecuteQueryRequest)(object)request);
            return Task.FromResult((TResponse)(object)Reply);
        }
        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static QueryExecutionService Service(Bridge bridge) =>
        new(bridge, new Connections(), new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    private static SqlSessionState Tab(DatabaseKind kind = DatabaseKind.SqlServer) =>
        new("tab-session") { IsConnected = true, Kind = kind, Server = "localhost", Database = "master" };

    private static ExecuteQueryResult Answer(string? cell) => new()
    {
        Status = ExecuteStatus.Ok,
        ResultSets = new[] { new ExecuteResultSet { ColumnNames = new[] { "xact_state" }, Rows = new[] { new[] { cell } } } },
    };

    [Theory]
    [InlineData("1", TransactionState.Open)]
    [InlineData("-1", TransactionState.Aborted)]
    [InlineData("0", TransactionState.None)]
    public async Task A_SQL_Server_session_is_asked_of_the_server_on_its_own_session(string cell, TransactionState expected)
    {
        var bridge = new Bridge { Reply = Answer(cell) };

        var state = await Service(bridge).ProbeTransactionStateAsync(Tab(), CancellationToken.None);

        Assert.Equal(expected, state);
        var request = Assert.Single(bridge.Requests);
        Assert.Equal("tab-session", request.SessionId);
        Assert.Equal(QueryExecutionService.TransactionProbeSql, request.Sql);
    }

    [Fact]
    public async Task An_answer_it_cannot_read_says_nothing()
    {
        Assert.Null(await Service(new Bridge { Reply = Answer("x") }).ProbeTransactionStateAsync(Tab(), CancellationToken.None));
        Assert.Null(await Service(new Bridge { Reply = new ExecuteQueryResult { Status = ExecuteStatus.Error, ErrorMessage = "no" } })
            .ProbeTransactionStateAsync(Tab(), CancellationToken.None));
    }

    [Fact]
    public async Task A_PostgreSQL_session_answers_from_its_own_state_without_asking()
    {
        var bridge = new Bridge();
        var tab = Tab(DatabaseKind.PostgreSql);
        tab.TransactionState = TransactionState.Aborted;

        Assert.Equal(TransactionState.Aborted, await Service(bridge).ProbeTransactionStateAsync(tab, CancellationToken.None));
        Assert.Empty(bridge.Requests);
    }

    [Fact]
    public async Task No_connection_or_no_engine_says_nothing_and_asks_nothing()
    {
        var bridge = new Bridge { Reply = Answer("1") };
        var closed = Tab();
        closed.IsConnected = false;
        Assert.Null(await Service(bridge).ProbeTransactionStateAsync(closed, CancellationToken.None));

        bridge.State = BridgeState.Disconnected;
        Assert.Null(await Service(bridge).ProbeTransactionStateAsync(Tab(), CancellationToken.None));
        Assert.Empty(bridge.Requests);
    }
}

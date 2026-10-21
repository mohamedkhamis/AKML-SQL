using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (task T067, FR-010, FR-016, FR-017): a PostgreSQL script goes to the engine untouched, the reply deadline is sized
/// from its statements (it has no GO), and the transaction state the engine reports reaches the connection service.
/// </summary>
public sealed class QueryExecutionServicePgTests
{
    private sealed class Session : ISqlConnectionService
    {
        public string SessionId => "session-1";
        public bool IsConnected { get; set; } = true;
        public string? Server => "db1:5432";
        public string? Database => "northwind";
        public DatabaseKind Kind { get; set; } = DatabaseKind.PostgreSql;
        public TransactionState TransactionState { get; private set; }
        public List<TransactionState> Reported { get; } = new();
#pragma warning disable CS0067
        public event Action? StateChanged;
#pragma warning restore CS0067
        public void ReportTransactionState(TransactionState state)
        {
            Reported.Add(state);
            TransactionState = state;
        }

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

        public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
            where TRequest : class
            where TResponse : class
        {
            Requests.Add((ExecuteQueryRequest)(object)request);
            return Task.FromResult((TResponse)(object)Reply);
        }

        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static QueryExecutionService Service(Bridge bridge, Session session) =>
        new(bridge, session, new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    [Fact]
    public async Task The_script_is_sent_untouched_including_a_GO_line()
    {
        var bridge = new Bridge();
        var script = "SELECT 1;\nGO\nSELECT 2;";

        await Service(bridge, new Session()).ExecuteAsync(script, 100, 30, "q1", CancellationToken.None);

        Assert.Equal(script, Assert.Single(bridge.Requests).Sql);
    }

    [Theory]
    [InlineData("SELECT 1", 1)]
    [InlineData("SELECT 1;", 2)]
    [InlineData("SELECT 1; SELECT 2; SELECT 3;", 4)]
    [InlineData("", 1)]
    public void A_PostgreSQL_script_is_counted_by_its_semicolons_never_fewer_than_its_statements(string sql, int expected)
    {
        var service = Service(new Bridge(), new Session());

        Assert.Equal(expected, service.CountBatchRuns(sql));
    }

    [Fact]
    public void A_SQL_Server_script_is_still_counted_by_its_GO_batches()
    {
        var service = Service(new Bridge(), new Session { Kind = DatabaseKind.SqlServer });

        Assert.Equal(1, service.CountBatchRuns("SELECT 1; SELECT 2; SELECT 3;"));
        Assert.Equal(3, service.CountBatchRuns("SELECT 1\nGO\nSELECT 2\nGO\nSELECT 3"));
    }

    [Fact]
    public async Task The_transaction_state_in_the_reply_is_reported_to_the_connection()
    {
        var bridge = new Bridge { Reply = new ExecuteQueryResult { Status = ExecuteStatus.Error, TransactionState = (int)TransactionState.Aborted } };
        var session = new Session();

        await Service(bridge, session).ExecuteAsync("BEGIN; SELECT 1/0;", 100, 30, "q2", CancellationToken.None);

        Assert.Equal(new[] { TransactionState.Aborted }, session.Reported);
    }

    [Fact]
    public async Task A_SQL_Server_reply_never_reports_a_transaction_state()
    {
        var session = new Session { Kind = DatabaseKind.SqlServer };

        await Service(new Bridge(), session).ExecuteAsync("SELECT 1", 100, 30, "q3", CancellationToken.None);

        Assert.Empty(session.Reported);
    }

    [Fact]
    public async Task Without_a_connection_the_message_does_not_name_SQL_Server()
    {
        var result = await Service(new Bridge(), new Session { IsConnected = false }).ExecuteAsync("SELECT 1", 100, 30, "q4", CancellationToken.None);

        Assert.Equal(ExecuteStatus.NoConnection, result.Status);
        Assert.DoesNotContain("SQL Server", result.ErrorMessage, StringComparison.Ordinal);
    }
}

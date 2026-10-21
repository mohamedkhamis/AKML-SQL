using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T048, FR-005, FR-016): the status bar says which kind of server the session is on, with an icon and a
/// word, and shows an open or aborted PostgreSQL transaction with a one-click rollback that sends <c>ROLLBACK;</c>.
/// </summary>
public sealed class StatusBarKindTests : BunitContext
{
    private sealed class Session : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected { get; set; }
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? Login { get; set; }
        public DatabaseKind Kind { get; set; } = DatabaseKind.SqlServer;
        public string? ServerVersionText { get; set; }
        public TransactionState TransactionState { get; set; }
        public event Action? StateChanged;

        public void Raise() => StateChanged?.Invoke();

        public void ReportTransactionState(TransactionState state)
        {
            if (!IsConnected || TransactionState == state) return;
            TransactionState = state;
            StateChanged?.Invoke();
        }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, Array.Empty<string>(), null));

        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RollbackExec : IQueryExecutionService
    {
        private readonly Session _session;
        public RollbackExec(Session session) => _session = session;

        public List<string> Ran { get; } = new();
        public ExecuteQueryResult Reply { get; set; } = new() { Status = ExecuteStatus.Ok, TransactionState = (int)TransactionState.None };

        public Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
        {
            Ran.Add(sql);
            _session.ReportTransactionState((TransactionState)Reply.TransactionState);
            return Task.FromResult(Reply);
        }

        public Task CancelAsync(string queryId, CancellationToken ct) => Task.CompletedTask;
        public Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct) => Task.FromResult(new ApplyChangesResult());
    }

    private readonly Session _session = new();
    private readonly RollbackExec _exec;
    private readonly FakeNotificationService _notifications = new();

    public StatusBarKindTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _exec = new RollbackExec(_session);
        var bridge = new FakeEngineBridge();
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IConnectionStore>(new ConnectionStore(adapter));
        Services.AddSingleton<ISqlConnectionService>(_session);
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(new ConnectionManagerController());
        Services.AddSingleton<IEngineAutoConnect>(new NoFailureAutoConnect());
        Services.AddSingleton<IWorkspaceStatus>(new WorkspaceStatus(() => DateTimeOffset.UnixEpoch));
        Services.AddSingleton<IQueryExecutionService>(_exec);
        Services.AddSingleton<INotificationService>(_notifications);
    }

    private void ConnectPostgres(TransactionState state = TransactionState.None)
    {
        _session.IsConnected = true;
        _session.Kind = DatabaseKind.PostgreSql;
        _session.Server = "db1:5432";
        _session.Database = "northwind";
        _session.Login = "app";
        _session.ServerVersionText = "18.1";
        _session.TransactionState = state;
    }

    // ── The kind (FR-005) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Before_a_connection_there_is_no_kind_and_no_transaction()
    {
        var cut = Render<StatusBar>();

        Assert.Empty(cut.FindAll("[data-testid=status-kind]"));
        Assert.Empty(cut.FindAll("[data-testid=status-txn]"));
    }

    [Fact]
    public void A_SQL_Server_session_is_labelled_SQL_Server_with_its_own_login_title()
    {
        _session.IsConnected = true;
        _session.Server = "localhost";
        _session.Database = "master";
        _session.Login = "sa";

        var cut = Render<StatusBar>();

        var kind = cut.Find("[data-testid=status-kind]");
        Assert.Equal("SQL Server", kind.TextContent.Trim());
        Assert.Equal("SQL Server", kind.GetAttribute("title"));
        Assert.Contains("#akml-icon-plug", kind.InnerHtml);
        Assert.Equal("SQL Server login", cut.Find("[data-testid=status-login]").GetAttribute("title"));
        Assert.Empty(cut.FindAll("[data-testid=status-txn]"));
    }

    [Fact]
    public void A_PostgreSQL_session_is_labelled_PostgreSQL_with_the_version_on_hover()
    {
        ConnectPostgres();

        var cut = Render<StatusBar>();

        var kind = cut.Find("[data-testid=status-kind]");
        Assert.Equal("PostgreSQL", kind.TextContent.Trim());
        Assert.Equal("PostgreSQL 18.1", kind.GetAttribute("title"));
        Assert.Contains("#akml-icon-database", kind.InnerHtml);
        Assert.Equal("Login", cut.Find("[data-testid=status-login]").GetAttribute("title"));
        Assert.Contains("db1:5432/northwind", cut.Find("[data-testid=status-connection]").TextContent);
    }

    [Fact]
    public void Switching_kind_updates_the_segment()
    {
        ConnectPostgres();
        var cut = Render<StatusBar>();
        Assert.Equal("PostgreSQL", cut.Find("[data-testid=status-kind]").TextContent.Trim());

        _session.Kind = DatabaseKind.SqlServer;
        _session.ServerVersionText = null;
        cut.InvokeAsync(_session.Raise);

        cut.WaitForAssertion(() => Assert.Equal("SQL Server", cut.Find("[data-testid=status-kind]").TextContent.Trim()));
    }

    // ── The transaction (FR-016) ────────────────────────────────────────────────────────────────

    [Fact]
    public void An_open_transaction_is_shown_with_a_rollback_and_no_failure_text()
    {
        ConnectPostgres(TransactionState.Open);

        var cut = Render<StatusBar>();

        Assert.Contains("Transaction open", cut.Find("[data-testid=status-txn]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=status-txn-rollback]"));
        Assert.Empty(cut.FindAll("[data-testid=status-txn-note]"));
        Assert.DoesNotContain("akml-status-txn--aborted", cut.Find("[data-testid=status-txn]").ClassName);
    }

    [Fact]
    public void An_aborted_transaction_says_every_statement_fails_until_rollback()
    {
        ConnectPostgres(TransactionState.Aborted);

        var cut = Render<StatusBar>();

        var segment = cut.Find("[data-testid=status-txn]");
        Assert.Contains("Transaction aborted", segment.TextContent);
        Assert.Equal("Every statement fails until ROLLBACK.", cut.Find("[data-testid=status-txn-note]").TextContent.Trim());
        Assert.Contains("akml-status-txn--aborted", segment.ClassName);
        Assert.Contains("#akml-icon-triangle-alert", segment.InnerHtml);   // the state is an icon and words, not colour alone
        Assert.Equal("status", segment.GetAttribute("role"));
    }

    [Fact]
    public void Rollback_sends_ROLLBACK_and_the_segment_goes_away_when_the_engine_says_none()
    {
        ConnectPostgres(TransactionState.Aborted);
        var cut = Render<StatusBar>();

        cut.Find("[data-testid=status-txn-rollback]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=status-txn]")));
        Assert.Equal(new[] { "ROLLBACK;" }, _exec.Ran);
        Assert.Equal(TransactionState.None, _session.TransactionState);
    }

    [Fact]
    public void A_rollback_that_fails_is_reported_and_the_segment_stays()
    {
        ConnectPostgres(TransactionState.Aborted);
        _exec.Reply = new ExecuteQueryResult
        {
            Status = ExecuteStatus.Error, ErrorMessage = "the connection is gone", TransactionState = (int)TransactionState.Aborted,
        };
        var cut = Render<StatusBar>();

        cut.Find("[data-testid=status-txn-rollback]").Click();

        cut.WaitForAssertion(() => Assert.Single(_notifications.Sent));
        Assert.Contains("Rollback failed: the connection is gone", _notifications.Sent.Single().Text);
        Assert.NotEmpty(cut.FindAll("[data-testid=status-txn]"));
    }

    [Fact]
    public void A_state_report_from_a_run_updates_the_bar_without_a_reload()
    {
        ConnectPostgres();
        var cut = Render<StatusBar>();
        Assert.Empty(cut.FindAll("[data-testid=status-txn]"));

        cut.InvokeAsync(() => _session.ReportTransactionState(TransactionState.Open));
        cut.WaitForAssertion(() => Assert.Contains("Transaction open", cut.Find("[data-testid=status-txn]").TextContent));

        cut.InvokeAsync(() => _session.ReportTransactionState(TransactionState.Aborted));
        cut.WaitForAssertion(() => Assert.Contains("Transaction aborted", cut.Find("[data-testid=status-txn]").TextContent));
    }
}

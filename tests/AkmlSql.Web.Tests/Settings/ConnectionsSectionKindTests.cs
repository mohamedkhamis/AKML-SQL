using AkmlSql.Core;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 042 (task T112, FR-106, SC-018). Settings › Connections names the kind of every saved connection, writes a PostgreSQL
/// server the way the status bar does, and can make the engine forget a remembered password: on request, and always before
/// a connection that had one is deleted. A list with no PostgreSQL in it, on an engine without PostgreSQL, looks as it did.
/// </summary>
public sealed class ConnectionsSectionKindTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task AddPgAsync(string id, bool remember, string name = "Local PG") =>
        _host.Stores.SavedSql.AddAsync(new SavedSqlConnection
        {
            Id = id, Name = name, Kind = DatabaseKind.PostgreSql, Server = "pg1", Port = 5433, Database = "northwind",
            WindowsAuth = false, Login = "alice", RememberPassword = remember,
        });

    private Task AddSqlAsync(string id) =>
        _host.Stores.SavedSql.AddAsync(new SavedSqlConnection { Id = id, Name = "Local SQL", Server = "localhost", Database = "master", WindowsAuth = true });

    private void EngineWithPostgres()
    {
        _host.Sql.EngineSupportsPostgres = true;
        _host.Bridge.SetState(BridgeState.Open);
    }

    // ---- the kind of every connection ----------------------------------------------------------------------------

    [Fact]
    public async Task Every_saved_connection_shows_its_kind_and_a_PostgreSQL_server_shows_host_port_and_login()
    {
        await AddSqlAsync("s1");
        await AddPgAsync("p1", remember: false);
        EngineWithPostgres();

        var cut = _host.Open("settings/connections");

        var sqlRow = cut.WaitForElement("[data-testid=saved-connection-s1]");
        var pgRow = cut.Find("[data-testid=saved-connection-p1]");
        Assert.Equal("SQL Server", sqlRow.QuerySelector("[data-testid=settings-conn-kind]")!.TextContent.Trim());
        Assert.Equal("PostgreSQL", pgRow.QuerySelector("[data-testid=settings-conn-kind]")!.TextContent.Trim());
        Assert.Contains("pg1:5433 / northwind", pgRow.TextContent);
        Assert.Contains("alice", pgRow.TextContent);
        Assert.DoesNotContain("SQL Server login", pgRow.TextContent);
    }

    [Fact]
    public async Task A_list_with_only_SQL_Server_connections_on_an_engine_without_PostgreSQL_shows_no_kind()
    {
        await AddSqlAsync("s1");

        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-s1]");
        Assert.Empty(cut.FindAll("[data-testid=settings-conn-kind]"));
        Assert.Empty(cut.FindAll("[data-testid^=settings-conn-forget]"));
    }

    [Fact]
    public async Task A_PostgreSQL_connection_whose_engine_lacks_PostgreSQL_support_is_still_listed_with_its_kind()
    {
        await AddPgAsync("p1", remember: false);

        var cut = _host.Open("settings/connections");

        var row = cut.WaitForElement("[data-testid=saved-connection-p1]");
        Assert.Equal("PostgreSQL", row.QuerySelector("[data-testid=settings-conn-kind]")!.TextContent.Trim());
    }

    // ---- review of US1 (finding 36, data-model §3): a tab's connection does not change under its run -------------------

    [Fact]
    public async Task While_the_active_tab_runs_Connect_and_Disconnect_are_disabled_and_say_why()
    {
        await AddSqlAsync("s1");
        _host.Bridge.SetState(BridgeState.Open);
        _host.Sql.IsConnected = true;
        _host.Sql.Server = "localhost";
        _host.Sql.Database = "Sales";
        _host.Sql.ActiveSession = new SqlSessionState { IsBusy = true };

        var cut = _host.Open("settings/connections");

        var connect = cut.WaitForElement("[data-testid=settings-conn-connect-s1]");
        Assert.True(connect.HasAttribute("disabled"));
        Assert.Equal("Wait for the current run to finish", connect.GetAttribute("title"));
        var disconnect = cut.Find("[data-testid=settings-sql-disconnect]");
        Assert.True(disconnect.HasAttribute("disabled"));
        Assert.Equal("Wait for the current run to finish", disconnect.GetAttribute("title"));
    }

    /// <summary>The close-time question, held open by the test.</summary>
    private sealed class SlowProbe : IQueryExecutionService
    {
        public TaskCompletionSource<TransactionState?> Answer { get; } = new();
        public Task<TransactionState?> ProbeTransactionStateAsync(SqlSessionState session, CancellationToken ct) => Answer.Task;
        public Task<AkmlSql.Core.Ipc.Messages.ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct) => throw new NotSupportedException();
        public Task CancelAsync(string queryId, CancellationToken ct) => Task.CompletedTask;
        public Task<AkmlSql.Core.Ipc.Messages.ApplyChangesResult> ApplyAsync(AkmlSql.Core.Ipc.Messages.ApplyChangesRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Disconnect_acts_on_the_tab_it_was_asked_for_even_when_another_is_shown_by_the_time_it_goes_ahead()
    {
        var probe = new SlowProbe();
        _host.Ctx.Services.AddSingleton<IQueryExecutionService>(probe);
        _host.Bridge.SetState(BridgeState.Open);
        _host.Sql.IsConnected = true;
        _host.Sql.Server = "localhost";
        _host.Sql.Database = "Sales";
        var asked = new SqlSessionState("tab-a") { IsConnected = true, Server = "localhost", Database = "Sales" };
        _host.Sql.ActiveSession = asked;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=settings-sql-disconnect]").Click();
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=settings-sql-disconnect]").HasAttribute("disabled")));   // checking
        _host.Sql.ActiveSession = new SqlSessionState("tab-b") { IsConnected = true, Server = "other", Database = "Ops" };   // another tab shown
        await cut.InvokeAsync(() => probe.Answer.SetResult(TransactionState.None));

        cut.WaitForAssertion(() => Assert.Same(asked, Assert.Single(_host.Sql.DisconnectedSessions)));
    }

    [Fact]
    public async Task With_no_run_in_flight_Connect_and_Disconnect_are_enabled()
    {
        await AddSqlAsync("s1");
        _host.Bridge.SetState(BridgeState.Open);
        _host.Sql.IsConnected = true;
        _host.Sql.Server = "localhost";
        _host.Sql.Database = "Sales";
        _host.Sql.ActiveSession = new SqlSessionState();

        var cut = _host.Open("settings/connections");

        Assert.False(cut.WaitForElement("[data-testid=settings-conn-connect-s1]").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid=settings-sql-disconnect]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Connect_for_a_PostgreSQL_connection_opens_the_connection_manager_on_PostgreSQL()
    {
        await AddPgAsync("p1", remember: false);
        EngineWithPostgres();
        var cut = _host.Open("settings/connections");
        var opened = 0;
        _host.ConnManager.OpenRequested += () => opened++;

        cut.WaitForElement("[data-testid=settings-conn-connect-p1]").Click();

        Assert.Equal(1, opened);
        Assert.Equal(DatabaseKind.PostgreSql, _host.ConnManager.RequestedKind);
    }

    [Fact]
    public async Task No_password_is_asked_for_or_shown_in_the_list()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();

        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-p1]");
        Assert.Empty(cut.FindAll("input[type=password]"));
    }

    // ---- Forget password -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Forget_password_is_offered_only_for_a_connection_whose_password_is_remembered()
    {
        await AddPgAsync("p1", remember: true);
        await AddPgAsync("p2", remember: false, name: "Other PG");
        EngineWithPostgres();

        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-p1]");
        Assert.NotEmpty(cut.FindAll("[data-testid=settings-conn-forget-p1]"));
        Assert.Empty(cut.FindAll("[data-testid=settings-conn-forget-p2]"));
    }

    [Fact]
    public async Task Forget_password_asks_the_engine_for_this_server_and_login_and_clears_the_remember_flag()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=settings-conn-forget-p1]").Click();

        cut.WaitForAssertion(() => Assert.Single(_host.Sql.Forgets));
        Assert.Equal(("pg1", (int?)5433, "alice"), _host.Sql.Forgets[0]);
        cut.WaitForAssertion(() => Assert.False(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()!.RememberPassword));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=settings-conn-forget-p1]")));
        Assert.Contains(_host.Notifications.Sent, n => n.Text.Contains("alice") && n.Text.Contains("forgot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Forget_password_with_no_engine_says_so_and_keeps_the_flag()
    {
        await AddPgAsync("p1", remember: true);
        _host.Sql.EngineSupportsPostgres = false;
        _host.Bridge.SetState(BridgeState.Disconnected);
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=settings-conn-forget-p1]").Click();

        cut.WaitForAssertion(() => Assert.Contains(_host.Notifications.Sent, n => n.Kind == NotificationKind.Error));
        Assert.Empty(_host.Sql.Forgets);
        Assert.True((await _host.Stores.SavedSql.GetAsync("p1"))!.RememberPassword);
    }

    // Spec 043 follow-up (review): a forget the engine did not answer is not reported as done.
    [Fact]
    public async Task Forget_password_the_engine_does_not_answer_says_so_and_keeps_the_flag()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        _host.Sql.ForgetAnswer = null;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=settings-conn-forget-p1]").Click();

        cut.WaitForAssertion(() => Assert.Contains(_host.Notifications.Sent, n => n.Kind == NotificationKind.Error && n.Text.Contains("did not confirm that it forgot the password for alice on pg1:5433")));
        Assert.Single(_host.Sql.Forgets);
        Assert.True((await _host.Stores.SavedSql.GetAsync("p1"))!.RememberPassword);
        Assert.DoesNotContain(_host.Notifications.Sent, n => n.Kind == NotificationKind.Success);
        Assert.NotEmpty(cut.FindAll("[data-testid=settings-conn-forget-p1]"));
    }

    [Fact]
    public async Task Deleting_when_the_engine_does_not_answer_the_forget_deletes_and_says_the_password_stays()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        _host.Sql.ForgetAnswer = null;
        _host.Dialogs.ConfirmAnswer = _ => true;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()));
        var notice = Assert.Single(_host.Notifications.Sent);
        Assert.Equal(NotificationKind.Warning, notice.Kind);
        Assert.Contains("may still be remembered on the engine machine", notice.Text);
    }

    // ---- Delete forgets first ------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_connection_with_a_remembered_password_forgets_the_password_first()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        var stillSavedWhenForgotten = false;
        _host.Sql.OnForget = async () => stillSavedWhenForgotten = await _host.Stores.SavedSql.GetAsync("p1") != null;
        _host.Dialogs.ConfirmAnswer = _ => true;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()));
        Assert.Single(_host.Sql.Forgets);
        Assert.True(stillSavedWhenForgotten, "the password must be forgotten while the record still exists");
        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.Contains("Local PG", confirm.Message);
        Assert.True(confirm.Destructive);
    }

    [Fact]
    public async Task Declining_the_delete_forgets_nothing_and_keeps_the_connection()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        _host.Dialogs.ConfirmAnswer = _ => false;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        Assert.Single(_host.Dialogs.Confirms);
        Assert.Empty(_host.Sql.Forgets);
        Assert.NotNull(await _host.Stores.SavedSql.GetAsync("p1"));
    }

    [Fact]
    public async Task Deleting_a_connection_without_a_remembered_password_does_not_touch_the_engine()
    {
        await AddPgAsync("p1", remember: false);
        EngineWithPostgres();
        _host.Dialogs.ConfirmAnswer = _ => true;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()));
        Assert.Empty(_host.Sql.Forgets);
    }

    [Fact]
    public async Task Deleting_when_the_engine_cannot_be_reached_says_the_password_stays_on_the_engine()
    {
        await AddPgAsync("p1", remember: true);
        _host.Sql.EngineSupportsPostgres = false;
        _host.Bridge.SetState(BridgeState.Disconnected);
        _host.Dialogs.ConfirmAnswer = _ => true;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.Contains(confirm.Details ?? Array.Empty<string>(), d => d.Contains("cannot be forgotten", StringComparison.OrdinalIgnoreCase));
        // Review, round 2: and once deleted, that is said again, as a warning rather than a plain success.
        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()));
        Assert.Equal(NotificationKind.Warning, Assert.Single(_host.Notifications.Sent).Kind);
    }

    // ---- Spec 043 follow-up (review, round 2) ---------------------------------------------------------------------

    [Fact]
    public async Task Deleting_after_the_engine_went_away_while_the_question_was_open_says_the_password_may_still_be_remembered()
    {
        await AddPgAsync("p1", remember: true);
        EngineWithPostgres();
        // The engine was there when the question was asked (it said the password is forgotten too), and gone once it was answered.
        _host.Dialogs.ConfirmAnswer = _ => { _host.Bridge.SetState(BridgeState.Disconnected); return true; };
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-p1]").Click();

        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.Contains(confirm.Details ?? Array.Empty<string>(), d => d.Contains("is forgotten too", StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("p1").GetAwaiter().GetResult()));
        Assert.Empty(_host.Sql.Forgets);   // nothing to ask any more
        var notice = Assert.Single(_host.Notifications.Sent);
        Assert.Equal(NotificationKind.Warning, notice.Kind);
        Assert.Contains("did not confirm that it forgot the password", notice.Text);
    }

    private Task AddSqlLoginAsync(string id, bool remember) =>
        _host.Stores.SavedSql.AddAsync(new SavedSqlConnection
        {
            Id = id, Name = "Remote SQL", Server = "sql01.example.com", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = remember,
        });

    private void EngineRemembersSqlLogins()
    {
        _host.Sql.EngineRemembersSqlServerPasswords = true;
        _host.Bridge.SetState(BridgeState.Open);
    }

    [Fact]
    public async Task A_SQL_login_remembered_on_the_engine_can_have_its_password_forgotten_here()
    {
        await AddSqlLoginAsync("s1", remember: true);
        await AddSqlLoginAsync("s2", remember: false);
        EngineRemembersSqlLogins();
        var cut = _host.Open("settings/connections");

        var row = cut.WaitForElement("[data-testid=saved-connection-s1]");
        Assert.Contains("password remembered on the engine", row.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=settings-conn-forget-s2]"));
        cut.Find("[data-testid=settings-conn-forget-s1]").Click();

        cut.WaitForAssertion(() => Assert.Equal(("sql01.example.com", "app"), Assert.Single(_host.Sql.SqlForgets)));
        Assert.Empty(_host.Sql.Forgets);   // not asked as a PostgreSQL password
        cut.WaitForAssertion(() => Assert.False(_host.Stores.SavedSql.GetAsync("s1").GetAwaiter().GetResult()!.RememberPassword));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=settings-conn-forget-s1]")));
        Assert.Contains(_host.Notifications.Sent, n => n.Kind == NotificationKind.Success && n.Text.Contains("app"));
    }

    [Fact]
    public async Task A_SQL_login_forget_the_engine_does_not_confirm_keeps_the_flag_and_says_so()
    {
        await AddSqlLoginAsync("s1", remember: true);
        EngineRemembersSqlLogins();
        _host.Sql.ForgetAnswer = null;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=settings-conn-forget-s1]").Click();

        cut.WaitForAssertion(() => Assert.Contains(_host.Notifications.Sent,
            n => n.Kind == NotificationKind.Error && n.Text.Contains("did not confirm that it forgot the password for app on sql01.example.com")));
        Assert.True((await _host.Stores.SavedSql.GetAsync("s1"))!.RememberPassword);
    }

    [Fact]
    public async Task On_an_engine_that_cannot_remember_SQL_logins_no_Forget_password_is_offered_for_them()
    {
        await AddSqlLoginAsync("s1", remember: true);
        _host.Bridge.SetState(BridgeState.Open);

        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-s1]");
        Assert.Empty(cut.FindAll("[data-testid=settings-conn-forget-s1]"));
    }

    [Fact]
    public async Task Deleting_a_SQL_login_with_a_remembered_password_forgets_the_password_first()
    {
        await AddSqlLoginAsync("s1", remember: true);
        EngineRemembersSqlLogins();
        var stillSavedWhenForgotten = false;
        _host.Sql.OnForget = async () => stillSavedWhenForgotten = await _host.Stores.SavedSql.GetAsync("s1") != null;
        _host.Dialogs.ConfirmAnswer = _ => true;
        var cut = _host.Open("settings/connections");

        cut.WaitForElement("[data-testid=saved-connection-delete-s1]").Click();

        cut.WaitForAssertion(() => Assert.Null(_host.Stores.SavedSql.GetAsync("s1").GetAwaiter().GetResult()));
        Assert.Equal(("sql01.example.com", "app"), Assert.Single(_host.Sql.SqlForgets));
        Assert.True(stillSavedWhenForgotten, "the password must be forgotten while the record still exists");
        Assert.Contains(Assert.Single(_host.Dialogs.Confirms).Details ?? Array.Empty<string>(), d => d.Contains("is forgotten too", StringComparison.Ordinal));
        Assert.Equal(NotificationKind.Success, Assert.Single(_host.Notifications.Sent).Kind);
    }
}

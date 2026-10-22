using System.Text.Json;
using AkmlSql.Core;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T047, FR-001, FR-002, FR-006, FR-104 to FR-106): the connection dialog has a kind switch. PostgreSQL shows
/// its own field set, is unavailable (with the reason) on an engine that lacks it, tests with "PostgreSQL 18.1 on host:port",
/// saves a record with no password, and offers to forget a remembered password only when the engine holds one.
/// </summary>
public sealed class ConnectionManagerModalKindTests : BunitContext
{
    private sealed class FakeSql : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected { get; set; }
        public TransactionState TransactionState { get; set; }
        public string? Server { get; private set; }
        public string? Database { get; private set; }
        public event Action? StateChanged { add { } remove { } }

        public bool EngineSupportsPostgres { get; set; } = true;
        public SqlSessionState? ActiveSession { get; set; }
        public ConnectResult TestReply { get; set; } = new(true, null, false, "PostgreSQL 18.1");
        public ConnectResult ConnectReply { get; set; } = new(true, null, false, "PostgreSQL 18.1");
        public bool Remembered { get; set; }
        public List<ConnectionTarget> Tested { get; } = new();
        public List<ConnectionTarget> Connected { get; } = new();
        public int Forgets { get; private set; }

        public Task<ConnectResult> TestAsync(ConnectionTarget target, CancellationToken ct)
        {
            Tested.Add(target);
            return Task.FromResult(TestReply);
        }

        public Task<ConnectResult> ConnectAsync(ConnectionTarget target, CancellationToken ct) => Task.FromResult(Connect(target, ActiveSession));

        /// <summary>
        /// Review, round 4: the tab the dialog connects is marked as <see cref="SqlConnectionService"/> marks it — a password the
        /// engine had none of, or one it supplied that the server refused — BEFORE the dialog words its message, so the tests see
        /// the two together as the page does. A successful connect clears the mark, as there.
        /// </summary>
        public Task<ConnectResult> ConnectAsync(ConnectionTarget target, SqlSessionState session, CancellationToken ct) =>
            Task.FromResult(Connect(target, session));

        private ConnectResult Connect(ConnectionTarget target, SqlSessionState? session)
        {
            Connected.Add(target);
            var reply = ConnectReply;
            if (session != null) MarkAsTheServiceDoes(session, target, reply);
            return reply;
        }

        private static void MarkAsTheServiceDoes(SqlSessionState session, ConnectionTarget target, ConnectResult reply)
        {
            if (reply.Ok)
            {
                session.PasswordMissing = false;
                session.PasswordMissingFor = null;
                session.PasswordRefused = false;
                return;
            }
            bool nothingTyped = string.IsNullOrEmpty(target.Password);
            bool refusedSupplied = nothingTyped && (target.Kind == DatabaseKind.PostgreSql
                ? SqlConnectionService.IsPgPasswordRefused(reply)
                : !target.WindowsAuth && reply.ErrorCode == AkmlSql.Core.Ipc.Messages.DialectErrors.LoginFailed);
            if (!reply.PasswordRequired && !refusedSupplied) return;
            if (session.Binding is { } own && !own.SameSignInAs(target)) return;   // another sign-in says nothing about the tab's
            session.CopyPasswordFrom = null;
            session.PasswordMissing = true;
            session.PasswordMissingFor = ConnectionBinding.FromTarget(target, target.SavedConnectionId);
            session.PasswordRefused = !reply.PasswordRequired;
        }

        /// <summary>Per host, when a test needs the answers to arrive in a given order; else <see cref="Remembered"/>.</summary>
        public Func<string, Task<bool>>? RememberedFor { get; set; }

        public Task<bool> HasRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) =>
            RememberedFor?.Invoke(host) ?? Task.FromResult(Remembered);

        /// <summary>Spec 043 follow-up (review): false = the engine did not answer the next forget (it keeps the password).</summary>
        public bool ForgetAnswered { get; set; } = true;

        public Task<bool?> ForgetRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct)
        {
            Forgets++;
            if (!ForgetAnswered) return Task.FromResult<bool?>(null);
            var had = Remembered;
            Remembered = false;
            return Task.FromResult<bool?>(had);
        }

        // ── Spec 043 follow-up: a SQL Server login's password on the engine, and the copy from another tab ──
        public bool EngineRemembersSqlServerPasswords { get; set; }
        public bool EngineCopiesSessionPasswords { get; set; }
        public bool SqlRemembered { get; set; }
        public List<(string Server, string Login)> SqlForgets { get; } = new();
        public List<ConnectionTarget> Listed { get; } = new();

        /// <summary>Review, round 2: when set, the answer to the next question about a SQL login (held open by the test).</summary>
        public Func<Task<bool>>? SqlRememberedAnswer { get; set; }

        /// <summary>Review, round 3: every question about a SQL login, with the certificate choice it was asked for.</summary>
        public List<(string Server, string Login, bool Trust)> SqlAsked { get; } = new();

        /// <summary>Review, round 3: when set, the answer for a certificate choice (else <see cref="SqlRemembered"/>).</summary>
        public Func<bool, bool>? SqlRememberedForTrust { get; set; }

        public Task<bool> HasRememberedSqlServerPasswordAsync(string server, string login, bool trustServerCertificate, CancellationToken ct)
        {
            SqlAsked.Add((server, login, trustServerCertificate));
            return SqlRememberedAnswer?.Invoke()
                ?? Task.FromResult(SqlRememberedForTrust?.Invoke(trustServerCertificate) ?? SqlRemembered);
        }

        /// <summary>Review, round 2: the open tabs' sessions, found by id (the tab a new tab was opened from).</summary>
        public List<SqlSessionState> Sessions { get; } = new();

        public SqlSessionState? FindSession(string sessionId) => Sessions.FirstOrDefault(s => s.SessionId == sessionId);

        public Task<bool?> ForgetRememberedSqlServerPasswordAsync(string server, string login, CancellationToken ct)
        {
            SqlForgets.Add((server, login));
            if (!ForgetAnswered) return Task.FromResult<bool?>(null);
            var had = SqlRemembered;
            SqlRemembered = false;
            return Task.FromResult<bool?>(had);
        }

        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(ConnectionTarget target, CancellationToken ct)
        {
            Listed.Add(target);
            return Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, new[] { "master", "Sales" }, null));
        }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct)
        {
            IsConnected = true;
            Server = server;
            Database = database;
            return Task.FromResult<(bool, string?)>((true, null));
        }

        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, new[] { "master" }, null));

        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly FakeSql _sql = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly ConnectionManagerController _controller = new();
    private readonly SavedSqlConnectionStore _store = new(new InMemoryIndexedDbAdapter());

    public ConnectionManagerModalKindTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(_store);
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton<INotificationService>(_notifications);
    }

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenAsync(DatabaseKind? kind = null)
    {
        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(kind));
        cut.WaitForElement("[data-testid=connection-manager]");
        return cut;
    }

    private static void Type(IRenderedComponent<ConnectionManagerModal> cut, string testId, string value) =>
        cut.Find($"[data-testid={testId}]").Input(value);

    // ── The switch (FR-001, FR-006) ─────────────────────────────────────────────────────────────

    // ── Spec 043, review of US1 (findings 28 to 34): which tab and connection the dialog opens on, its name, its rows ──

    [Fact]
    public async Task With_no_tab_asked_for_the_dialog_opens_on_the_active_tab_connection()
    {
        var tab = new SqlSessionState
        {
            Binding = new ConnectionBinding
            {
                Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 6000, Database = "sales", Login = "app", SslMode = "require",
            },
        };
        _sql.ActiveSession = tab;

        var cut = await OpenAsync();

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-postgresql]").GetAttribute("aria-pressed"));
        Assert.Equal("db1", cut.Find("[data-testid=conn-pg-host]").GetAttribute("value"));
        Assert.Equal("6000", cut.Find("[data-testid=conn-pg-port]").GetAttribute("value"));
        Assert.Equal("sales", cut.Find("[data-testid=conn-pg-database]").GetAttribute("value"));
        Assert.Equal("app", cut.Find("[data-testid=conn-pg-user]").GetAttribute("value"));
        Assert.Equal("require", cut.Find("[data-testid=conn-pg-sslmode]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_tab_with_no_connection_opens_on_SQL_Server_even_when_the_last_saved_row_used_is_PostgreSQL()
    {
        var pg = new SavedSqlConnection { Name = "PG", Kind = DatabaseKind.PostgreSql, Server = "db1", Database = "sales", Login = "app" };
        await _store.AddAsync(pg);
        await _store.SetActiveIdAsync(pg.Id);
        _sql.ActiveSession = new SqlSessionState();

        var cut = await OpenAsync();

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-sqlserver]").GetAttribute("aria-pressed"));
        Assert.DoesNotContain("selected", cut.Find("[data-testid=conn-row]").ClassList);
    }

    [Fact]
    public async Task A_tab_with_no_connection_still_starts_from_the_last_SQL_Server_row_used()
    {
        var sql = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = true };
        await _store.AddAsync(sql);
        await _store.SetActiveIdAsync(sql.Id);
        _sql.ActiveSession = new SqlSessionState();

        var cut = await OpenAsync();

        Assert.Contains("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.Equal("sql01", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_tab_connected_differently_from_its_saved_row_opens_as_a_new_connection_with_its_own_values()
    {
        var local = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "Sales", WindowsAuth = true };
        await _store.AddAsync(local);
        var binding = new ConnectionBinding
        {
            SavedConnectionId = local.Id, Kind = DatabaseKind.SqlServer, Server = "sql02", Database = "Sales",
            WindowsAuth = false, Login = "app_user", TrustServerCertificate = true,
        };

        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(new SqlSessionState(), binding));
        cut.WaitForElement("[data-testid=connection-manager]");

        // The form reconnects the tab to where it was (finding 33), and Save would add a row, never change "Local" (finding 25).
        Assert.DoesNotContain("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.Equal("sql02", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid=conn-auth-sql]").HasAttribute("checked"));
        Assert.Equal("app_user", cut.Find("[data-testid=conn-login-input]").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid=conn-trust-cert]").HasAttribute("checked"));
    }

    [Fact]
    public async Task A_run_that_reconnects_a_tab_in_another_database_takes_that_database_and_leaves_the_row_as_saved()
    {
        var local = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "Sales", WindowsAuth = true };
        await _store.AddAsync(local);
        var binding = new ConnectionBinding { SavedConnectionId = local.Id, Server = "localhost", Database = "tempdb", WindowsAuth = true };

        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(new SqlSessionState(), binding));
        cut.WaitForElement("[data-testid=connection-manager]");

        // The tab goes back to tempdb; "Local" is not the form's, so a Save never writes tempdb into it (review of US1, round 3).
        Assert.DoesNotContain("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.Equal("tempdb", cut.Find("[data-testid=conn-database-select]").GetAttribute("value"));
        Assert.Equal("localhost", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_run_that_reconnects_a_tab_still_in_its_saved_database_keeps_its_row()
    {
        var local = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "Sales", WindowsAuth = true };
        await _store.AddAsync(local);
        var binding = new ConnectionBinding { SavedConnectionId = local.Id, Server = "localhost", Database = "Sales", WindowsAuth = true };

        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(new SqlSessionState(), binding));
        cut.WaitForElement("[data-testid=connection-manager]");

        Assert.Contains("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.Equal("Sales", cut.Find("[data-testid=conn-database-select]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_remembered_password_answer_for_the_form_before_a_kind_switch_is_dropped()
    {
        var pending = new TaskCompletionSource<bool>();
        _sql.RememberedFor = _ => pending.Task;
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");
        cut.Find("[data-testid=conn-pg-host]").Change("db1");   // asked; no answer yet

        cut.Find("[data-testid=conn-kind-sqlserver]").Click();
        cut.Find("[data-testid=conn-kind-postgresql]").Click();   // a fresh PostgreSQL form
        await cut.InvokeAsync(() => pending.SetResult(true));    // the old answer arrives

        Assert.Null(cut.Find("[data-testid=conn-pg-password]").GetAttribute("placeholder"));
        Assert.Empty(cut.FindAll("[data-testid=conn-pg-forget]"));
    }

    [Fact]
    public async Task Opening_the_dialog_on_a_tab_still_on_its_saved_row_shows_that_row_as_saved()
    {
        var local = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "Sales", WindowsAuth = true };
        await _store.AddAsync(local);
        _sql.ActiveSession = new SqlSessionState
        {
            Binding = new ConnectionBinding { SavedConnectionId = local.Id, Server = "localhost", Database = "tempdb", WindowsAuth = true },
        };

        var cut = await OpenAsync();

        // A USE moved the tab to tempdb; the row is still "Local" as saved, so a rename never writes tempdb into it.
        Assert.Contains("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.Equal("Sales", cut.Find("[data-testid=conn-database-select]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_late_answer_about_a_remembered_password_for_an_earlier_host_is_dropped()
    {
        var earlier = new TaskCompletionSource<bool>();
        _sql.RememberedFor = host => host == "db1" ? earlier.Task : Task.FromResult(true);
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-user", "app");
        Type(cut, "conn-pg-host", "db1");
        cut.Find("[data-testid=conn-pg-host]").Change("db1");   // asked about db1: no answer yet
        Type(cut, "conn-pg-host", "db2");
        cut.Find("[data-testid=conn-pg-host]").Change("db2");   // asked about db2: remembered

        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", cut.Find("[data-testid=conn-pg-password]").GetAttribute("placeholder")));
        await cut.InvokeAsync(() => earlier.SetResult(false));   // db1's answer arrives last

        Assert.Equal("Remembered on the engine machine", cut.Find("[data-testid=conn-pg-password]").GetAttribute("placeholder"));
    }

    [Fact]
    public async Task The_dialog_is_named_by_its_visible_title()
    {
        var cut = await OpenAsync();

        var dialog = cut.Find("[data-testid=connection-manager]");
        Assert.Null(dialog.GetAttribute("aria-label"));
        var title = cut.Find("#" + dialog.GetAttribute("aria-labelledby"));
        Assert.Equal("Connect to database", title.TextContent.Trim());
        Assert.Equal("H2", title.TagName);
    }

    [Fact]
    public async Task A_saved_row_is_a_button_in_the_Tab_order_that_picks_the_row()
    {
        await _store.AddAsync(new SavedSqlConnection { Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql03", Database = "Ops", WindowsAuth = false, Login = "ops" });
        var cut = await OpenAsync();

        var pick = cut.Find("[data-testid=conn-row] [data-testid=conn-row-pick]");
        Assert.Equal("BUTTON", pick.TagName);
        Assert.Null(pick.GetAttribute("tabindex"));
        Assert.Null(pick.GetAttribute("aria-current"));

        pick.Click();   // Enter and Space on a button are a click

        Assert.Equal("sql03", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
        Assert.Equal("true", cut.Find("[data-testid=conn-row-pick]").GetAttribute("aria-current"));
    }

    // Gap G9: a tab's connection does not change under its run (data-model §3).
    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public async Task Connect_is_refused_while_the_tab_runs_and_says_why(DatabaseKind kind)
    {
        _sql.ActiveSession = new SqlSessionState { IsBusy = true };
        var cut = await OpenAsync(kind);
        if (kind == DatabaseKind.PostgreSql)
        {
            Type(cut, "conn-pg-host", "db1");
            Type(cut, "conn-pg-user", "app");
        }

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Wait for the current run to finish", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);
        Assert.NotEmpty(cut.FindAll("[data-testid=connection-manager]"));   // still open, nothing lost
    }

    [Fact]
    public async Task A_new_connection_starts_as_SQL_Server_and_both_choices_are_buttons()
    {
        var cut = await OpenAsync();

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-sqlserver]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=conn-kind-postgresql]").GetAttribute("aria-pressed"));
        Assert.Equal("Connect to database", cut.Find("[data-testid=conn-title]").TextContent.Trim());   // spec 043 FR-024
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-server-input]"));
        Assert.Empty(cut.FindAll("[data-testid=conn-pg-host]"));
    }

    [Fact]
    public async Task The_PostgreSQL_choice_is_disabled_with_the_reason_when_the_engine_lacks_it()
    {
        _sql.EngineSupportsPostgres = false;

        var cut = await OpenAsync();

        var button = cut.Find("[data-testid=conn-kind-postgresql]");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("Requires an engine with PostgreSQL support (dialect.postgresql.v1)", button.GetAttribute("title"));
        Assert.Contains("dialect.postgresql.v1", cut.Find("[data-testid=conn-kind-hint]").TextContent);

        // Asking for the PostgreSQL form on such an engine still lands on SQL Server.
        var requested = await OpenAsync(DatabaseKind.PostgreSql);
        Assert.NotEmpty(requested.FindAll("[data-testid=conn-server-input]"));
    }

    [Fact]
    public async Task Clicking_PostgreSQL_shows_its_fields_and_hides_the_SQL_Server_ones()
    {
        var cut = await OpenAsync();

        cut.Find("[data-testid=conn-kind-postgresql]").Click();

        foreach (var id in new[] { "conn-pg-host", "conn-pg-port", "conn-pg-database", "conn-pg-user", "conn-pg-password", "conn-pg-sslmode", "conn-pg-remember" })
            Assert.NotEmpty(cut.FindAll($"[data-testid={id}]"));
        Assert.Empty(cut.FindAll("[data-testid=conn-server-input]"));
        Assert.Empty(cut.FindAll("[data-testid=conn-auth-windows]"));
        Assert.Equal("5432", cut.Find("[data-testid=conn-pg-port]").GetAttribute("value"));
        Assert.Equal("postgres", cut.Find("[data-testid=conn-pg-database]").GetAttribute("value"));
        Assert.Equal("prefer", cut.Find("[data-testid=conn-pg-sslmode]").GetAttribute("value"));
        Assert.Equal("Connect to database", cut.Find("[data-testid=conn-title]").TextContent.Trim());   // spec 043 FR-024
    }

    // ── Spec 043 (US3, FR-024, FR-028, FR-030, research R16) ───────────────────────────────────

    private static readonly string[] Groups = { "conn-group-server", "conn-group-signin", "conn-group-database", "conn-group-security" };

    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public async Task The_form_is_four_named_groups_in_order_after_the_kind_choice(DatabaseKind kind)
    {
        var cut = await OpenAsync(kind);

        var groups = cut.FindAll("[data-testid^=conn-group-]").Select(g => g.GetAttribute("data-testid")).ToArray();
        Assert.Equal(Groups, groups);
        Assert.All(cut.FindAll("[data-testid^=conn-group-]"), g =>
        {
            Assert.Equal("FIELDSET", g.TagName);
            Assert.False(string.IsNullOrWhiteSpace(g.QuerySelector("legend")?.TextContent));
        });

        // The kind choice is the first control of the form; keyboard order is document order (no positive tabindex).
        var first = cut.Find(".akml-connmgr-form-pane").QuerySelector("button, input, select");
        Assert.Equal("conn-kind-sqlserver", first!.GetAttribute("data-testid"));
        Assert.DoesNotContain(cut.FindAll(".akml-connmgr-form-pane [tabindex]"), e => int.TryParse(e.GetAttribute("tabindex"), out var t) && t > 0);
    }

    [Fact]
    public async Task The_trust_choice_lives_in_the_security_group_and_says_what_it_risks()
    {
        var cut = await OpenAsync();
        Type(cut, "conn-server-input", "db1.example.com");
        cut.Find("[data-testid=conn-server-input]").Change("db1.example.com");

        var security = cut.Find("[data-testid=conn-group-security]");
        var box = security.QuerySelector("[data-testid=conn-trust-cert]");
        Assert.NotNull(box);
        Assert.False(box!.HasAttribute("disabled"));
        var help = cut.Find("[data-testid=conn-trust-cert-help]").TextContent;
        Assert.Contains("self-signed", help);
        Assert.Contains("never validated", help);
    }

    [Fact]
    public async Task For_a_server_on_the_engine_machine_the_trust_choice_is_disabled_and_says_why()
    {
        var cut = await OpenAsync();
        Type(cut, "conn-server-input", "localhost");
        cut.Find("[data-testid=conn-server-input]").Change("localhost");

        Assert.True(cut.Find("[data-testid=conn-trust-cert]").HasAttribute("disabled"));
        Assert.Contains("engine's own computer", cut.Find("[data-testid=conn-trust-cert-help]").TextContent);
    }

    [Fact]
    public async Task The_palette_entry_opens_the_dialog_on_the_PostgreSQL_kind()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-postgresql]").GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-pg-host]"));
    }

    [Fact]
    public async Task The_CA_certificate_path_appears_only_for_the_verifying_modes()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Assert.Empty(cut.FindAll("[data-testid=conn-pg-rootcert]"));

        cut.Find("[data-testid=conn-pg-sslmode]").Change("verify-full");
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-pg-rootcert]"));

        cut.Find("[data-testid=conn-pg-sslmode]").Change("require");
        Assert.Empty(cut.FindAll("[data-testid=conn-pg-rootcert]"));
    }

    [Fact]
    public async Task The_remember_option_says_where_the_password_goes_and_who_can_use_it()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);

        var help = cut.Find("[data-testid=conn-pg-remember-help]").TextContent;

        Assert.False(cut.Find("[data-testid=conn-pg-remember]").HasAttribute("checked"));
        Assert.Contains("engine machine", help);
        Assert.Contains("administrator of the engine machine", help);
        Assert.Contains("Any client paired with this engine can use it", help);
    }

    // ── Test (FR-002) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test_shows_the_version_and_where_it_connected()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");
        Type(cut, "conn-pg-password", "s3cret");

        cut.Find("[data-testid=conn-test-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("PostgreSQL 18.1 on db1:5432", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        var tested = Assert.Single(_sql.Tested);
        Assert.Equal((DatabaseKind.PostgreSql, "db1", "postgres", "app", "s3cret", 5432, "prefer"),
            (tested.Kind, tested.Server, tested.Database, tested.User, tested.Password, tested.Port, tested.SslMode));
        Assert.Contains("ok", cut.Find("[data-testid=conn-message]").ClassList);
    }

    [Fact]
    public async Task A_failed_test_shows_the_engines_words()
    {
        _sql.TestReply = ConnectResult.Failed("password authentication failed (SQLSTATE 28P01). Check the user name and password.");
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-test-btn]").Click();

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("[data-testid=conn-message]");
            Assert.Contains("28P01", message.TextContent);
            Assert.Contains("error", message.ClassList);
        });
    }

    [Fact]
    public async Task A_server_that_wants_a_password_asks_for_it_instead_of_reporting_a_failure()
    {
        _sql.TestReply = new ConnectResult(false, "The server asked for a password and none was supplied.", PasswordRequired: true);
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-test-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Enter the password", cut.Find("[data-testid=conn-message]").TextContent));
    }

    // ── Save (FR-001, FR-104) ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_stores_the_record_with_its_kind_and_no_password()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-name-input", "Production");
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-port", "5433");
        Type(cut, "conn-pg-database", "northwind");
        Type(cut, "conn-pg-user", "app");
        Type(cut, "conn-pg-password", "s3cret-value");
        cut.Find("[data-testid=conn-pg-sslmode]").Change("require");
        cut.Find("[data-testid=conn-pg-remember]").Change(true);

        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        var saved = Assert.Single(await _store.ListAsync());
        Assert.Equal(DatabaseKind.PostgreSql, saved.Kind);
        Assert.Equal(("Production", "db1", 5433, "northwind", "app", "require", true),
            (saved.Name, saved.Server, saved.Port, saved.Database, saved.Login, saved.SslMode, saved.RememberPassword));
        Assert.DoesNotContain("s3cret-value", JsonSerializer.Serialize(saved));
        Assert.Single(cut.FindAll("[data-testid=conn-row]"));
        Assert.Equal("PG", cut.Find("[data-testid=conn-row-kind]").TextContent.Trim());
    }

    [Fact]
    public async Task A_saved_PostgreSQL_row_loads_its_fields_and_never_a_password()
    {
        await _store.AddAsync(new SavedSqlConnection
        {
            Name = "Prod", Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 6000, Database = "sales",
            Login = "app", SslMode = "verify-ca", RootCertificatePath = @"C:\certs\root.crt", RememberPassword = true,
        });
        _sql.Remembered = true;

        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=conn-pg-forget]")));
        Assert.Equal("db1", cut.Find("[data-testid=conn-pg-host]").GetAttribute("value"));
        Assert.Equal("6000", cut.Find("[data-testid=conn-pg-port]").GetAttribute("value"));
        Assert.Equal("sales", cut.Find("[data-testid=conn-pg-database]").GetAttribute("value"));
        Assert.Equal("app", cut.Find("[data-testid=conn-pg-user]").GetAttribute("value"));
        Assert.Equal("verify-ca", cut.Find("[data-testid=conn-pg-sslmode]").GetAttribute("value"));
        Assert.Equal(@"C:\certs\root.crt", cut.Find("[data-testid=conn-pg-rootcert]").GetAttribute("value"));
        Assert.True(string.IsNullOrEmpty(cut.Find("[data-testid=conn-pg-password]").GetAttribute("value")));
        Assert.Equal("Remembered on the engine machine", cut.Find("[data-testid=conn-pg-password]").GetAttribute("placeholder"));
    }

    // ── Forget (FR-106) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Forget_is_offered_only_when_the_engine_holds_a_password_and_removes_it()
    {
        var withoutOne = await OpenAsync(DatabaseKind.PostgreSql);
        Type(withoutOne, "conn-pg-host", "db1");
        Type(withoutOne, "conn-pg-user", "app");
        Assert.Empty(withoutOne.FindAll("[data-testid=conn-pg-forget]"));

        _sql.Remembered = true;
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        // A browser fires input while typing and change when the field is left; the engine is asked on the change.
        cut.Find("[data-testid=conn-pg-user]").Input("app");
        cut.Find("[data-testid=conn-pg-user]").Change("app");
        cut.WaitForElement("[data-testid=conn-pg-forget]");

        cut.Find("[data-testid=conn-pg-forget]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=conn-pg-forget]"));
            Assert.Contains("forgotten", cut.Find("[data-testid=conn-message]").TextContent);
        });
        Assert.Equal(1, _sql.Forgets);
    }

    // ── Connect ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_sends_the_target_with_the_remember_choice_and_closes_on_success()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");
        Type(cut, "conn-pg-password", "s3cret");
        cut.Find("[data-testid=conn-pg-remember]").Change(true);

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.Equal((DatabaseKind.PostgreSql, "db1", "app", "s3cret", true), (target.Kind, target.Server, target.User, target.Password, target.RememberPassword));
    }

    // ── An open transaction (FR-009) ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TransactionState.Open, "Open transaction", "has an open transaction")]
    [InlineData(TransactionState.Aborted, "Aborted transaction", "an error aborted")]
    public async Task Connecting_elsewhere_while_a_transaction_is_open_asks_first_and_declining_connects_nothing(
        TransactionState state, string title, string wording)
    {
        _sql.IsConnected = true;
        _sql.TransactionState = state;
        _dialogs.ConfirmAnswer = _ => false;
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db2");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Single(_dialogs.Confirms));
        var asked = _dialogs.Confirms.Single();
        Assert.Equal(title, asked.Title);
        Assert.Contains(wording, asked.Message);
        Assert.Contains("rolls it back. Nothing is committed.", asked.Message);
        Assert.True(asked.Destructive);
        Assert.Empty(_sql.Connected);
        Assert.NotEmpty(cut.FindAll("[data-testid=connection-manager]"));
    }

    [Fact]
    public async Task Agreeing_to_roll_back_connects_and_no_question_is_asked_without_a_transaction()
    {
        _sql.IsConnected = true;
        _sql.TransactionState = TransactionState.Open;
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db2");
        Type(cut, "conn-pg-user", "app");
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Single(_sql.Connected));

        _sql.TransactionState = TransactionState.None;
        var again = await OpenAsync(DatabaseKind.PostgreSql);
        Type(again, "conn-pg-host", "db3");
        Type(again, "conn-pg-user", "app");
        again.Find("[data-testid=conn-connect-btn]").Click();

        again.WaitForAssertion(() => Assert.Equal(2, _sql.Connected.Count));
        Assert.Single(_dialogs.Confirms);   // only the first connect asked
    }

    [Fact]
    public async Task A_failed_connect_keeps_the_dialog_open_with_the_reason()
    {
        _sql.ConnectReply = ConnectResult.Failed("Could not reach db1:5432: connection refused. Check the host name and port.");
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not reach db1:5432", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid=connection-manager]"));
    }

    // ── A remembered password (FR-105, SC-018) ──────────────────────────────────────────────────

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenWithRememberedAsync()
    {
        _sql.Remembered = true;
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        // A browser fires input while typing and change when the field is left; the engine is asked on the change.
        cut.Find("[data-testid=conn-pg-user]").Input("app");
        cut.Find("[data-testid=conn-pg-user]").Change("app");
        cut.WaitForElement("[data-testid=conn-pg-forget]");
        cut.Find("[data-testid=conn-pg-remember]").Change(true);
        return cut;
    }

    [Fact]
    public async Task Connecting_with_a_remembered_password_sends_none_and_asks_for_none()
    {
        var cut = await OpenWithRememberedAsync();
        Assert.Equal("Remembered on the engine machine", cut.Find("[data-testid=conn-pg-password]").GetAttribute("placeholder"));

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.True(string.IsNullOrEmpty(target.Password));
        Assert.True(target.RememberPassword);
    }

    [Fact]
    public async Task A_remembered_password_the_server_rejects_says_so_and_keeps_Remember_on_to_replace_it()
    {
        _sql.ConnectReply = ConnectResult.Failed("password authentication failed (SQLSTATE 28P01). Check the user name and password.");
        var cut = await OpenWithRememberedAsync();

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("[data-testid=conn-message]").TextContent;
            Assert.Contains("remembered password was not accepted", message);
            Assert.Contains("Remember password", message);
        });
        Assert.NotEmpty(cut.FindAll("[data-testid=connection-manager]"));
        Assert.True(cut.Find("[data-testid=conn-pg-remember]").HasAttribute("checked"));
    }

    [Fact]
    public async Task The_same_rejection_without_a_remembered_password_is_an_ordinary_failure()
    {
        _sql.ConnectReply = ConnectResult.Failed("password authentication failed (SQLSTATE 28P01). Check the user name and password.");
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("password authentication failed", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.DoesNotContain("remembered", cut.Find("[data-testid=conn-message]").TextContent);
    }

    [Fact]
    public async Task A_server_that_wants_a_password_when_none_is_remembered_asks_for_it_and_offers_to_remember_it()
    {
        _sql.ConnectReply = new ConnectResult(false, "The server asked for a password and none was supplied.", PasswordRequired: true);
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        Type(cut, "conn-pg-host", "db1");
        Type(cut, "conn-pg-user", "app");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("[data-testid=conn-message]").TextContent;
            Assert.Contains("Enter the password", message);
            Assert.Contains("Remember password", message);
        });
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-pg-remember]"));
    }

    [Fact]
    public async Task No_label_or_hint_of_the_PostgreSQL_form_names_SQL_Server()
    {
        var cut = await OpenAsync(DatabaseKind.PostgreSql);
        cut.Find("[data-testid=conn-pg-sslmode]").Change("verify-full");

        // The kind switch offers both kinds by name; every label and hint of the fields themselves is PostgreSQL's.
        var texts = cut.FindAll(".akml-connmgr-form-pane label, .akml-connmgr-form-pane .akml-connmgr-hint, .akml-connmgr-form-pane .akml-field-label")
            .Select(e => e.TextContent)
            .ToList();

        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.DoesNotContain("SQL Server", t, StringComparison.Ordinal));
    }

    // ── Spec 043 follow-up (review): a SQL login's password on the engine, Forget, and the copy from another tab ─────────────

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenSqlLoginAsync(bool remembered)
    {
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = remembered;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-auth-sql]").Change(true);
        Type(cut, "conn-server-input", "sql01");
        cut.Find("[data-testid=conn-server-input]").Change("sql01");
        // A browser fires input while typing and change when the field is left; the engine is asked on the change.
        cut.Find("[data-testid=conn-login-input]").Input("app");
        cut.Find("[data-testid=conn-login-input]").Change("app");
        return cut;
    }

    private static bool RememberTicked(IRenderedComponent<ConnectionManagerModal> cut) =>
        cut.Find("[data-testid=conn-sql-remember]").HasAttribute("checked");

    [Fact]
    public async Task Remember_is_ticked_when_the_engine_holds_the_logins_password()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);

        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        Assert.Equal("Remembered on the engine machine", cut.Find("[data-testid=conn-password-input]").GetAttribute("placeholder"));
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-sql-forget]"));
    }

    [Fact]
    public async Task With_Remember_unticked_the_password_must_be_typed_even_when_the_engine_holds_one()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));

        cut.Find("[data-testid=conn-sql-remember]").Change(false);

        Assert.Null(cut.Find("[data-testid=conn-password-input]").GetAttribute("placeholder"));
        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password", cut.Find("[data-testid=conn-message]").TextContent));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Tested);
        Assert.Empty(_sql.Connected);
        Assert.Empty(_sql.SqlForgets);   // nothing is forgotten until a connect succeeds
    }

    [Fact]
    public async Task Connecting_with_Remember_unticked_forgets_the_engines_copy_after_the_connect()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        cut.Find("[data-testid=conn-sql-remember]").Change(false);
        Type(cut, "conn-password-input", "typed-now");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.Equal("typed-now", target.Password);
        Assert.False(target.RememberPassword);
        Assert.Equal(("sql01", "app"), Assert.Single(_sql.SqlForgets));
        Assert.False(_sql.SqlRemembered);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task A_forget_on_connect_the_engine_does_not_answer_says_the_engine_still_holds_the_password()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        cut.Find("[data-testid=conn-sql-remember]").Change(false);
        Type(cut, "conn-password-input", "typed-now");
        _sql.ForgetAnswered = false;

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));   // the connect stands
        var notice = Assert.Single(_notifications.Sent);
        Assert.Equal(NotificationKind.Warning, notice.Kind);
        Assert.Contains("did not confirm that it forgot the password", notice.Text);
        Assert.Contains("may still be remembered on the engine machine", notice.Text);
        Assert.True(_sql.SqlRemembered);
    }

    [Fact]
    public async Task Forget_that_the_engine_does_not_answer_says_so_and_changes_nothing()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        _sql.ForgetAnswered = false;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForElement("[data-testid=conn-sql-forget]");

        cut.Find("[data-testid=conn-sql-forget]").Click();

        cut.WaitForAssertion(() => Assert.Equal("The engine did not confirm that it forgot the password; it may still be remembered on the engine machine.", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        Assert.Contains("error", cut.Find("[data-testid=conn-message]").ClassList);
        Assert.Single(_sql.SqlForgets);
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-sql-forget]"));
        Assert.True(RememberTicked(cut));
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task Forget_that_the_engine_answers_unticks_Remember_and_clears_the_saved_row()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForElement("[data-testid=conn-sql-forget]");

        cut.Find("[data-testid=conn-sql-forget]").Click();

        cut.WaitForAssertion(() => Assert.Equal("The remembered password was forgotten.", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-forget]"));
        Assert.False(RememberTicked(cut));
        Assert.False((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task A_PostgreSQL_forget_the_engine_does_not_answer_keeps_the_remembered_password()
    {
        var cut = await OpenWithRememberedAsync();
        _sql.ForgetAnswered = false;

        cut.Find("[data-testid=conn-pg-forget]").Click();

        cut.WaitForAssertion(() => Assert.Equal("The engine did not confirm that it forgot the password; it may still be remembered on the engine machine.", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        Assert.NotEmpty(cut.FindAll("[data-testid=conn-pg-forget]"));
        Assert.True(cut.Find("[data-testid=conn-pg-remember]").HasAttribute("checked"));
        Assert.True(_sql.Remembered);
    }

    [Fact]
    public async Task A_password_the_engine_could_not_keep_is_told_and_the_saved_row_is_not_left_remembered()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);   // the engine could not write its store
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        Assert.True(RememberTicked(cut));
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.True(Assert.Single(_sql.Connected).RememberPassword);
        var notice = Assert.Single(_notifications.Sent);
        Assert.Equal(NotificationKind.Warning, notice.Kind);
        Assert.Contains("could not keep the password", notice.Text);
        Assert.False((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task A_password_the_engine_kept_needs_no_notice()
    {
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);
        var cut = await OpenSqlLoginAsync(remembered: false);
        cut.Find("[data-testid=conn-sql-remember]").Change(true);
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.Empty(_notifications.Sent);
        Assert.Empty(_sql.SqlForgets);
    }

    private static ConnectionBinding SqlLogin(string server = "sql01", string login = "app", bool remember = false) =>
        new() { Kind = DatabaseKind.SqlServer, Server = server, Database = "Sales", WindowsAuth = false, Login = login, RememberPassword = remember };

    // A new query tab opened on a connected SQL login tab (New query): its binding is a copy of that tab's.
    private SqlSessionState NewQueryTab(string sourceServer = "sql01", string sourceLogin = "app")
    {
        var source = new SqlSessionState("source-tab") { IsConnected = true, Binding = SqlLogin(sourceServer, sourceLogin) };
        _sql.Sessions.Add(source);
        var tab = new SqlSessionState("new-tab") { Binding = SqlLogin(sourceServer, sourceLogin), NeedsReattach = true, CopyPasswordFrom = "source-tab" };
        _sql.Sessions.Add(tab);
        return tab;
    }

    [Fact]
    public async Task A_new_query_tabs_dialog_lists_and_connects_with_the_password_of_the_tab_it_was_opened_from()
    {
        _sql.EngineCopiesSessionPasswords = true;
        _sql.ActiveSession = NewQueryTab();

        var cut = await OpenAsync();

        // Nothing typed: the list comes with the other tab's password, and Connect needs none either.
        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.Listed));
        var listed = _sql.Listed.Last();
        Assert.Equal("source-tab", listed.CopyPasswordFrom);
        Assert.True(string.IsNullOrEmpty(listed.Password));
        Assert.Equal("The same as the query tab this one was opened from", cut.Find("[data-testid=conn-password-input]").GetAttribute("placeholder"));

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.Equal("source-tab", Assert.Single(_sql.Connected).CopyPasswordFrom);
    }

    [Fact]
    public async Task A_typed_password_is_sent_instead_of_the_other_tabs_and_an_engine_that_cannot_copy_asks_for_one()
    {
        _sql.ActiveSession = NewQueryTab();
        var cut = await OpenAsync();   // the engine does not offer session.copy.v2

        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);

        _sql.EngineCopiesSessionPasswords = true;
        Type(cut, "conn-password-input", "typed");
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Single(_sql.Connected));
        Assert.Null(_sql.Connected.Single().CopyPasswordFrom);
        Assert.Equal("typed", _sql.Connected.Single().Password);
    }

    [Fact]
    public async Task A_remembered_PostgreSQL_password_refused_by_its_error_code_says_so_whatever_the_servers_words()
    {
        // A server in another language: no "28P01" or English text, only the engine's code.
        _sql.ConnectReply = new ConnectResult(false, "la authentification par mot de passe a échoué",
            ErrorCode: AkmlSql.Core.Ipc.Messages.DialectErrors.PgLoginFailed);
        var cut = await OpenWithRememberedAsync();

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("remembered password was not accepted", cut.Find("[data-testid=conn-message]").TextContent));
    }

    // ── Spec 043 follow-up (review, round 2) ─────────────────────────────────────────────────────────────────────────────────

    private static string? Placeholder(IRenderedComponent<ConnectionManagerModal> cut, string testId = "conn-password-input") =>
        cut.Find($"[data-testid={testId}]").GetAttribute("placeholder");

    // [1] The released engines advertise session.copy.v1 only: they ignore a database list's copy source.
    [Fact]
    public async Task On_an_engine_without_session_copy_v2_a_new_tabs_dialog_neither_lists_nor_offers_the_other_tabs_password()
    {
        _sql.EngineCopiesSessionPasswords = false;
        _sql.ActiveSession = NewQueryTab();

        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-database-refresh]").Click();

        Assert.Equal("sql01", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
        Assert.Null(Placeholder(cut));
        Assert.Contains("Enter the SQL password", cut.Find("[data-testid=conn-database-hint]").TextContent);
        Assert.Empty(_sql.Listed);
    }

    // [3] The dialog a run opened because the tab's remembered password was refused.
    private static SqlSessionState RefusedTab(ConnectionBinding binding) =>
        new("refused-tab") { Binding = binding, NeedsReattach = true, PasswordMissing = true, PasswordRefused = true };

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenOnAsync(SqlSessionState tab)
    {
        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(tab, tab.Binding));
        cut.WaitForElement("[data-testid=connection-manager]");
        return cut;
    }

    [Fact]
    public async Task A_tab_whose_remembered_SQL_password_was_refused_asks_for_the_current_one_and_never_sends_the_old_one()
    {
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;   // the engine still holds the password the server refused
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);

        var cut = await OpenOnAsync(RefusedTab(SqlLogin(remember: true)));

        cut.WaitForElement("[data-testid=conn-sql-refused-hint]");
        Assert.Contains("not accepted", cut.Find("[data-testid=conn-sql-refused-hint]").TextContent);
        Assert.Null(Placeholder(cut));
        Assert.True(RememberTicked(cut));   // the password typed now replaces the stored one
        Assert.Empty(_sql.Listed);          // no list with the refused password

        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password", cut.Find("[data-testid=conn-message]").TextContent));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Tested);
        Assert.Empty(_sql.Connected);

        Type(cut, "conn-password-input", "current");
        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.Equal(("current", true), (target.Password, target.RememberPassword));
    }

    [Fact]
    public async Task Another_login_in_the_dialog_of_a_refused_tab_follows_the_normal_rules()
    {
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        var cut = await OpenOnAsync(RefusedTab(SqlLogin(remember: true)));
        cut.WaitForElement("[data-testid=conn-sql-refused-hint]");

        cut.Find("[data-testid=conn-login-input]").Input("reporting");
        cut.Find("[data-testid=conn-login-input]").Change("reporting");

        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", Placeholder(cut)));
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-refused-hint]"));
        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.Listed));
        Assert.Equal("reporting", _sql.Listed.Last().User);
    }

    [Fact]
    public async Task A_PostgreSQL_tab_whose_remembered_password_was_refused_asks_for_the_current_one_and_never_sends_the_old_one()
    {
        _sql.Remembered = true;
        var binding = new ConnectionBinding
        {
            Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 5432, Database = "sales", Login = "app", RememberPassword = true,
        };

        var cut = await OpenOnAsync(RefusedTab(binding));

        cut.WaitForElement("[data-testid=conn-pg-refused-hint]");
        Assert.Null(Placeholder(cut, "conn-pg-password"));
        Assert.True(cut.Find("[data-testid=conn-pg-remember]").HasAttribute("checked"));
        cut.Find("[data-testid=conn-pg-database-refresh]").Click();
        Assert.Contains("Enter the password", cut.Find("[data-testid=conn-pg-database-hint]").TextContent);
        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the current password", cut.Find("[data-testid=conn-message]").TextContent));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the current password", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Listed);
        Assert.Empty(_sql.Tested);
        Assert.Empty(_sql.Connected);

        Type(cut, "conn-pg-password", "current");
        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.Equal(("current", true), (target.Password, target.RememberPassword));
    }

    // [4] The other tab's password, only where it can be the right one.
    [Fact]
    public async Task A_new_tabs_dialog_offers_the_other_tabs_password_only_for_that_tabs_server_and_login()
    {
        _sql.EngineCopiesSessionPasswords = true;
        _sql.ActiveSession = NewQueryTab();
        var cut = await OpenAsync();
        cut.WaitForAssertion(() => Assert.Equal("The same as the query tab this one was opened from", Placeholder(cut)));

        Type(cut, "conn-server-input", "sql02");
        cut.Find("[data-testid=conn-server-input]").Change("sql02");

        Assert.Null(Placeholder(cut));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);

        Type(cut, "conn-server-input", " SQL01 ");   // the other tab's server again, written another way
        cut.Find("[data-testid=conn-server-input]").Change(" SQL01 ");
        Assert.Equal("The same as the query tab this one was opened from", Placeholder(cut));

        cut.Find("[data-testid=conn-login-input]").Input("reporting");
        Assert.Null(Placeholder(cut));
    }

    [Fact]
    public async Task A_new_tabs_dialog_does_not_offer_the_password_of_a_tab_that_was_closed()
    {
        _sql.EngineCopiesSessionPasswords = true;
        var tab = NewQueryTab();
        _sql.Sessions.RemoveAll(s => s.SessionId == "source-tab");
        _sql.ActiveSession = tab;

        var cut = await OpenAsync();

        Assert.Null(Placeholder(cut));
        Assert.Empty(_sql.Listed);
    }

    [Fact]
    public async Task With_Remember_unticked_and_a_password_remembered_for_the_login_the_other_tabs_password_is_not_used_either()
    {
        _sql.EngineCopiesSessionPasswords = true;
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        _sql.ActiveSession = NewQueryTab();
        var cut = await OpenAsync();
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));

        cut.Find("[data-testid=conn-sql-remember]").Change(false);

        // A refused copy would fall back to the remembered password, which unticking Remember said not to use.
        Assert.Null(Placeholder(cut));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);
    }

    [Fact]
    public async Task An_answer_about_a_remembered_password_that_arrives_after_Remember_was_changed_does_not_tick_it_again()
    {
        await _store.AddAsync(new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        });
        _sql.EngineRemembersSqlServerPasswords = true;
        var answer = new TaskCompletionSource<bool>();
        _sql.SqlRememberedAnswer = () => answer.Task;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();   // the engine is asked; no answer yet
        Assert.True(RememberTicked(cut));                  // the saved row asks for it

        cut.Find("[data-testid=conn-sql-remember]").Change(false);   // the user's choice, while the engine is asked
        await cut.InvokeAsync(() => answer.SetResult(true));

        cut.WaitForElement("[data-testid=conn-sql-forget]");   // the answer arrived
        Assert.False(RememberTicked(cut));
        Assert.Null(Placeholder(cut));
    }

    // [5] The engine kept no new password, and held an older one.
    [Fact]
    public async Task A_new_password_the_engine_could_not_keep_says_the_one_it_remembered_before_may_still_be_used()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Type(cut, "conn-password-input", "new-password");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var notice = Assert.Single(_notifications.Sent);
        Assert.Equal(NotificationKind.Warning, notice.Kind);
        Assert.Contains("may still be used", notice.Text);
        Assert.Contains("Forget", notice.Text);
        Assert.DoesNotContain("asked for again next time", notice.Text);
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);   // Settings › Connections still offers Forget password
    }

    // ── Spec 043 follow-up (review, round 3) ─────────────────────────────────────────────────────────────────────────────────

    // [1] The question about a SQL login's password is for the form's certificate choice, and is asked again when it changes.
    [Fact]
    public async Task Changing_the_trust_choice_asks_the_engine_again_for_that_choice()
    {
        _sql.SqlRememberedForTrust = trust => !trust;   // a password remembered for a validated connection only
        var cut = await OpenSqlLoginAsync(remembered: false);
        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", Placeholder(cut)));
        Assert.Equal(("sql01", "app", false), _sql.SqlAsked.Last());

        cut.Find("[data-testid=conn-trust-cert]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(("sql01", "app", true), _sql.SqlAsked.Last()));
        cut.WaitForAssertion(() => Assert.Null(Placeholder(cut)));
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-forget]"));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);

        cut.Find("[data-testid=conn-trust-cert]").Change(false);
        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", Placeholder(cut)));
    }

    // [2] A tab with no binding that is missing its password: the Status bar's restore of a remembered login on a fresh page.
    private async Task<SavedSqlConnection> ActiveSavedSqlLoginAsync()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = true,
        };
        await _store.AddAsync(row);
        await _store.SetActiveIdAsync(row.Id);
        return row;
    }

    private static SqlSessionState FreshTabMissingItsPassword(ConnectionBinding signIn, bool refused) =>
        new("fresh-tab") { PasswordMissing = true, PasswordMissingFor = signIn, PasswordRefused = refused };

    // The Status bar restores the saved row it names, so the mark's sign-in carries the row's id.
    private static ConnectionBinding RestoredSignIn(SavedSqlConnection row)
    {
        var signIn = SqlLogin(remember: true);
        signIn.SavedConnectionId = row.Id;
        return signIn;
    }

    [Fact]
    public async Task A_tab_with_no_binding_whose_restored_login_was_refused_neither_lists_nor_sends_the_refused_password()
    {
        var row = await ActiveSavedSqlLoginAsync();
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;   // the engine still holds the password the server refused
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);
        _sql.ActiveSession = FreshTabMissingItsPassword(RestoredSignIn(row), refused: true);

        var cut = await OpenAsync();   // the run of a tab with no binding: the dialog opens on the last saved row used

        cut.WaitForElement("[data-testid=conn-sql-forget]");   // the engine was asked and holds one
        Assert.Contains("selected", cut.Find("[data-testid=conn-row]").ClassList);
        Assert.True(RememberTicked(cut));   // the password typed now replaces the stored one
        Assert.Contains("not accepted", cut.Find("[data-testid=conn-sql-refused-hint]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-missing-hint]"));
        Assert.Null(Placeholder(cut));
        Assert.Empty(_sql.Listed);   // no list with the refused password

        cut.Find("[data-testid=conn-database-refresh]").Click();
        Assert.Contains("Enter the SQL password", cut.Find("[data-testid=conn-database-hint]").TextContent);
        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password", cut.Find("[data-testid=conn-message]").TextContent));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Listed);
        Assert.Empty(_sql.Tested);
        Assert.Empty(_sql.Connected);

        Type(cut, "conn-password-input", "current");
        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var target = Assert.Single(_sql.Connected);
        Assert.Equal(("current", true), (target.Password, target.RememberPassword));
    }

    [Fact]
    public async Task A_tab_with_no_binding_whose_engine_had_no_password_says_that_instead()
    {
        var row = await ActiveSavedSqlLoginAsync();
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.ActiveSession = FreshTabMissingItsPassword(RestoredSignIn(row), refused: false);

        var cut = await OpenAsync();

        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.SqlAsked));
        Assert.Contains("no password it can use for this login", cut.Find("[data-testid=conn-sql-missing-hint]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-refused-hint]"));
        Assert.Empty(_sql.Listed);
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);
    }

    [Fact]
    public async Task Another_login_in_the_dialog_of_a_tab_with_no_binding_follows_the_normal_rules()
    {
        var row = await ActiveSavedSqlLoginAsync();
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        _sql.ActiveSession = FreshTabMissingItsPassword(RestoredSignIn(row), refused: true);
        var cut = await OpenAsync();
        cut.WaitForElement("[data-testid=conn-sql-refused-hint]");

        cut.Find("[data-testid=conn-login-input]").Input("reporting");
        cut.Find("[data-testid=conn-login-input]").Change("reporting");

        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", Placeholder(cut)));
        Assert.Empty(cut.FindAll("[data-testid=conn-sql-refused-hint]"));
        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.Listed));
        Assert.Equal("reporting", _sql.Listed.Last().User);
    }

    [Fact]
    public async Task A_PostgreSQL_tab_with_no_binding_whose_remembered_password_was_refused_asks_for_the_current_one()
    {
        var row = new SavedSqlConnection { Name = "PG", Kind = DatabaseKind.PostgreSql, Server = "db1", Database = "sales", Login = "app", RememberPassword = true };
        await _store.AddAsync(row);
        await _store.SetActiveIdAsync(row.Id);
        _sql.Remembered = true;
        _sql.ActiveSession = FreshTabMissingItsPassword(new ConnectionBinding
        {
            Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 5432, Database = "sales", Login = "app", RememberPassword = true,
        }, refused: true);

        var cut = await OpenAsync(DatabaseKind.PostgreSql);

        cut.WaitForElement("[data-testid=conn-pg-forget]");   // the engine was asked and holds one
        Assert.Contains("not accepted", cut.Find("[data-testid=conn-pg-refused-hint]").TextContent);
        Assert.Null(Placeholder(cut, "conn-pg-password"));
        Assert.Empty(_sql.Listed);
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the current password", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);
    }

    [Fact]
    public async Task A_tab_with_no_binding_opens_on_the_sign_in_its_password_is_missing_for_of_either_kind()
    {
        _sql.Remembered = true;
        _sql.ActiveSession = FreshTabMissingItsPassword(new ConnectionBinding
        {
            Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 6000, Database = "sales", Login = "app", SslMode = "require", RememberPassword = true,
        }, refused: false);

        var cut = await OpenAsync();   // no kind asked for, no saved row: the tab's record decides

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-postgresql]").GetAttribute("aria-pressed"));
        Assert.Equal(("db1", "6000", "app"), (cut.Find("[data-testid=conn-pg-host]").GetAttribute("value"),
            cut.Find("[data-testid=conn-pg-port]").GetAttribute("value"), cut.Find("[data-testid=conn-pg-user]").GetAttribute("value")));
        cut.WaitForElement("[data-testid=conn-pg-missing-hint]");
        Assert.Empty(_sql.Listed);   // not even with the password the engine says it holds
    }

    // [3] Where to forget a password the engine still holds after it could not keep the new one.
    [Fact]
    public async Task A_new_password_the_engine_could_not_keep_makes_the_saved_row_offer_Forget_password_in_Settings()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = false,   // saved without it; the engine holds one for this login all the same
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Assert.True(RememberTicked(cut));
        Type(cut, "conn-password-input", "new-password");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var notice = Assert.Single(_notifications.Sent);
        Assert.Contains("Forget it in Connect to database", notice.Text);
        Assert.Contains("Forget password in Settings › Connections", notice.Text);
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);   // so Settings › Connections shows Forget password
    }

    [Fact]
    public async Task A_new_password_the_engine_could_not_keep_without_a_saved_row_points_only_to_Connect_to_database()
    {
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Type(cut, "conn-password-input", "new-password");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        var notice = Assert.Single(_notifications.Sent);
        Assert.Contains("may still be used", notice.Text);
        Assert.Contains("Forget it in Connect to database.", notice.Text);
        Assert.DoesNotContain("Settings", notice.Text);
    }

    [Fact]
    public async Task A_saved_row_for_another_login_than_the_one_connected_is_not_made_to_offer_Forget()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = false,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        _sql.SqlRemembered = true;   // for any login, in this fake
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.Find("[data-testid=conn-login-input]").Input("reporting");   // the form edited away from the row's login
        cut.Find("[data-testid=conn-login-input]").Change("reporting");
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Type(cut, "conn-password-input", "new-password");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.DoesNotContain("Settings", Assert.Single(_notifications.Sent).Text);
        Assert.False((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    // [4] Another tab's password is offered only where the engine would copy it: the same certificate checking too.
    [Fact]
    public async Task A_new_tabs_dialog_offers_the_other_tabs_password_only_with_its_trust_choice()
    {
        _sql.EngineCopiesSessionPasswords = true;
        _sql.ActiveSession = NewQueryTab();   // sql01, certificate validated
        var cut = await OpenAsync();
        cut.WaitForAssertion(() => Assert.Equal("The same as the query tab this one was opened from", Placeholder(cut)));

        cut.Find("[data-testid=conn-trust-cert]").Change(true);

        Assert.Null(Placeholder(cut));
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);

        cut.Find("[data-testid=conn-trust-cert]").Change(false);
        Assert.Equal("The same as the query tab this one was opened from", Placeholder(cut));
    }

    [Fact]
    public async Task On_the_engine_machine_the_other_tabs_password_is_offered_whatever_either_trust_checkbox_says()
    {
        _sql.EngineCopiesSessionPasswords = true;
        _sql.Sessions.Add(new SqlSessionState("source-tab") { IsConnected = true, Binding = SqlLogin("localhost") });   // unticked
        var binding = SqlLogin("localhost");
        binding.TrustServerCertificate = true;
        _sql.ActiveSession = new SqlSessionState("new-tab") { Binding = binding, NeedsReattach = true, CopyPasswordFrom = "source-tab" };

        var cut = await OpenAsync();

        Assert.True(cut.Find("[data-testid=conn-trust-cert]").HasAttribute("checked"));
        cut.WaitForAssertion(() => Assert.Equal("The same as the query tab this one was opened from", Placeholder(cut)));
    }

    [Fact]
    public async Task A_new_PostgreSQL_tabs_dialog_copies_the_other_tabs_password_only_with_its_TLS_mode()
    {
        _sql.EngineCopiesSessionPasswords = true;
        var pg = new ConnectionBinding { Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 5432, Database = "sales", Login = "app", SslMode = "require" };
        _sql.Sessions.Add(new SqlSessionState("source-tab") { IsConnected = true, Binding = pg });
        _sql.ActiveSession = new SqlSessionState("new-tab") { Binding = pg.Clone(), NeedsReattach = true, CopyPasswordFrom = "source-tab" };
        var cut = await OpenAsync();

        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Single(_sql.Tested));
        Assert.Equal("source-tab", _sql.Tested[0].CopyPasswordFrom);

        cut.Find("[data-testid=conn-pg-sslmode]").Change("verify-full");
        cut.Find("[data-testid=conn-test-btn]").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, _sql.Tested.Count));
        Assert.Null(_sql.Tested[1].CopyPasswordFrom);
    }

    // ── Spec 043 follow-up (review, round 4) ─────────────────────────────────────────────────────────────────────────────────

    // [1] Once the user has changed Remember for the form, no answer from the engine ticks it again — until a form is loaded.
    [Fact]
    public async Task Remember_unticked_before_the_trust_choice_is_changed_and_back_is_not_ticked_by_the_new_answer()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        cut.Find("[data-testid=conn-sql-remember]").Change(false);   // the user's choice, before the next question
        int asked = _sql.SqlAsked.Count;

        cut.Find("[data-testid=conn-trust-cert]").Change(true);
        cut.Find("[data-testid=conn-trust-cert]").Change(false);   // and back: asked again, and the engine holds one

        cut.WaitForAssertion(() => Assert.Equal(asked + 2, _sql.SqlAsked.Count));
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Assert.False(RememberTicked(cut));
        Assert.Null(Placeholder(cut));   // the remembered password is not used: it is typed, and a connect forgets the old one
    }

    [Fact]
    public async Task Remember_unticked_before_the_login_is_edited_and_back_is_not_ticked_by_the_new_answer()
    {
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        cut.Find("[data-testid=conn-sql-remember]").Change(false);

        cut.Find("[data-testid=conn-login-input]").Input("reporting");
        cut.Find("[data-testid=conn-login-input]").Change("reporting");
        cut.Find("[data-testid=conn-login-input]").Input("app");
        cut.Find("[data-testid=conn-login-input]").Change("app");

        cut.WaitForAssertion(() => Assert.Equal(("sql01", "app", false), _sql.SqlAsked.Last()));
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        Assert.False(RememberTicked(cut));
    }

    [Fact]
    public async Task A_saved_row_shown_after_Remember_was_unticked_is_a_new_form_that_the_engines_answer_ticks()
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = false,
        };
        await _store.AddAsync(row);
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        cut.Find("[data-testid=conn-sql-remember]").Change(false);

        cut.Find("[data-testid=conn-row-pick]").Click();   // a load: the choice was about the form before it

        cut.WaitForAssertion(() => Assert.True(RememberTicked(cut)));
        Assert.Equal("Remembered on the engine machine", Placeholder(cut));
    }

    // [2] The saved row says the password is remembered when the engine kept it, and only the row's own login changes the flag.
    private async Task<(IRenderedComponent<ConnectionManagerModal> Cut, SavedSqlConnection Row)> OpenOnSavedSqlLoginAsync(bool rememberPassword)
    {
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            RememberPassword = rememberPassword,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.SqlAsked));
        return (cut, row);
    }

    private static void EditLogin(IRenderedComponent<ConnectionManagerModal> cut, string login)
    {
        cut.Find("[data-testid=conn-login-input]").Input(login);
        cut.Find("[data-testid=conn-login-input]").Change(login);
    }

    [Fact]
    public async Task A_password_the_engine_kept_makes_the_saved_row_say_it_is_remembered()
    {
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);   // a v2 engine's word, or a v1 engine asked to keep it
        var (cut, row) = await OpenOnSavedSqlLoginAsync(rememberPassword: false);
        Assert.False(RememberTicked(cut));
        cut.Find("[data-testid=conn-sql-remember]").Change(true);
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.True(Assert.Single(_sql.Connected).RememberPassword);
        Assert.Empty(_notifications.Sent);
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);   // the Status bar restores it; Settings offers Forget password
    }

    [Fact]
    public async Task A_password_kept_on_a_validating_connect_is_not_claimed_for_a_row_that_trusts_a_remote_server()
    {
        // The engine keeps the password under the validating key; the row's own connect (trusting, remote) is given only a
        // password kept on a trusting connect, so marking the row remembered would make its restore ask anyway.
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);
        var row = new SavedSqlConnection
        {
            Name = "Remote", Kind = DatabaseKind.SqlServer, Server = "sql01", Database = "Sales", WindowsAuth = false, Login = "app",
            TrustServerCertificate = true,
        };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = true;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(_sql.SqlAsked));
        cut.Find("[data-testid=conn-trust-cert]").Change(false);
        cut.Find("[data-testid=conn-sql-remember]").Change(true);
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.False(Assert.Single(_sql.Connected).TrustServerCertificate);
        Assert.False((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task A_password_kept_for_another_login_than_the_saved_rows_leaves_the_row_as_it_was()
    {
        _sql.ConnectReply = new ConnectResult(true, Remembered: true);
        var (cut, row) = await OpenOnSavedSqlLoginAsync(rememberPassword: false);
        EditLogin(cut, "reporting");
        cut.Find("[data-testid=conn-sql-remember]").Change(true);
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.Equal("reporting", Assert.Single(_sql.Connected).User);
        Assert.False((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task A_password_not_kept_for_another_login_than_the_saved_rows_does_not_clear_the_rows_flag()
    {
        _sql.ConnectReply = new ConnectResult(true, Remembered: false);   // the engine could not keep the new login's password
        var (cut, row) = await OpenOnSavedSqlLoginAsync(rememberPassword: true);
        EditLogin(cut, "reporting");
        cut.WaitForAssertion(() => Assert.Equal(("sql01", "reporting", false), _sql.SqlAsked.Last()));
        Assert.True(RememberTicked(cut));   // from the row: the user asks to keep the new login's password
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.Contains("could not keep the password for reporting", Assert.Single(_notifications.Sent).Text);
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);   // the row's login (app) keeps its remembered password
    }

    [Fact]
    public async Task Forgetting_another_logins_password_on_connect_does_not_clear_the_rows_flag()
    {
        _sql.SqlRemembered = true;   // for any login, in this fake
        var (cut, row) = await OpenOnSavedSqlLoginAsync(rememberPassword: true);
        EditLogin(cut, "reporting");
        cut.WaitForElement("[data-testid=conn-sql-forget]");
        cut.Find("[data-testid=conn-sql-remember]").Change(false);   // forget the one remembered for reporting
        Type(cut, "conn-password-input", "s3cret");

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-manager]")));
        Assert.Equal(("sql01", "reporting"), Assert.Single(_sql.SqlForgets));
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    [Fact]
    public async Task Forget_it_for_another_login_than_the_saved_rows_does_not_clear_the_rows_flag()
    {
        _sql.SqlRemembered = true;
        var (cut, row) = await OpenOnSavedSqlLoginAsync(rememberPassword: true);
        EditLogin(cut, "reporting");
        cut.WaitForElement("[data-testid=conn-sql-forget]");

        cut.Find("[data-testid=conn-sql-forget]").Click();

        cut.WaitForAssertion(() => Assert.Equal("The remembered password was forgotten.", cut.Find("[data-testid=conn-message]").TextContent.Trim()));
        Assert.Equal(("sql01", "reporting"), Assert.Single(_sql.SqlForgets));
        Assert.True((await _store.GetAsync(row.Id))!.RememberPassword);
    }

    // [3] A refused remembered password marks the tab before the dialog words its message; the message still says what happened.
    [Fact]
    public async Task A_remembered_SQL_password_the_server_refuses_on_connect_says_so_although_the_refusal_marked_the_tab()
    {
        var tab = new SqlSessionState("fresh");
        _sql.ActiveSession = tab;
        _sql.ConnectReply = new ConnectResult(false, "Login failed for user 'app'.", ErrorCode: AkmlSql.Core.Ipc.Messages.DialectErrors.LoginFailed);
        var cut = await OpenSqlLoginAsync(remembered: true);
        cut.WaitForAssertion(() => Assert.Equal("Remembered on the engine machine", Placeholder(cut)));

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("remembered password was not accepted", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.True(string.IsNullOrEmpty(Assert.Single(_sql.Connected).Password));
        Assert.Equal((true, true), (tab.PasswordMissing, tab.PasswordRefused));   // marked as the service marks it
        cut.WaitForElement("[data-testid=conn-sql-refused-hint]");               // and the form now asks for the current one
        Assert.Null(Placeholder(cut));
    }

    [Fact]
    public async Task A_remembered_PostgreSQL_password_the_server_refuses_on_connect_says_so_although_the_refusal_marked_the_tab()
    {
        var tab = new SqlSessionState("fresh-pg");
        _sql.ActiveSession = tab;
        _sql.ConnectReply = new ConnectResult(false, "la authentification par mot de passe a échoué",
            ErrorCode: AkmlSql.Core.Ipc.Messages.DialectErrors.PgLoginFailed);
        var cut = await OpenWithRememberedAsync();

        cut.Find("[data-testid=conn-connect-btn]").Click();

        cut.WaitForAssertion(() => Assert.Contains("remembered password was not accepted", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Equal((true, true), (tab.PasswordMissing, tab.PasswordRefused));
        cut.WaitForElement("[data-testid=conn-pg-refused-hint]");
        Assert.Null(Placeholder(cut, "conn-pg-password"));
    }

    // [4] The tab a new tab was opened from must still be connected for its password to be offered: the engine copies only then.
    [Fact]
    public async Task A_new_tabs_dialog_does_not_offer_the_password_of_a_tab_whose_connection_was_lost()
    {
        _sql.EngineCopiesSessionPasswords = true;
        var tab = NewQueryTab();
        _sql.Sessions.Single(s => s.SessionId == "source-tab").IsConnected = false;   // as MarkConnectionLost leaves it: binding kept
        _sql.ActiveSession = tab;

        var cut = await OpenAsync();

        Assert.Equal("sql01", cut.Find("[data-testid=conn-server-input]").GetAttribute("value"));
        Assert.Null(Placeholder(cut));
        Assert.Empty(_sql.Listed);
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the SQL password to connect", cut.Find("[data-testid=conn-message]").TextContent));
        Assert.Empty(_sql.Connected);

        Type(cut, "conn-password-input", "typed");
        cut.Find("[data-testid=conn-connect-btn]").Click();
        cut.WaitForAssertion(() => Assert.Single(_sql.Connected));
        Assert.Null(_sql.Connected.Single().CopyPasswordFrom);
    }

    [Fact]
    public async Task A_new_PostgreSQL_tabs_dialog_does_not_copy_from_a_tab_whose_connection_was_lost()
    {
        _sql.EngineCopiesSessionPasswords = true;
        var pg = new ConnectionBinding { Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 5432, Database = "sales", Login = "app" };
        _sql.Sessions.Add(new SqlSessionState("source-tab") { IsConnected = false, Binding = pg });
        _sql.ActiveSession = new SqlSessionState("new-tab") { Binding = pg.Clone(), NeedsReattach = true, CopyPasswordFrom = "source-tab" };
        var cut = await OpenAsync();

        cut.Find("[data-testid=conn-test-btn]").Click();

        cut.WaitForAssertion(() => Assert.Single(_sql.Tested));
        Assert.Null(_sql.Tested[0].CopyPasswordFrom);
    }
}

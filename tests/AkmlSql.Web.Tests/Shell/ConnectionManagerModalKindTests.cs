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

        public Task<ConnectResult> ConnectAsync(ConnectionTarget target, CancellationToken ct)
        {
            Connected.Add(target);
            return Task.FromResult(ConnectReply);
        }

        /// <summary>Per host, when a test needs the answers to arrive in a given order; else <see cref="Remembered"/>.</summary>
        public Func<string, Task<bool>>? RememberedFor { get; set; }

        public Task<bool> HasRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) =>
            RememberedFor?.Invoke(host) ?? Task.FromResult(Remembered);

        public Task<bool> ForgetRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct)
        {
            Forgets++;
            var had = Remembered;
            Remembered = false;
            return Task.FromResult(had);
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
    private readonly ConnectionManagerController _controller = new();
    private readonly SavedSqlConnectionStore _store = new(new InMemoryIndexedDbAdapter());

    public ConnectionManagerModalKindTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(_store);
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(_dialogs);
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
}

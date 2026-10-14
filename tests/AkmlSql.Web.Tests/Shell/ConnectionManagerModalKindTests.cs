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

        public Task<bool> HasRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) => Task.FromResult(Remembered);

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

    [Fact]
    public async Task A_new_connection_starts_as_SQL_Server_and_both_choices_are_buttons()
    {
        var cut = await OpenAsync();

        Assert.Equal("true", cut.Find("[data-testid=conn-kind-sqlserver]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=conn-kind-postgresql]").GetAttribute("aria-pressed"));
        Assert.Contains("Connect to SQL Server", cut.Find("[data-testid=conn-title]").TextContent);
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
        Assert.Contains("Connect to PostgreSQL", cut.Find("[data-testid=conn-title]").TextContent);
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

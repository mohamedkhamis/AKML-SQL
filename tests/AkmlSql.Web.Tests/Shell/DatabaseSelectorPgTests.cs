using AkmlSql.Core;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T077, FR-009, FR-031): the database selector on a PostgreSQL session asks before switching, because a switch
/// is a reconnect; with a transaction open it says the transaction will be rolled back, never committed; cancelling leaves the
/// database as it was.
/// </summary>
public sealed class DatabaseSelectorPgTests : BunitContext
{
    private sealed class FakeSql : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected => true;
        public string? Server => "db1:5432";
        public string? Database { get; set; } = "northwind";
        public DatabaseKind Kind { get; set; } = DatabaseKind.PostgreSql;
        public TransactionState TransactionState { get; set; }
        public bool CanChangeDatabase => true;
        public event Action? StateChanged;
        public List<string> Switches { get; } = new();
        public bool Reset { get; set; } = true;

        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesForSessionAsync(CancellationToken ct) =>
            Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, new[] { "northwind", "sales", "postgres" }, null));

        public Task<(bool Ok, string? Error, bool ConnectionWasReset)> ChangeDatabaseAsync(string database, CancellationToken ct)
        {
            Switches.Add(database);
            Database = database;
            TransactionState = TransactionState.None;
            StateChanged?.Invoke();
            return Task.FromResult<(bool, string?, bool)>((true, null, Reset));
        }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly FakeSql _sql = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNotificationService _notes = new();

    public DatabaseSelectorPgTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton<INotificationService>(_notes);
    }

    private IRenderedComponent<DatabaseSelectorComponent> Open()
    {
        var cut = Render<DatabaseSelectorComponent>();
        cut.Find("[data-testid='db-selector']").Click();
        cut.WaitForElement("[data-testid='db-selector-list']");
        return cut;
    }

    private static void Pick(IRenderedComponent<DatabaseSelectorComponent> cut, string name) =>
        cut.FindAll("[data-testid='db-selector-list'] [role='menuitem'], [data-testid='db-selector-list'] [role='menuitemcheckbox'], [data-testid='db-selector-list'] button")
            .First(e => e.TextContent.Trim() == name).Click();

    [Fact]
    public void A_switch_asks_first_and_says_it_reconnects()
    {
        var cut = Open();

        Pick(cut, "sales");

        cut.WaitForAssertion(() => Assert.Single(_dialogs.Confirms));
        var asked = _dialogs.Confirms.Single();
        Assert.Equal("db-switch-confirm", asked.DialogId);
        Assert.Equal("Switch to sales?", asked.Title);
        Assert.Contains("reconnects", asked.Message);
        Assert.Contains("Temporary tables and session settings are lost", asked.Message);
        Assert.False(asked.Destructive);
        Assert.Equal(new[] { "sales" }, _sql.Switches);
    }

    [Theory]
    [InlineData(TransactionState.Open, "Open transaction", "has an open transaction")]
    [InlineData(TransactionState.Aborted, "Aborted transaction", "an error aborted")]
    public void With_a_transaction_open_the_question_is_about_rolling_it_back(TransactionState state, string title, string wording)
    {
        _sql.TransactionState = state;
        var cut = Open();

        Pick(cut, "sales");

        cut.WaitForAssertion(() => Assert.Single(_dialogs.Confirms));
        var asked = _dialogs.Confirms.Single();
        Assert.Equal("db-switch-confirm", asked.DialogId);
        Assert.Equal(title, asked.Title);
        Assert.Contains(wording, asked.Message);
        Assert.Contains("reconnects and rolls it back. Nothing is committed.", asked.Message);
        Assert.Equal("Roll back and switch", asked.ConfirmLabel);
        Assert.True(asked.Destructive);
    }

    [Fact]
    public void Cancelling_leaves_the_database_and_the_transaction_as_they_were()
    {
        _sql.TransactionState = TransactionState.Open;
        _dialogs.ConfirmAnswer = _ => false;
        var cut = Open();

        Pick(cut, "sales");

        cut.WaitForAssertion(() => Assert.Single(_dialogs.Confirms));
        Assert.Empty(_sql.Switches);
        Assert.Equal("northwind", _sql.Database);
        Assert.Equal(TransactionState.Open, _sql.TransactionState);
        Assert.Contains("northwind", cut.Find("[data-testid='db-selector']").TextContent);
    }

    [Fact]
    public void After_a_switch_the_notice_names_what_a_reconnect_lost_in_PostgreSQL_terms()
    {
        var cut = Open();

        Pick(cut, "sales");

        cut.WaitForAssertion(() => Assert.Single(_notes.Sent));
        var (text, kind) = _notes.Sent.Single();
        Assert.Contains("Switched to sales", text);
        Assert.Contains("temporary tables and session settings were lost", text);
        Assert.DoesNotContain("#temp", text);
        Assert.Equal(NotificationKind.Warning, kind);
    }

    [Fact]
    public void The_tooltip_names_the_reconnect()
    {
        var cut = Render<DatabaseSelectorComponent>();

        Assert.Contains("PostgreSQL reconnects", cut.Find("[data-testid='db-selector']").GetAttribute("title"));
    }

    [Fact]
    public void A_SQL_Server_switch_is_not_questioned()
    {
        _sql.Kind = DatabaseKind.SqlServer;
        var cut = Open();

        Pick(cut, "sales");

        cut.WaitForAssertion(() => Assert.Equal(new[] { "sales" }, _sql.Switches));
        Assert.Empty(_dialogs.Confirms);
    }
}

using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 041 (T182, research R28, FR-082) — the toolbar's database selector: the list loads when it is opened, the
/// current database is checked (also when the list does not have it), it is disabled without the engine
/// capability, without a connection or while a query runs, it shows a busy state and an error in place, and
/// picking a database switches to it.
/// </summary>
public sealed class DatabaseSelectorComponentTests : IDisposable
{
    private sealed class FakeSql : ISqlConnectionService
    {
        public string SessionId => "s";
        public bool IsConnected { get; set; } = true;
        public string? Server => "localhost";
        public string? Database { get; set; } = "Sales";
        public event Action? StateChanged;
        public bool CanChangeDatabase { get; set; } = true;
        public int ListCalls { get; private set; }
        public List<string> Switches { get; } = new();
        public (bool, IReadOnlyList<string>, string?) ListReply { get; set; } = (true, new[] { "master", "tempdb" }, null);
        public TaskCompletionSource? ListGate { get; set; }

        public async Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesForSessionAsync(CancellationToken ct)
        {
            ListCalls++;
            if (ListGate != null) await ListGate.Task;
            return ListReply;
        }

        public Task<(bool Ok, string? Error, bool ConnectionWasReset)> ChangeDatabaseAsync(string database, CancellationToken ct)
        {
            Switches.Add(database);
            Database = database;
            StateChanged?.Invoke();
            return Task.FromResult<(bool, string?, bool)>((true, null, false));
        }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly BunitContext _ctx = new();
    private readonly FakeSql _sql = new();
    private readonly FakeNotificationService _notes = new();

    public DatabaseSelectorComponentTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton<ISqlConnectionService>(_sql);
        _ctx.Services.AddSingleton<INotificationService>(_notes);
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<DatabaseSelectorComponent> Render(bool running = false) =>
        _ctx.Render<DatabaseSelectorComponent>(p => p.Add(x => x.IsRunning, running));

    [Fact]
    public void It_shows_the_current_database_and_loads_the_list_only_when_opened()
    {
        var cut = Render();

        Assert.Contains("Sales", cut.Find("[data-testid=db-selector]").TextContent);
        Assert.Equal(0, _sql.ListCalls);

        cut.Find("[data-testid=db-selector]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=db-selector-list]")));
        Assert.Equal(1, _sql.ListCalls);
    }

    [Fact]
    public void The_current_database_is_checked_even_when_the_list_does_not_have_it()
    {
        var cut = Render();

        cut.Find("[data-testid=db-selector]").Click();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-testid=db-selector-list] [role=menuitemcheckbox]");
            Assert.Equal(new[] { "Sales", "master", "tempdb" }, items.Select(i => i.TextContent.Trim()));
            Assert.Equal("true", items[0].GetAttribute("aria-checked"));
        });
    }

    [Fact]
    public void Picking_a_database_switches_to_it()
    {
        var cut = Render();
        cut.Find("[data-testid=db-selector]").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=db-selector-list]")));

        cut.FindAll("[data-testid=db-selector-list] [role=menuitemcheckbox]").First(i => i.TextContent.Trim() == "master").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { "master" }, _sql.Switches));
        Assert.Contains("master", cut.Find("[data-testid=db-selector]").TextContent);
    }

    [Theory]
    [InlineData(false, true, false)]   // no capability
    [InlineData(true, false, false)]   // not connected
    [InlineData(true, true, true)]     // a query is running
    public void It_is_disabled_without_the_capability_a_connection_or_while_a_query_runs(bool capability, bool connected, bool running)
    {
        _sql.CanChangeDatabase = capability && connected;
        _sql.IsConnected = connected;

        var cut = Render(running);

        var button = cut.Find("[data-testid=db-selector]");
        Assert.True(button.HasAttribute("disabled"));
        Assert.False(string.IsNullOrEmpty(button.GetAttribute("title")));
    }

    [Fact]
    public void Loading_shows_a_busy_state_and_a_failure_shows_in_place()
    {
        _sql.ListGate = new TaskCompletionSource();
        _sql.ListReply = (false, Array.Empty<string>(), "Login failed.");
        var cut = Render();

        cut.Find("[data-testid=db-selector]").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=db-selector-busy]")));

        _sql.ListGate.SetResult();

        cut.WaitForAssertion(() => Assert.Equal("Login failed.", cut.Find("[data-testid=db-selector-error]").TextContent.Trim()));
        Assert.Empty(cut.FindAll("[data-testid=db-selector-busy]"));
    }
}

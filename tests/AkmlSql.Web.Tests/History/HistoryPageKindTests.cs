using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using AkmlSql.Web.Tests.Settings;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HistoryPage = AkmlSql.Web.Pages.History;

namespace AkmlSql.Web.Tests.History;

/// <summary>
/// Spec 042 (task T112, FR-070, FR-071, SC-009). The History page shows the kind of every entry, filters by it from a control
/// and from a <c>kind:</c> term, and on restore points to the saved connection the entry ran on. A page with nothing of
/// PostgreSQL in it, on an engine that has no PostgreSQL, looks as it did.
/// </summary>
public sealed class HistoryPageKindTests : BunitContext
{
    private sealed class NoDownloads : IFileDownloader
    {
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom) => Task.CompletedTask;
    }

    private sealed class NoopApplier : IThemeApplier
    {
        public Task ApplyAsync(ThemeMode mode) => Task.CompletedTask;
        public Task ForgetAsync() => Task.CompletedTask;
    }

    private sealed class FakeHistory : IHistoryService
    {
        public List<HistoryEntryDto> Entries { get; } = new()
        {
            new HistoryEntryDto { Id = 1, SqlText = "SELECT 1", TabTitle = "sql-run", Server = "srv", Database = "db", ExecutedAt = "2026-10-09T10:00:00Z", Kind = 0 },
            new HistoryEntryDto { Id = 2, SqlText = "SELECT now()", TabTitle = "pg-run", Server = "pg1:5432", Database = "northwind", ExecutedAt = "2026-10-08T10:00:00Z", Kind = 1 },
        };

        public List<HistorySearchRequest> Searches { get; } = new();
        public bool IsAvailable => true;

        public Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct)
        {
            Searches.Add(request);
            var wanted = Entries.Where(e => request.Kind == null || e.Kind == request.Kind).ToArray();
            return Task.FromResult(new HistorySearchResponse { Success = true, Entries = wanted, TotalCount = wanted.Length });
        }

        public Task RecordAsync(HistoryRecordRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RenameAsync(long id, string newName, CancellationToken ct) => Task.FromResult(true);
        public Task<int> DeleteAsync(long[] ids, CancellationToken ct) => Task.FromResult(ids.Length);
        public Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct) => Task.FromResult(0);
        public Task<string?> GetFullSqlAsync(long id, CancellationToken ct) => Task.FromResult<string?>(Entries.First(e => e.Id == id).SqlText);
        public Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct) => Task.FromResult(Array.Empty<HistoryVersionDto>());
        public Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct) => Task.FromResult<(string, string)?>(("", ""));
    }

    private readonly FakeHistory _history = new();
    private readonly FakeNotificationService _notes = new();
    private readonly SettingsPageHost.FakeSql _sql = new();
    private readonly SavedSqlConnectionStore _saved;

    public HistoryPageKindTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _saved = new SavedSqlConnectionStore(adapter);
        var bridge = new FakeEngineBridge();
        bridge.SetState(BridgeState.Open);
        Services.AddSingleton<IHistoryService>(_history);
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(_saved);
        Services.AddSingleton<IThemeService>(new ThemeService(adapter, new NoopApplier()));
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IEditorSessionStore>(new EditorSessionStore(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(_notes);
        Services.AddSingleton<IFileDownloader>(new NoDownloads());
        Services.AddSingleton<IQueryTabs, QueryTabs>();   // spec 043: History opens queries in tabs
    }

    // ---- the kind on every entry ---------------------------------------------------------------------------------

    [Fact]
    public void Every_entry_shows_its_kind_once_the_engine_supports_PostgreSQL()
    {
        _sql.EngineSupportsPostgres = true;

        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        var badges = page.FindAll("[data-testid=history-kind]");
        Assert.Equal(new[] { "SQL Server", "PostgreSQL" }, badges.Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal(new[] { "sqlserver", "postgresql" }, badges.Select(b => b.GetAttribute("data-kind")).ToArray());
    }

    [Fact]
    public void A_PostgreSQL_entry_in_the_history_shows_the_kind_even_when_this_engine_does_not_advertise_PostgreSQL()
    {
        _sql.EngineSupportsPostgres = false;

        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        Assert.Equal(2, page.FindAll("[data-testid=history-kind]").Count);
        Assert.NotEmpty(page.FindAll("[data-testid=history-kind-filter]"));
    }

    [Fact]
    public void A_page_with_only_SQL_Server_entries_on_an_engine_without_PostgreSQL_is_as_it_was()
    {
        _sql.EngineSupportsPostgres = false;
        _history.Entries.RemoveAll(e => e.Kind != 0);

        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        Assert.Empty(page.FindAll("[data-testid=history-kind]"));
        Assert.Empty(page.FindAll("[data-testid=history-kind-filter]"));
    }

    // ---- filtering by kind -----------------------------------------------------------------------------------------

    [Fact]
    public void The_kind_filter_offers_all_SQL_Server_and_PostgreSQL()
    {
        _sql.EngineSupportsPostgres = true;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        var options = page.Find("[data-testid=history-kind-filter]").QuerySelectorAll("option");

        Assert.Equal(new[] { "All kinds", "SQL Server", "PostgreSQL" }, options.Select(o => o.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void Choosing_PostgreSQL_asks_for_PostgreSQL_entries_only_and_All_asks_for_both()
    {
        _sql.EngineSupportsPostgres = true;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        page.Find("[data-testid=history-kind-filter]").Change("1");

        page.WaitForAssertion(() => Assert.Equal(1, _history.Searches.Last().Kind));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=history-row]")));
        Assert.Equal("PostgreSQL", page.Find("[data-testid=history-kind]").TextContent.Trim());

        page.Find("[data-testid=history-kind-filter]").Change("");

        page.WaitForAssertion(() => Assert.Null(_history.Searches.Last().Kind));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid=history-row]").Count));
    }

    [Fact]
    public void A_kind_term_in_the_search_is_sent_as_the_kind_and_not_as_text()
    {
        _sql.EngineSupportsPostgres = true;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        page.Find("[data-testid=history-search]").Input("now kind:pg");
        page.Find("[data-testid=history-search]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        page.WaitForAssertion(() =>
        {
            var last = _history.Searches.Last();
            Assert.Equal("now", last.SearchText);
            Assert.Equal(1, last.Kind);
        });
    }

    [Fact]
    public void A_search_without_a_kind_term_sends_the_text_as_typed_and_no_kind()
    {
        _sql.EngineSupportsPostgres = true;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        page.Find("[data-testid=history-search]").Input("orders");
        page.Find("[data-testid=history-search]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        page.WaitForAssertion(() =>
        {
            var last = _history.Searches.Last();
            Assert.Equal("orders", last.SearchText);
            Assert.Null(last.Kind);
        });
    }

    // ---- restore: the saved connection ---------------------------------------------------------------------------

    private void OpenEntry(IRenderedComponent<HistoryPage> page, int index)
    {
        page.FindAll("[data-testid=history-row]")[index].Click();
        page.WaitForElement("[data-testid=history-open]");
    }

    [Fact]
    public async Task Opening_a_PostgreSQL_entry_on_a_SQL_Server_connection_suggests_the_saved_PostgreSQL_connection()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = true;
        _sql.Kind = DatabaseKind.SqlServer;
        _sql.Server = "srv";
        _sql.Database = "db";
        await _saved.AddAsync(new SavedSqlConnection { Name = "Local PG", Kind = DatabaseKind.PostgreSql, Server = "pg1", Port = 5432, Database = "northwind", Login = "alice", WindowsAuth = false });
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 1);

        page.Find("[data-testid=history-open]").Click();

        page.WaitForAssertion(() =>
        {
            var note = Assert.Single(_notes.Sent);
            Assert.Contains("PostgreSQL", note.Text);
            Assert.Contains("Local PG", note.Text);
        });
    }

    [Fact]
    public async Task Opening_an_entry_that_ran_on_the_active_connection_says_nothing()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = true;
        _sql.Kind = DatabaseKind.PostgreSql;
        _sql.Server = "pg1:5432";
        _sql.Database = "northwind";
        await _saved.AddAsync(new SavedSqlConnection { Name = "Local PG", Kind = DatabaseKind.PostgreSql, Server = "pg1", Port = 5432, Database = "northwind", Login = "alice", WindowsAuth = false });
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 1);

        page.Find("[data-testid=history-open]").Click();

        page.WaitForAssertion(() => Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Empty(_notes.Sent);
    }

    [Fact]
    public void A_PostgreSQL_entry_with_no_saved_connection_still_says_which_kind_it_ran_on()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = true;
        _sql.Kind = DatabaseKind.SqlServer;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 1);

        page.Find("[data-testid=history-open]").Click();

        page.WaitForAssertion(() =>
        {
            var note = Assert.Single(_notes.Sent);
            Assert.Contains("PostgreSQL", note.Text);
            Assert.Contains("pg1:5432", note.Text);
        });
    }

    [Fact]
    public async Task Re_executing_a_PostgreSQL_entry_on_a_SQL_Server_connection_opens_it_without_running_it()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = true;
        _sql.Kind = DatabaseKind.SqlServer;
        await _saved.AddAsync(new SavedSqlConnection { Name = "Local PG", Kind = DatabaseKind.PostgreSql, Server = "pg1", Port = 5432, Database = "northwind", Login = "alice", WindowsAuth = false });
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 1);

        page.Find("[data-testid=history-reexecute]").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(_notes.Sent));
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.DoesNotContain("autorun", uri);
        Assert.Contains("not run", _notes.Sent[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Re_executing_an_entry_of_the_same_kind_runs_it_as_before()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = true;
        _sql.Kind = DatabaseKind.SqlServer;
        _sql.Server = "srv";
        _sql.Database = "db";
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 0);

        page.Find("[data-testid=history-reexecute]").Click();

        // Spec 043 (R11): the run flag is the new tab's; the editor runs it once that tab is connected.
        var tabs = Services.GetRequiredService<IQueryTabs>();
        page.WaitForAssertion(() => Assert.True(tabs.Active?.AutorunPending));
        Assert.Equal((int)DatabaseKind.SqlServer, tabs.Active!.AutorunKind);
        Assert.DoesNotContain("autorun", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Empty(_notes.Sent);
    }

    [Fact]
    public void Re_executing_while_disconnected_carries_the_entrys_kind_so_the_editor_never_runs_it_on_the_other_kind()
    {
        _sql.EngineSupportsPostgres = true;
        _sql.IsConnected = false;
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        OpenEntry(page, 1);   // the PostgreSQL entry

        page.Find("[data-testid=history-reexecute]").Click();

        var tabs = Services.GetRequiredService<IQueryTabs>();
        page.WaitForAssertion(() => Assert.True(tabs.Active?.AutorunPending));
        Assert.Equal((int)DatabaseKind.PostgreSql, tabs.Active!.AutorunKind);
        Assert.DoesNotContain("autorun", Services.GetRequiredService<NavigationManager>().Uri);   // contracts/web-ui.md §6
    }
}

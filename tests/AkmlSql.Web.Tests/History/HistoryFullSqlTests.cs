using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using AkmlSql.Web.Tests.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HistoryPage = AkmlSql.Web.Pages.History;

namespace AkmlSql.Web.Tests.History;

/// <summary>
/// Re-review REV-02 (from the reviewer's acceptance probe, assertion kept): a history search returns only the first 500
/// characters of each query. When the full text cannot be loaded, that excerpt may be shown, marked as an excerpt — but it
/// must never be copied, opened or re-executed as the query. A retry that loads the full text enables them again; a version
/// failing to load never costs the full text.
/// </summary>
public sealed class HistoryFullSqlTests : BunitContext
{
    private const string Excerpt = "SELECT 1; ";   // the end of a 500-character excerpt
    private const string Full = "SELECT 1; -- ... the rest of the query, WHERE and ROLLBACK included";

    private sealed class NoThemeApply : IThemeApplier
    {
        public Task ApplyAsync(ThemeMode mode) => Task.CompletedTask;
        public Task ForgetAsync() => Task.CompletedTask;
    }

    private sealed class NoDownload : IFileDownloader
    {
        public Task DownloadTextAsync(string name, string mime, string text, bool bom) => Task.CompletedTask;
    }

    private sealed class FlakyHistoryDetails : IHistoryService
    {
        /// <summary>What GetFullSql does: answer null (the default), throw, or return the full text.</summary>
        public Func<string?> FullSql { get; set; } = () => null;
        public Func<HistoryVersionDto[]> Versions { get; set; } = Array.Empty<HistoryVersionDto>;

        public bool IsAvailable => true;
        public Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct) => Task.FromResult(new HistorySearchResponse
        {
            Success = true, TotalCount = 1, Entries = new[] { new HistoryEntryDto {
                Id = 1, TabTitle = "large-query", SqlText = new string(' ', 490) + Excerpt,
                Server = "srv", Database = "db", ExecutedAt = "2026-10-17T10:00:00Z" } },
        });
        public Task<string?> GetFullSqlAsync(long id, CancellationToken ct) => Task.FromResult(FullSql());
        public Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct) => Task.FromResult(Versions());
        public Task RecordAsync(HistoryRecordRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RenameAsync(long id, string name, CancellationToken ct) => Task.FromResult(true);
        public Task<int> DeleteAsync(long[] ids, CancellationToken ct) => Task.FromResult(0);
        public Task<int> RemoveOlderThanAsync(long id, bool keep, CancellationToken ct) => Task.FromResult(0);
        public Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct) => Task.FromResult<(string, string)?>(null);
    }

    private readonly FlakyHistoryDetails _history = new();

    public HistoryFullSqlTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        var bridge = new FakeEngineBridge();
        bridge.SetState(BridgeState.Open);
        Services.AddSingleton<IHistoryService>(_history);
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISqlConnectionService>(new SettingsPageHost.FakeSql());
        Services.AddSingleton<IThemeService>(new ThemeService(adapter, new NoThemeApply()));
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<IEditorSessionStore>(new EditorSessionStore(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
        Services.AddSingleton<IFileDownloader>(new NoDownload());
        Services.AddSingleton<IQueryTabs, QueryTabs>();   // spec 043: History opens queries in tabs
    }

    private IRenderedComponent<HistoryPage> OpenEntry()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]").Click();
        page.WaitForAssertion(() => Assert.Contains("SELECT 1;", page.Find("[data-testid=history-preview]").TextContent));
        return page;
    }

    private static void ActionsDisabled(IRenderedComponent<HistoryPage> page, bool disabled)
    {
        foreach (var id in new[] { "history-reexecute", "history-open", "history-copy" })
            Assert.Equal(disabled, page.Find($"[data-testid={id}]").HasAttribute("disabled"));
    }

    [Fact]
    public void Missing_full_history_SQL_must_not_enable_reexecute_of_search_excerpt()
    {
        var page = OpenEntry();

        Assert.True(page.Find("[data-testid=history-reexecute]").HasAttribute("disabled"),
            "Full SQL failed to load; the 500-character search excerpt must not be executable.");
        ActionsDisabled(page, true);
        Assert.Contains("first 500 characters", page.Find("[data-testid=history-excerpt-notice]").TextContent);
    }

    [Fact]
    public void A_full_SQL_request_that_throws_is_treated_as_missing()
    {
        _history.FullSql = () => throw new InvalidOperationException("bridge dropped");

        var page = OpenEntry();

        ActionsDisabled(page, true);
    }

    [Fact]
    public void Retry_loads_the_full_text_and_enables_the_actions()
    {
        var page = OpenEntry();
        _history.FullSql = () => Full;

        page.Find("[data-testid=history-detail-retry]").Click();

        page.WaitForAssertion(() => Assert.Contains("ROLLBACK included", page.Find("[data-testid=history-preview]").TextContent));
        ActionsDisabled(page, false);
        Assert.Empty(page.FindAll("[data-testid=history-excerpt-notice]"));
    }

    [Fact]
    public void Versions_failing_to_load_never_cost_the_full_text()
    {
        _history.FullSql = () => Full;
        _history.Versions = () => throw new InvalidOperationException("versions unavailable");

        var page = OpenEntry();

        Assert.Contains("ROLLBACK included", page.Find("[data-testid=history-preview]").TextContent);
        ActionsDisabled(page, false);
    }

    [Fact]
    public void A_stored_version_is_a_complete_query_even_when_the_entry_text_did_not_load()
    {
        _history.Versions = () => new[] { new HistoryVersionDto { SqlText = Full, SavedAt = "2026-10-17T10:00:00Z" } };
        var page = OpenEntry();
        ActionsDisabled(page, true);

        page.Find("[data-testid=history-version]").Click();

        ActionsDisabled(page, false);
    }
}

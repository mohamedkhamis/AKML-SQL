using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using AkmlSql.Web.Tests.Settings;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HistoryPage = AkmlSql.Web.Pages.History;

namespace AkmlSql.Web.Tests.History;

/// <summary>
/// Review findings on SQL History:
/// <list type="bullet">
///   <item>WEB-16 — the preview, the versions and the actions belong to the selected entry; a late answer for an earlier
///   selection is dropped, and Open / Re-execute wait until the entry's SQL is there.</item>
///   <item>WEB-17 — a filter changed while a search is loading replaces it; the earlier search's rows never show under the
///   new filter.</item>
///   <item>WEB-13 — entries, group headers, favorites and versions are native controls reachable by keyboard.</item>
/// </list>
/// Responses are released by hand, in any order, through gates.
/// </summary>
public sealed class HistoryRaceAndKeyboardTests : BunitContext
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

    private sealed class GatedHistory : IHistoryService
    {
        public List<HistoryEntryDto> Entries { get; } = new()
        {
            new HistoryEntryDto { Id = 1, SqlText = "SELECT 'A'", TabTitle = "entry-a", Server = "srv", Database = "db", ExecutedAt = "2026-10-09T10:00:00Z" },
            new HistoryEntryDto { Id = 2, SqlText = "SELECT 'B'", TabTitle = "entry-b", Server = "srv", Database = "db", ExecutedAt = "2026-10-09T09:00:00Z" },
        };

        /// <summary>When set for an id, its details wait for the gate.</summary>
        public Dictionary<long, TaskCompletionSource> DetailGates { get; } = new();

        /// <summary>When set, searches wait for the gate; each search request is recorded.</summary>
        public Queue<TaskCompletionSource> SearchGates { get; } = new();
        public List<HistorySearchRequest> Searches { get; } = new();

        public bool IsAvailable => true;

        public async Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct)
        {
            Searches.Add(request);
            if (SearchGates.Count > 0) await SearchGates.Dequeue().Task;
            var rows = request.FavoritesOnly ? Entries.Where(e => e.IsFavorite).ToArray() : Entries.ToArray();
            return new HistorySearchResponse { Success = true, Entries = rows, TotalCount = rows.Length };
        }

        public async Task<string?> GetFullSqlAsync(long id, CancellationToken ct)
        {
            if (DetailGates.TryGetValue(id, out var gate)) await gate.Task;
            return Entries.First(e => e.Id == id).SqlText;
        }

        public Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct) =>
            Task.FromResult(new[] { new HistoryVersionDto { SqlText = Entries.First(e => e.Id == id).SqlText, SavedAt = "2026-10-09T10:00:00Z" } });

        public Task RecordAsync(HistoryRecordRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct)
        {
            var e = Entries.First(x => x.Id == id);
            e.IsFavorite = !e.IsFavorite;
            return Task.FromResult(true);
        }
        public Task<bool> RenameAsync(long id, string newName, CancellationToken ct) => Task.FromResult(true);
        public Task<int> DeleteAsync(long[] ids, CancellationToken ct) => Task.FromResult(ids.Length);
        public Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct) => Task.FromResult(0);
        public Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct) => Task.FromResult<(string, string)?>(("a", "b"));
    }

    private readonly GatedHistory _history = new();

    public HistoryRaceAndKeyboardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        var bridge = new FakeEngineBridge();
        bridge.SetState(BridgeState.Open);
        Services.AddSingleton<IHistoryService>(_history);
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISqlConnectionService>(new SettingsPageHost.FakeSql());
        Services.AddSingleton<IThemeService>(new ThemeService(adapter, new NoopApplier()));
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IEditorSessionStore>(new EditorSessionStore(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
        Services.AddSingleton<IFileDownloader>(new NoDownloads());
        Services.AddSingleton<EditorDocumentFlow>();
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<HistoryPage> page, string title) =>
        page.FindAll("[data-testid=history-row]").First(r => r.TextContent.Contains(title));

    private static string Preview(IRenderedComponent<HistoryPage> page) => page.Find("[data-testid=history-preview]").TextContent;

    // ── WEB-16 ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_late_answer_for_the_previous_entry_never_shows_under_the_new_one()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        _history.DetailGates[1] = new TaskCompletionSource();
        _history.DetailGates[2] = new TaskCompletionSource();

        Row(page, "entry-a").Click();
        Row(page, "entry-b").Click();

        // While B loads, nothing of A shows and the actions that use the preview wait.
        Assert.DoesNotContain("'A'", Preview(page));
        Assert.True(page.Find("[data-testid=history-open]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid=history-reexecute]").HasAttribute("disabled"));

        _history.DetailGates[2].SetResult();   // B answers first…
        page.WaitForAssertion(() => Assert.Contains("'B'", Preview(page)));
        _history.DetailGates[1].SetResult();   // …then A, late

        page.WaitForAssertion(() => Assert.Contains("'B'", Preview(page)));
        Assert.DoesNotContain("'A'", Preview(page));
        Assert.False(page.Find("[data-testid=history-open]").HasAttribute("disabled"));
        Assert.Contains("entry-b", page.Find(".akml-hist-pname").TextContent);
    }

    // ── WEB-17 ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_filter_changed_during_a_search_replaces_it()
    {
        _history.Entries[1].IsFavorite = true;
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        _history.SearchGates.Enqueue(first);
        _history.SearchGates.Enqueue(second);

        var page = Render<HistoryPage>();                        // search 1 (all entries) is pending
        page.Find("[data-testid=history-fav-filter]").Click();   // search 2 (favorites only) replaces it
        Assert.Equal(2, _history.Searches.Count);
        Assert.True(_history.Searches[1].FavoritesOnly);

        second.SetResult();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=history-row]")));
        first.SetResult();                                      // the superseded answer arrives late

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=history-row]")));
        Assert.Contains("entry-b", page.Find("[data-testid=history-row]").TextContent);
    }

    // ── WEB-13 ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Entries_groups_favorites_and_versions_are_native_keyboard_controls_with_one_tab_stop_in_the_list()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");

        var stops = page.FindAll("[data-testid=history-list] [tabindex='0']");
        Assert.Equal("BUTTON", page.Find("[data-testid=history-group]").TagName);
        Assert.All(page.FindAll("[data-testid=history-row-select]"), b => Assert.Equal("BUTTON", b.TagName));
        Assert.All(page.FindAll("[data-testid=history-row-favorite]"), b => Assert.NotNull(b.GetAttribute("aria-pressed")));
        Assert.Single(stops);                                   // the list is one stop until an entry is current

        // Enter on a focused entry is a click on its button, which selects it (a native button does that in the browser).
        Row(page, "entry-a").Click();
        page.WaitForElement("[data-testid=history-version]");
        Assert.Equal("BUTTON", page.Find("[data-testid=history-version]").TagName);
        var current = page.FindAll("[data-testid=history-list] [tabindex='0']");
        Assert.Contains(current, e => e.GetAttribute("data-testid") == "history-row-select");
        Assert.Contains(current, e => e.GetAttribute("data-testid") == "history-row-favorite");
    }

    [Fact]
    public void Arrow_keys_move_the_lists_tab_stop_and_a_group_folds_by_keyboard()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        var list = page.Find("[data-testid=history-list]");

        list.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });     // group header → first entry
        Assert.Contains("entry-a", page.Find("[data-testid=history-row-select][tabindex='0']").TextContent);
        Assert.Equal("-1", page.Find("[data-testid=history-group]").GetAttribute("tabindex"));
        list.KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Contains("entry-b", page.FindAll("[data-testid=history-list] [tabindex='0']")
            .First(e => e.GetAttribute("data-testid") == "history-row-select").TextContent);
        list.KeyDown(new KeyboardEventArgs { Key = "Home" });
        var group = page.Find("[data-testid=history-group]");
        Assert.Equal("0", group.GetAttribute("tabindex"));

        group.Click();                                                 // Enter/Space on the header button
        Assert.Equal("false", page.Find("[data-testid=history-group]").GetAttribute("aria-expanded"));
        Assert.Empty(page.FindAll("[data-testid=history-row]"));
    }
}

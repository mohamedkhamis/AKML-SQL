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
/// Spec 041 (T185, research R59, SC-012) — SQL History renames, deletes and removes older entries through the in-app
/// dialog, never the browser's prompt or confirm; declining changes nothing.
/// </summary>
public sealed class HistoryDialogTests : BunitContext
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
            new HistoryEntryDto { Id = 1, SqlText = "SELECT 1", TabTitle = "orders-audit", Server = "srv", Database = "db", ExecutedAt = "2026-10-09T10:00:00Z" },
            new HistoryEntryDto { Id = 2, SqlText = "SELECT 2", TabTitle = "older", Server = "srv", Database = "db", ExecutedAt = "2026-10-08T10:00:00Z" },
        };
        public List<string> Calls { get; } = new();

        public bool IsAvailable { get; set; } = true;

        public Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct) =>
            Task.FromResult(new HistorySearchResponse { Success = true, Entries = Entries.ToArray(), TotalCount = Entries.Count });

        public Task RecordAsync(HistoryRecordRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> RenameAsync(long id, string newName, CancellationToken ct)
        {
            Calls.Add($"rename {id} {newName}");
            return Task.FromResult(true);
        }

        public Task<int> DeleteAsync(long[] ids, CancellationToken ct)
        {
            Calls.Add($"delete {string.Join(",", ids)}");
            return Task.FromResult(ids.Length);
        }

        public Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct)
        {
            Calls.Add($"remove-older {anchorId} {keepFavorites}");
            return Task.FromResult(1);
        }

        public Task<string?> GetFullSqlAsync(long id, CancellationToken ct) => Task.FromResult<string?>(Entries.First(e => e.Id == id).SqlText);
        public Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct) => Task.FromResult(Array.Empty<HistoryVersionDto>());
        public Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct) => Task.FromResult<(string, string)?>(("SELECT 1", "SELECT 2"));
    }

    private readonly FakeHistory _history = new();
    private readonly FakeDialogService _dialogs = new() { ConfirmAnswer = _ => false, PromptAnswer = _ => null };
    private readonly FakeNotificationService _notes = new();

    public HistoryDialogTests()
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
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton<INotificationService>(_notes);
        Services.AddSingleton<IFileDownloader>(new NoDownloads());
        Services.AddSingleton<EditorDocumentFlow>();
    }

    private IRenderedComponent<HistoryPage> OpenFirstEntry()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]").Click();
        page.WaitForElement("[data-testid=history-rename]");
        return page;
    }

    private void NoBrowserPopUp() =>
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier is "confirm" or "prompt" or "alert");

    [Fact]
    public void Rename_asks_for_the_name_in_the_app()
    {
        var page = OpenFirstEntry();
        _dialogs.PromptAnswer = _ => "  nightly report ";

        page.Find("[data-testid=history-rename]").Click();

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal("Rename history entry", prompt.Title);
        Assert.Equal("Name", prompt.Label);
        Assert.Equal("orders-audit", prompt.InitialValue);
        Assert.Equal("Rename", prompt.ConfirmLabel);
        page.WaitForAssertion(() => Assert.Contains("rename 1 nightly report", _history.Calls));
        NoBrowserPopUp();
    }

    [Fact]
    public void Cancelling_the_rename_changes_nothing()
    {
        var page = OpenFirstEntry();

        page.Find("[data-testid=history-rename]").Click();

        Assert.Single(_dialogs.Prompts);
        Assert.Empty(_history.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_confirms_destructively_first(bool answer)
    {
        var page = OpenFirstEntry();
        _dialogs.ConfirmAnswer = _ => answer;

        page.Find("[data-testid=history-delete]").Click();

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Delete history entry", confirm.Title);
        Assert.Equal("Delete", confirm.ConfirmLabel);
        Assert.True(confirm.Destructive);
        if (answer) page.WaitForAssertion(() => Assert.Equal(new[] { "delete 1" }, _history.Calls));
        else Assert.Empty(_history.Calls);
        NoBrowserPopUp();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Remove_older_confirms_destructively_first_and_keeps_favourites(bool answer)
    {
        var page = OpenFirstEntry();
        _dialogs.ConfirmAnswer = _ => answer;

        page.Find("[data-testid=history-remove-older]").Click();

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Remove older entries", confirm.Title);
        Assert.Contains("Favorites are kept", confirm.Message);
        Assert.Equal("Remove", confirm.ConfirmLabel);
        Assert.True(confirm.Destructive);
        if (answer) page.WaitForAssertion(() => Assert.Equal(new[] { "remove-older 1 True" }, _history.Calls));
        else Assert.Empty(_history.Calls);
        NoBrowserPopUp();
    }

    [Fact]
    public void Compare_opens_the_shared_dialog_and_Close_closes_it()
    {
        var page = Render<HistoryPage>();
        page.WaitForElement("[data-testid=history-row]");
        page.FindAll(".akml-hist-cmp")[0].Change(true);
        page.FindAll(".akml-hist-cmp")[1].Change(true);   // found again: the first change re-rendered the list

        page.Find("[data-testid=history-compare]").Click();

        var dialog = page.WaitForElement("[data-testid=history-compare-dialog]");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Contains("akml-dialog", dialog.ClassList);
        Assert.NotNull(page.Find(".akml-scrim"));

        page.Find("[data-testid=history-compare-close]").Click();
        Assert.Empty(page.FindAll("[data-testid=history-compare-dialog]"));
    }

    // Spec 041 (T201, R5, N14) — Copy works on the http LAN deployment and says whether it did.
    [Theory]
    [InlineData(true, NotificationKind.Success, "Copied")]
    [InlineData(false, NotificationKind.Error, "Copy failed")]
    public void Copy_uses_the_copy_that_works_over_http_and_says_what_happened(bool copied, NotificationKind kind, string text)
    {
        var ui = JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(copied);
        var page = OpenFirstEntry();

        page.Find("[data-testid=history-copy]").Click();

        page.WaitForAssertion(() => Assert.Contains(_notes.Sent, n => n.Kind == kind && n.Text.Contains(text)));
        Assert.Equal("SELECT 1", Assert.Single(ui.Invocations["copyText"]).Arguments[0]);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier.StartsWith("navigator.clipboard", StringComparison.Ordinal));
    }

    // Spec 041 (T203, FR-085) — the one page header every page shares.
    private static void HasThePageHeader(AngleSharp.Dom.IParentNode page, string title)
    {
        var header = page.QuerySelector("[data-testid=page-header]")!;
        Assert.Contains("akml-page-header", header.ClassList);
        var h1 = header.QuerySelector("[data-testid=page-title]")!;
        Assert.Equal("H1", h1.TagName);
        Assert.Contains("akml-page-title", h1.ClassList);
        Assert.Equal(title, h1.TextContent.Trim());
        Assert.Contains("akml-page-actions", header.QuerySelector("[data-testid=page-actions]")!.ClassList);
    }

    [Fact]
    public void History_has_the_shared_page_header_and_its_selected_row_uses_the_shared_style()
    {
        var page = OpenFirstEntry();

        HasThePageHeader(page.Find(".akml-page"), "SQL History");
        // Review WEB-13: the row is not an ARIA option (it holds a favorite button and a checkbox); its select button carries
        // the selection, as aria-current.
        var selected = page.Find("[data-testid=history-row].is-selected");
        Assert.Contains("akml-list-row", selected.ClassList);
        Assert.Equal("true", selected.QuerySelector("[data-testid=history-row-select]")!.GetAttribute("aria-current"));
        Assert.Single(page.FindAll("[data-testid=history-row-select][aria-current=true]"));
    }

    // Spec 041 (T205, R66, FR-088) — empty and unavailable states say what to do next.
    [Fact]
    public void An_empty_history_says_how_it_fills()
    {
        _history.Entries.Clear();

        var page = Render<HistoryPage>();

        var empty = page.WaitForElement("[data-testid=history-empty]");
        Assert.Contains("akml-empty", empty.ClassList);
        Assert.Contains("Run a query", empty.TextContent);
    }

    [Fact]
    public void Without_an_engine_History_offers_retry_and_engine_setup()
    {
        _history.IsAvailable = false;

        var page = Render<HistoryPage>();

        var empty = page.Find("[data-testid=history-unavailable]");
        Assert.Contains("akml-empty", empty.ClassList);
        Assert.NotNull(empty.QuerySelector("[data-testid=history-retry]"));
        Assert.Equal("settings/connections", empty.QuerySelector("a[data-testid=history-setup]")!.GetAttribute("href"));
    }

    // ── PR #255: Open and Re-execute never overwrite a modified document without asking ─────────────────

    [Theory]
    [InlineData("history-open")]
    [InlineData("history-reexecute")]
    public async Task Opening_over_a_modified_document_asks_first_and_Cancel_keeps_it(string button)
    {
        var store = Services.GetRequiredService<IEditorSessionStore>();
        await store.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "edited", IsModified = true, NextDefaultNumber = 3, SessionKey = "k" });
        await store.FlushAsync();
        var page = OpenFirstEntry();
        _dialogs.ConfirmAnswer = _ => false;

        page.Find($"[data-testid={button}]").Click();

        page.WaitForAssertion(() => Assert.Contains("orders-audit", Assert.Single(_dialogs.Confirms).Message));
        var kept = await store.RestoreAsync();
        Assert.Equal("edited", kept!.DocumentText);
        Assert.Equal("orders-audit", kept.DocumentName);
        Assert.DoesNotContain("autorun", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        NoBrowserPopUp();
    }

    [Fact]
    public async Task Opening_an_entry_starts_a_new_default_named_document()
    {
        var store = Services.GetRequiredService<IEditorSessionStore>();
        await store.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 1;", IsModified = false, NextDefaultNumber = 3, SessionKey = "k" });
        await store.FlushAsync();
        var page = OpenFirstEntry();

        page.Find("[data-testid=history-open]").Click();

        await page.InvokeAsync(() => { });
        var opened = await store.RestoreAsync();
        Assert.Equal("SQLQuery3", opened!.DocumentName);
        Assert.False(opened.IsModified);
        Assert.Empty(_dialogs.Confirms);
    }
}

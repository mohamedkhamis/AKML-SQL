using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Re-review REV-04 (from the reviewer's acceptance probe, assertion kept): the snippet fields stay editable while a Save waits
/// for the store, so only what was saved may become the baseline — text typed meanwhile is still a draft and must survive the
/// reload that follows the save. A failed save keeps the draft; a second Save is not started while one is waiting.
/// </summary>
public sealed class SnippetSaveRaceTests
{
    private sealed class DelayedWrite : IIndexedDbAdapter
    {
        private readonly InMemoryIndexedDbAdapter _inner = new();
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started { get; private set; }
        public int Writes { get; private set; }
        public Exception? Fail { get; set; }
        public Task<string?> GetAsync(string store, string key) => _inner.GetAsync(store, key);
        public Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store) => _inner.ListAsync(store);
        public Task DeleteAsync(string store, string key) => _inner.DeleteAsync(store, key);
        public Task ClearAsync(string store) => _inner.ClearAsync(store);
        public async Task SetAsync(string store, string key, string json)
        {
            if (store == StoreNames.Snippets)
            {
                Writes++;
                Started = true;
                await Gate.Task;
                if (Fail != null) throw Fail;
            }
            await _inner.SetAsync(store, key, json);
        }
    }

    private static (BunitContext Context, DelayedWrite Adapter, IRenderedComponent<SnippetsPage> Page) Open()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new DelayedWrite();
        context.Services.AddSingleton<ISnippetStore>(new SnippetStore(adapter));
        context.Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        context.Services.AddSingleton<IDialogService>(new FakeDialogService { ConfirmAnswer = _ => false });
        var page = context.Render<SnippetsPage>();
        page.WaitForElement("[data-testid=snippet-new]").Click();
        page.Find("#akml-snippet-shortcode").Input("review_save_race");
        page.Find("#akml-snippet-body").Input("SELECT 1;");
        return (context, adapter, page);
    }

    private static string Body(IRenderedComponent<SnippetsPage> page) => page.Find("#akml-snippet-body").GetAttribute("value") ?? "";

    [Fact]
    public void Typing_while_snippet_save_waits_must_preserve_the_new_draft()
    {
        var (context, adapter, page) = Open();
        using var _ = context;
        page.Find("[data-testid=snippet-save]").Click();
        // The save has taken its snapshot once it reaches the store; under load the click's handler can still be queued.
        page.WaitForAssertion(() => Assert.True(adapter.Started));
        page.Find("#akml-snippet-body").Input("SELECT 2; -- typed while saving");
        adapter.Gate.SetResult();
        page.WaitForAssertion(() => Assert.Contains("Saved", page.Find("[data-testid=snippet-status]").TextContent));
        page.WaitForAssertion(() => Assert.Equal("SELECT 2; -- typed while saving", page.Find("#akml-snippet-body").GetAttribute("value")));
    }

    [Fact]
    public void The_text_typed_during_the_save_is_still_a_draft_and_the_next_save_updates_the_same_snippet()
    {
        var (context, adapter, page) = Open();
        using var _ = context;
        page.Find("[data-testid=snippet-save]").Click();
        page.WaitForAssertion(() => Assert.True(adapter.Started));   // the snapshot is taken: what follows is typed during the save
        page.Find("#akml-snippet-body").Input("SELECT 2;");
        adapter.Gate.SetResult();
        page.WaitForAssertion(() => Assert.Contains("Saved", page.Find("[data-testid=snippet-status]").TextContent));

        // Still a draft: leaving it asks first.
        var dialogs = (FakeDialogService)context.Services.GetRequiredService<IDialogService>();
        page.Find("[data-testid=snippet-new]").Click();
        page.WaitForAssertion(() => Assert.Single(dialogs.Confirms));
        Assert.Equal("SELECT 2;", Body(page));

        page.WaitForAssertion(() => Assert.False(page.Find("[data-testid=snippet-save]").HasAttribute("disabled")));
        page.Find("[data-testid=snippet-save]").Click();
        page.WaitForAssertion(() => Assert.Equal(2, adapter.Writes));
        // The list reloads after the write: one row for the snippet, not a second copy.
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=snippet-row]").Where(r => r.GetAttribute("data-shortcode") == "review_save_race")));
        Assert.Equal("SELECT 2;", Body(page));
    }

    [Fact]
    public void A_second_Save_waits_for_the_first()
    {
        var (context, adapter, page) = Open();
        using var _ = context;
        page.Find("[data-testid=snippet-save]").Click();

        Assert.True(page.Find("[data-testid=snippet-save]").HasAttribute("disabled"));
        page.Find("[data-testid=snippet-save]").Click();   // a handler that ran anyway is refused
        Assert.Equal(1, adapter.Writes);
        adapter.Gate.SetResult();
    }

    [Fact]
    public void A_failed_save_keeps_the_draft()
    {
        var (context, adapter, page) = Open();
        using var _ = context;
        adapter.Fail = new InvalidOperationException("quota exceeded");
        page.Find("[data-testid=snippet-save]").Click();
        adapter.Gate.SetResult();

        page.WaitForAssertion(() => Assert.Contains("Save failed", page.Find("[data-testid=snippet-status]").TextContent));
        Assert.Equal("SELECT 1;", Body(page));
        var dialogs = (FakeDialogService)context.Services.GetRequiredService<IDialogService>();
        page.Find("[data-testid=snippet-new]").Click();
        page.WaitForAssertion(() => Assert.Single(dialogs.Confirms));  // still a draft; wait for the async click handler
    }
}

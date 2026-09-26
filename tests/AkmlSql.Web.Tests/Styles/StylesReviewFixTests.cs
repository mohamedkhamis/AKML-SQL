using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StylesPage = AkmlSql.Web.Pages.Styles;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// The Format styles page, the editor's style picker and the style store where they used to lose
/// work or show the wrong thing: a click on the open style, a failed "Save as", an import over
/// unsaved edits, an engine that fails to list its styles, and an engine that connects after the
/// editor has already picked its style.
/// </summary>
public sealed class StylesReviewFixTests : BunitContext
{
    private const string Mine = "Mine";

    public StylesReviewFixTests()
    {
        Services.AddSingleton<IFormatterService>(new FormatterService());
        JSInterop.Mode = JSRuntimeMode.Loose;   // confirm() answers false, prompt() null, unless set up
    }

    private ProfileStore BrowserStore()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault(Mine)).GetAwaiter().GetResult();
        Services.AddSingleton<IProfileStore>(store);
        return store;
    }

    private IRenderedComponent<StylesPage> OpenWithEdit(string style)
    {
        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        Row(page, style).Click();
        page.WaitForAssertion(() => Assert.Equal(style, page.Find("[data-testid=style-name]").TextContent));
        page.Find("[data-testid=page-lists]").Click();
        page.Find("[data-testid='option-lists.placeCommasBeforeItems']").Change(true);
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
        return page;
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<StylesPage> page, string style) =>
        page.FindAll("[data-testid=style-row]").First(b => b.GetAttribute("data-style") == style);

    // ── the styles page ─────────────────────────────────────────────────────

    [Fact]
    public void Clicking_the_open_style_keeps_its_unsaved_edits()
    {
        BrowserStore();
        var page = OpenWithEdit(Mine);

        Row(page, Mine).Click();

        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
        Assert.True(page.Find("[data-testid='option-lists.placeCommasBeforeItems']").HasAttribute("checked"));
        Assert.Empty(JSInterop.Invocations.Where(i => i.Identifier == "confirm"));
    }

    [Fact]
    public void A_failed_save_as_keeps_the_edits_unsaved()
    {
        BrowserStore();
        JSInterop.Setup<string?>("prompt", _ => true).SetResult("Khamis Style");   // a name that is taken
        var page = OpenWithEdit(Mine);

        page.Find("[data-testid=style-saveas]").Click();

        page.WaitForAssertion(() => Assert.Contains("already", page.Find("[data-testid=styles-status]").TextContent));
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
        Assert.False(page.Find("[data-testid=style-save]").HasAttribute("disabled"));
        Assert.Equal(Mine, page.Find("[data-testid=style-name]").TextContent);
    }

    [Fact]
    public async Task Importing_over_unsaved_edits_asks_first_and_declining_keeps_them()
    {
        var store = BrowserStore();
        var before = (await store.ListAsync()).Count;
        var page = OpenWithEdit(Mine);

        var json = SqlPromptStyleDocument.CreateDefault("Imported").ToJson();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(json, "imported.json"));

        page.WaitForAssertion(() => Assert.Single(JSInterop.Invocations.Where(i => i.Identifier == "confirm")));
        Assert.Equal(before, (await store.ListAsync()).Count);
        Assert.Equal(Mine, page.Find("[data-testid=style-name]").TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
    }

    [Fact]
    public void Importing_over_unsaved_edits_proceeds_when_confirmed()
    {
        BrowserStore();
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var page = OpenWithEdit(Mine);

        var json = SqlPromptStyleDocument.CreateDefault("Imported").ToJson();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(json, "imported.json"));

        page.WaitForAssertion(() => Assert.Equal("Imported", page.Find("[data-testid=style-name]").TextContent));
    }

    [Fact]
    public void An_engine_that_cannot_list_its_styles_is_reported_and_the_browser_styles_still_show()
    {
        var engine = new FakeStylesEngine { ListFailure = new TimeoutException("busy") };
        Services.AddSingleton<IProfileStore>(new ProfileStore(new InMemoryIndexedDbAdapter(), engine));

        var page = Render<StylesPage>();

        page.WaitForAssertion(() => Assert.Contains("could not be listed", page.Find("[data-testid=styles-engine-error]").TextContent));
        Assert.Contains(page.FindAll("[data-testid=style-row]"), r => r.GetAttribute("data-style") == "Khamis Style");
    }

    // ── the store ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_failing_engine_does_not_take_the_browser_styles_with_it()
    {
        var engine = new FakeStylesEngine { ListFailure = new OperationCanceledException() };
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);

        var list = await store.ListAsync();

        Assert.Contains(list, r => r.Id == "builtin.khamis");
        Assert.DoesNotContain(list, r => r.Location == ProfileLocation.Engine);
        Assert.Equal("The engine did not answer in time.", store.EngineStylesError);

        engine.ListFailure = null;
        Assert.Contains(await store.ListAsync(), r => r.Location == ProfileLocation.Engine);
        Assert.Null(store.EngineStylesError);
    }

    // ── the editor's style picker ───────────────────────────────────────────

    [Fact]
    public async Task When_the_engine_connects_the_editor_gets_the_engine_style_the_picker_now_shows()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        var saved = await store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault("My SSMS style"));
        Assert.Equal(ProfileLocation.Engine, saved.Location);
        await store.SetActiveIdAsync(saved.Id);
        engine.SetState(BridgeState.Connecting);   // a reload: the bridge is still connecting
        Services.AddSingleton<IProfileStore>(store);

        ProfileRecord? handed = null;
        var picker = Render<ProfilePickerComponent>(p => p.Add(x => x.OnProfileChanged, r => handed = r));
        picker.WaitForAssertion(() => Assert.Equal("builtin.khamis", picker.Find("select").GetAttribute("value")));
        Assert.Null(handed);   // the editor loaded that same default itself

        engine.SetState(BridgeState.Open);

        picker.WaitForAssertion(() => Assert.Equal(saved.Id, picker.Find("select").GetAttribute("value")));
        picker.WaitForAssertion(() => Assert.Equal("My SSMS style", handed?.Name));
        Assert.False(handed!.IsSummary);   // the whole style, not the list summary
    }

    // ── second review: names, a failing engine, stale notifications ─────────

    private static async Task<(FakeStylesEngine Engine, ProfileStore Store, ProfileRecord Saved)> EngineWithStyleAsync(string name)
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        var saved = await store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault(name));
        return (engine, store, saved);
    }

    [Fact]
    public async Task A_new_style_is_not_created_on_the_engine_while_its_styles_cannot_be_listed()
    {
        var engine = new FakeStylesEngine { ListFailure = new TimeoutException("busy") };
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault("Team style")));

        Assert.Contains("could not be listed", error.Message);
        Assert.Null(engine.LastSave);   // nothing was sent: the name could not be checked
    }

    [Fact]
    public async Task New_engine_styles_are_created_with_create_only_and_edits_are_not()
    {
        var (engine, store, saved) = await EngineWithStyleAsync("Team style");
        Assert.True(engine.LastSave!.CreateOnly);

        var document = (await store.GetDocumentAsync(saved.Id))!;
        document.Set("lists.placeCommasBeforeItems", "true");
        await store.SaveDocumentAsync(saved.Id, document);
        Assert.False(engine.LastSave!.CreateOnly);
    }

    [Fact]
    public async Task A_listing_failure_keeps_the_engine_styles_last_listed()
    {
        var (engine, store, saved) = await EngineWithStyleAsync("Team style");
        Assert.Contains(await store.ListAsync(), r => r.Id == saved.Id);

        engine.ListFailure = new TimeoutException("busy");
        var list = await store.ListAsync();

        Assert.Contains(list, r => r.Id == saved.Id);
        Assert.NotNull(store.EngineStylesError);
    }

    [Fact]
    public async Task Opening_an_engine_style_asks_the_engine_once()
    {
        var (engine, store, saved) = await EngineWithStyleAsync("Team style");
        var before = engine.Sent.Count(t => t == AkmlSql.Core.Ipc.MessageTypes.ProfileGet);

        var opened = await store.OpenAsync(saved.Id);

        Assert.NotNull(opened);
        Assert.Equal("Team style", opened!.Value.Record.Name);
        Assert.Equal(before + 1, engine.Sent.Count(t => t == AkmlSql.Core.Ipc.MessageTypes.ProfileGet));
    }

    [Fact]
    public async Task The_active_engine_style_stays_put_while_the_engine_cannot_list_its_styles()
    {
        var (engine, store, saved) = await EngineWithStyleAsync("My SSMS style");
        await store.SetActiveIdAsync(saved.Id);
        Services.AddSingleton<IProfileStore>(store);
        ProfileRecord? handed = null;
        var picker = Render<ProfilePickerComponent>(p => p.Add(x => x.OnProfileChanged, r => handed = r));
        picker.WaitForAssertion(() => Assert.Equal(saved.Id, picker.Find("select").GetAttribute("value")));

        engine.ListFailure = new TimeoutException("busy");
        engine.SetState(BridgeState.Open);   // any StylesChanged reload

        picker.WaitForAssertion(() => Assert.Equal(saved.Id, picker.Find("select").GetAttribute("value")));
        Assert.Null(handed);   // the editor keeps the style it has
    }

    [Fact]
    public async Task On_first_load_an_unlisted_active_engine_style_is_shown_not_replaced()
    {
        var (engine, _, saved) = await EngineWithStyleAsync("My SSMS style");
        engine.ListFailure = new TimeoutException("busy");
        var fresh = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);   // no earlier list to fall back on
        await fresh.SetActiveIdAsync(saved.Id);
        Services.AddSingleton<IProfileStore>(fresh);

        ProfileRecord? handed = null;
        var picker = Render<ProfilePickerComponent>(p => p.Add(x => x.OnProfileChanged, r => handed = r));

        picker.WaitForAssertion(() => Assert.Contains("My SSMS style", picker.Find("[data-testid=profile-unlisted]").TextContent));
        Assert.Equal(saved.Id, picker.Find("select").GetAttribute("value"));
        Assert.Null(handed);
    }

    [Fact]
    public async Task A_slow_engine_style_does_not_override_a_newer_choice()
    {
        var (engine, store, saved) = await EngineWithStyleAsync("My SSMS style");
        await store.SetActiveIdAsync(saved.Id);
        engine.SetState(BridgeState.Connecting);
        Services.AddSingleton<IProfileStore>(store);
        ProfileRecord? handed = null;
        var picker = Render<ProfilePickerComponent>(p => p.Add(x => x.OnProfileChanged, r => handed = r));
        picker.WaitForAssertion(() => Assert.Equal("builtin.khamis", picker.Find("select").GetAttribute("value")));

        // The engine connects; reading its style is slow.
        var gate = new TaskCompletionSource();
        engine.GetGate = gate;
        engine.SetState(BridgeState.Open);
        picker.WaitForAssertion(() => Assert.Equal(saved.Id, picker.Find("select").GetAttribute("value")));

        // Meanwhile the user picks another style ...
        picker.Find("select").Change("builtin.collapsed");
        picker.WaitForAssertion(() => Assert.Equal("Collapsed", handed?.Name));

        // ... and the engine's answer arrives late: it must not win.
        engine.GetGate = null;
        gate.SetResult();
        await Task.Delay(200);
        picker.WaitForAssertion(() => Assert.Equal("Collapsed", handed?.Name));
    }

    [Fact]
    public async Task A_created_style_that_cannot_be_opened_keeps_the_edits_unsaved()
    {
        var (engine, store, _) = await EngineWithStyleAsync(Mine);
        Services.AddSingleton<IProfileStore>(store);
        JSInterop.Setup<string?>("prompt", _ => true).SetResult("Copy");
        var page = OpenWithEdit(Mine);

        // The copy is saved and read back, then opening it times out.
        engine.GetFailure = new TimeoutException("slow");
        engine.GetsBeforeFailure = 1;
        page.Find("[data-testid=style-saveas]").Click();

        page.WaitForAssertion(() => Assert.Contains("could not be opened", page.Find("[data-testid=styles-status]").TextContent));
        Assert.Contains("Created", page.Find("[data-testid=styles-status]").TextContent);
        Assert.Equal(Mine, page.Find("[data-testid=style-name]").TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
    }
}

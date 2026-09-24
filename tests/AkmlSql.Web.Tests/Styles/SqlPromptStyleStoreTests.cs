using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// SQL Prompt styles in the web edition: kept in the browser when no engine is paired, and on the
/// paired engine (the styles SSMS and Visual Studio use) when one is.
/// </summary>
public sealed class SqlPromptStyleStoreTests
{
    private static SqlPromptStyleDocument Style(string name, bool commasBefore = true)
    {
        var document = SqlPromptStyleDocument.CreateDefault(name);
        if (commasBefore) document.Set("lists.placeCommasBeforeItems", "true");
        return document;
    }

    // ── browser ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_an_engine_a_new_style_is_saved_in_the_browser_as_a_sql_prompt_style()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        Assert.False(store.EngineStylesAvailable);

        var saved = await store.SaveDocumentAsync(null, Style("Team"));

        Assert.Equal(ProfileLocation.Browser, saved.Location);
        Assert.True(saved.IsSqlPromptStyle);
        var listed = Assert.Single(await store.ListAsync(), r => r.Name == "Team");
        Assert.True(listed.IsSqlPromptStyle);
        var document = await store.GetDocumentAsync(saved.Id);
        Assert.True(document!.GetBool("lists.placeCommasBeforeItems"));
    }

    [Fact]
    public async Task The_saved_style_is_what_the_editor_formats_with()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        var saved = await store.SaveDocumentAsync(null, Style("Team"));
        var record = await store.GetAsync(saved.Id);

        var formatted = new FormatterService().Format("SELECT a, b FROM t;", record!.Profile).FormattedText;
        Assert.Contains("\n       , b", formatted.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Editing_a_built_in_saves_an_edited_copy_and_reset_brings_the_original_back()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        var original = await store.GetDocumentAsync("builtin.collapsed");
        var edited = original!.Clone();
        edited.Set("lists.alignAliases", "true");

        var saved = await store.SaveDocumentAsync("builtin.collapsed", edited);
        Assert.Equal("builtin.collapsed", saved.Id);
        Assert.True(saved.IsCustomizedBuiltIn);
        var listed = (await store.ListAsync()).Single(r => r.Id == "builtin.collapsed");
        Assert.True(listed.IsCustomizedBuiltIn);
        Assert.True((await store.GetDocumentAsync("builtin.collapsed"))!.GetBool("lists.alignAliases"));

        var reset = await store.ResetAsync("builtin.collapsed");
        Assert.False(reset!.IsCustomizedBuiltIn);
        Assert.False(reset.IsSqlPromptStyle);
        Assert.False((await store.GetDocumentAsync("builtin.collapsed"))!.GetBool("lists.alignAliases"));
    }

    [Fact]
    public async Task Rename_changes_the_name_everywhere_and_rejects_duplicates()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        var a = await store.SaveDocumentAsync(null, Style("A"));
        await store.SaveDocumentAsync(null, Style("B"));

        var renamed = await store.RenameAsync(a.Id, "Renamed");
        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal("Renamed", (await store.GetDocumentAsync(a.Id))!.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameAsync(a.Id, "b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameAsync("builtin.khamis", "X"));
    }

    [Fact]
    public async Task A_new_style_cannot_take_an_existing_name()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        await store.SaveDocumentAsync(null, Style("Team"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveDocumentAsync(null, Style("TEAM")));
    }

    [Fact]
    public async Task Classic_styles_open_as_their_sql_prompt_reading()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        var khamis = await store.GetDocumentAsync("builtin.khamis");
        Assert.Equal("uppercase", khamis!.Get("casing.reservedKeywords"));
        Assert.True(khamis.GetBool("lists.placeCommasBeforeItems"));
    }

    // ── engine (shared with SSMS / Visual Studio) ────────────────────────────

    [Fact]
    public async Task With_an_engine_new_styles_are_saved_on_the_engine()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        Assert.True(store.EngineStylesAvailable);

        var saved = await store.SaveDocumentAsync(null, Style("Shared"));

        Assert.Equal(ProfileLocation.Engine, saved.Location);
        Assert.Equal("engine:Shared", saved.Id);
        var stored = engine.Stored("Shared");
        Assert.NotNull(stored!.SqlPrompt);
        Assert.Equal("leading", stored.List.CommaPosition);           // projection refreshed by the engine
        Assert.Contains(await store.ListAsync(), r => r.Id == "engine:Shared" && r.IsSqlPromptStyle);
        Assert.DoesNotContain(await store.ListAsync(), r => r.Location == ProfileLocation.Browser && r.Name == "Shared");
    }

    [Fact]
    public async Task Engine_styles_load_edit_and_save_in_place()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        await store.SaveDocumentAsync(null, Style("Shared"));

        var document = await store.GetDocumentAsync("engine:Shared");
        document!.Set("casing.reservedKeywords", "lowercase");
        await store.SaveDocumentAsync("engine:Shared", document);

        Assert.Equal("lowercase", engine.Stored("Shared")!.SqlPrompt!["casing"]!["reservedKeywords"]!.GetValue<string>());
        var record = await store.GetAsync("engine:Shared");
        Assert.Equal(ProfileLocation.Engine, record!.Location);
        Assert.Equal("select a", new FormatterService().Format("SELECT a;", record.Profile).FormattedText.Split(';')[0].Trim());
    }

    [Fact]
    public async Task Engine_classic_styles_come_back_as_sql_prompt_documents_and_can_be_edited()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);

        var document = await store.GetDocumentAsync("engine:Engine Built-in");
        Assert.NotNull(document);
        document!.Set("lists.alignAliases", "true");
        var saved = await store.SaveDocumentAsync("engine:Engine Built-in", document);

        Assert.True(saved.IsCustomizedBuiltIn);
        Assert.NotNull(engine.Stored("Engine Built-in")!.SqlPrompt);
        await store.ResetAsync("engine:Engine Built-in");
        Assert.Null(engine.Stored("Engine Built-in")!.SqlPrompt);
    }

    [Fact]
    public async Task An_active_engine_style_falls_back_while_the_engine_is_away_and_returns_with_it()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        await store.SaveDocumentAsync(null, Style("Shared"));
        await store.SetActiveIdAsync("engine:Shared");
        Assert.Equal("engine:Shared", await store.GetActiveIdAsync());

        engine.SetState(BridgeState.Disconnected);
        Assert.Equal("builtin.khamis", await store.GetActiveIdAsync());
        Assert.DoesNotContain(await store.ListAsync(), r => r.Location == ProfileLocation.Engine);

        engine.SetState(BridgeState.Open);
        Assert.Equal("engine:Shared", await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task An_engine_without_style_support_is_not_used_for_styles()
    {
        var engine = new FakeStylesEngine(advertiseStyles: false);
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        var saved = await store.SaveDocumentAsync(null, Style("Local"));
        Assert.Equal(ProfileLocation.Browser, saved.Location);
        Assert.Empty(engine.Sent);
    }

    [Fact]
    public async Task Engine_styles_rename_and_delete()
    {
        var engine = new FakeStylesEngine();
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        await store.SaveDocumentAsync(null, Style("Old"));
        await store.SetActiveIdAsync("engine:Old");

        var renamed = await store.RenameAsync("engine:Old", "New");
        Assert.Equal("engine:New", renamed.Id);
        Assert.Equal("engine:New", await store.GetActiveIdAsync());
        Assert.Null(engine.Stored("Old"));

        await store.DeleteAsync("engine:New");
        Assert.Null(engine.Stored("New"));
        Assert.Contains(MessageTypes.ProfileDelete, engine.Sent);
    }
}

using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Web query-session grouping: <see cref="EditorSessionKeys"/> mints a GUID "N" session key on
/// first use and persists it with the editor session record, so a Blazor Server circuit reset
/// (full page reload) keeps landing in the SAME history session. "Reset editor session" is the
/// deliberate boundary that starts a new one.
/// </summary>
public class EditorSessionKeyTests
{
    [Fact]
    public async Task Session_key_survives_a_reload()
    {
        var store = new FakeEditorSessionStore();
        var first = await EditorSessionKeys.GetOrCreateAsync(store);
        var second = await EditorSessionKeys.GetOrCreateAsync(store);   // simulates a reload
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reset_mints_a_new_key()
    {
        var store = new FakeEditorSessionStore();
        var first = await EditorSessionKeys.GetOrCreateAsync(store);
        await EditorSessionKeys.ResetAsync(store);
        var second = await EditorSessionKeys.GetOrCreateAsync(store);
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Regression test for a real bug caught during review: Editor.razor's OnTextChangedAsync fires
    /// on every keystroke and previously rebuilt EditorSessionRecord inline WITHOUT SessionKey. Since
    /// SaveAsync overwrites the whole persisted record, that silently wiped the key on the first
    /// keystroke after it was minted -- every reload after that would have started a brand new
    /// session, making the whole feature a no-op in practice. No bUnit test renders the Editor page
    /// (too much JS-interop/DI to stand up), so nothing else would have caught this; this test pins
    /// the exact save Editor.razor performs via the extracted BuildTextChangeRecord helper.
    /// </summary>
    [Fact]
    public async Task Session_key_survives_a_text_change_save()
    {
        var store = new FakeEditorSessionStore();
        var key = await EditorSessionKeys.GetOrCreateAsync(store);

        // Simulates exactly what Editor.razor's OnTextChangedAsync does on every keystroke.
        await store.SaveAsync(EditorSessionKeys.BuildTextChangeRecord(key, "SELECT 1;", activeProfileId: null));

        var afterEdit = await EditorSessionKeys.GetOrCreateAsync(store);
        Assert.Equal(key, afterEdit);
    }

    [Fact]
    public void BuildTextChangeRecord_carries_the_session_key_and_fields()
    {
        var record = EditorSessionKeys.BuildTextChangeRecord("abc123", "SELECT 1;", "builtin.ansi");
        Assert.Equal("abc123", record.SessionKey);
        Assert.Equal("SELECT 1;", record.DocumentText);
        Assert.Equal("builtin.ansi", record.ActiveProfileId);
    }
}

/// <summary>In-memory <see cref="IEditorSessionStore"/> fake — mirrors the shape of the real
/// interface (see <c>Editor/SessionRestoreTests.cs</c> for the real-store equivalent) without the
/// IndexedDB adapter or the 500 ms debounce, so tests can await a save synchronously.</summary>
internal sealed class FakeEditorSessionStore : IEditorSessionStore
{
    private EditorSessionRecord? _record;

    public Task SaveAsync(EditorSessionRecord record)
    {
        _record = record;
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;

    public Task<EditorSessionRecord?> RestoreAsync() => Task.FromResult(_record);

    public Task ClearAsync()
    {
        _record = null;
        return Task.CompletedTask;
    }
}

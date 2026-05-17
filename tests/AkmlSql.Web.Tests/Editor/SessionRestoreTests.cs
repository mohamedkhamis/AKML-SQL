using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 021 (web edition) -- M2 task T052. Editor-session persistence round-trip
/// through the in-memory IndexedDB adapter.
/// </summary>
public sealed class SessionRestoreTests
{
    [Fact]
    public async Task SaveAsync_then_FlushAsync_persists_and_RestoreAsync_returns_the_record()
    {
        var store = new InMemoryIndexedDbAdapter();
        var session = new EditorSessionStore(store);

        await session.SaveAsync(new EditorSessionRecord
        {
            DocumentText = "SELECT 1;",
            CursorOffset = 7,
            ActiveProfileId = "builtin.ansi",
        });
        await session.FlushAsync();

        var restored = await session.RestoreAsync();
        Assert.NotNull(restored);
        Assert.Equal("SELECT 1;", restored!.DocumentText);
        Assert.Equal(7, restored.CursorOffset);
        Assert.Equal("builtin.ansi", restored.ActiveProfileId);
    }

    [Fact]
    public async Task RestoreAsync_returns_null_on_a_fresh_store()
    {
        var store = new InMemoryIndexedDbAdapter();
        var session = new EditorSessionStore(store);

        var restored = await session.RestoreAsync();

        Assert.Null(restored);
    }

    [Fact]
    public async Task ClearAsync_drops_the_persisted_session()
    {
        var store = new InMemoryIndexedDbAdapter();
        var session = new EditorSessionStore(store);

        await session.SaveAsync(new EditorSessionRecord { DocumentText = "anything" });
        await session.FlushAsync();
        Assert.NotNull(await session.RestoreAsync());

        await session.ClearAsync();

        Assert.Null(await session.RestoreAsync());
    }

    [Fact]
    public async Task Multiple_SaveAsync_in_quick_succession_collapse_to_one_record()
    {
        var store = new InMemoryIndexedDbAdapter();
        var session = new EditorSessionStore(store);

        // Three rapid saves -- the debounce window is 500 ms, so the first two are
        // superseded. We force a flush to bypass the timer.
        await session.SaveAsync(new EditorSessionRecord { DocumentText = "v1" });
        await session.SaveAsync(new EditorSessionRecord { DocumentText = "v2" });
        await session.SaveAsync(new EditorSessionRecord { DocumentText = "v3" });
        await session.FlushAsync();

        var restored = await session.RestoreAsync();
        Assert.NotNull(restored);
        Assert.Equal("v3", restored!.DocumentText);
        Assert.Equal(1, store.Count(StoreNames.EditorSession));
    }
}

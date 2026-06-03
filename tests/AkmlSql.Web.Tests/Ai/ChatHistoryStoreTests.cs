using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T037 (US6). Chat persistence round-trips and clears in isolation, and
/// the Markdown export preserves turns/roles, records the originating provider, and is
/// code-fence-safe.
/// </summary>
public sealed class ChatHistoryStoreTests
{
    private static ChatConversation Sample()
    {
        var c = new ChatConversation
        {
            Id = "conv1",
            Title = "conversation",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
        c.Turns.Add(new ChatTurn { Role = "user", Content = "list orders" });
        c.Turns.Add(new ChatTurn { Role = "assistant", Content = "SELECT * FROM Orders", ProviderId = "anthropic" });
        return c;
    }

    [Fact]
    public async Task Save_then_Get_RoundTripsTurnsAndProvider()
    {
        var db = new InMemoryIndexedDbAdapter();
        var store = new ChatHistoryStore(db);

        await store.SaveAsync(Sample());
        var restored = await store.GetAsync();

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Turns.Count);
        Assert.Equal("user", restored.Turns[0].Role);
        Assert.Equal("SELECT * FROM Orders", restored.Turns[1].Content);
        Assert.Equal("anthropic", restored.Turns[1].ProviderId);   // FR-033
    }

    [Fact]
    public async Task Clear_RemovesConversation()
    {
        var db = new InMemoryIndexedDbAdapter();
        var store = new ChatHistoryStore(db);
        await store.SaveAsync(Sample());

        await store.ClearAsync();

        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public async Task Clear_LeavesSchemaCacheAndKeysIntact()
    {
        // FR-032: chat storage is independent of the schema cache and key vault.
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync(StoreNames.SchemaEntries, "srvdb", "schema-blob");
        await db.SetAsync(StoreNames.AiKeys, "openai", "wrapped-key");
        var store = new ChatHistoryStore(db);
        await store.SaveAsync(Sample());

        await store.ClearAsync();

        Assert.Equal("schema-blob", await db.GetAsync(StoreNames.SchemaEntries, "srvdb"));
        Assert.Equal("wrapped-key", await db.GetAsync(StoreNames.AiKeys, "openai"));
    }

    [Fact]
    public void ToMarkdown_PreservesTurnsAndEscapesCodeFences()
    {
        var c = new ChatConversation { Id = "x", UpdatedAt = DateTimeOffset.UnixEpoch };
        c.Turns.Add(new ChatTurn { Role = "user", Content = "hi" });
        c.Turns.Add(new ChatTurn { Role = "assistant", Content = "```sql\nSELECT 1\n```" });

        var md = c.ToMarkdown();

        Assert.Contains("## You", md);
        Assert.Contains("## Assistant", md);
        Assert.Contains("hi", md);
        // The message-level fence is escaped so it can't terminate the document structure.
        Assert.Contains("\\```sql", md);
        // Order: "You" heading precedes "Assistant" heading.
        Assert.True(md.IndexOf("## You", StringComparison.Ordinal) < md.IndexOf("## Assistant", StringComparison.Ordinal));
    }
}

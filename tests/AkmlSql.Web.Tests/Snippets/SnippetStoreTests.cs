using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Spec 021 (web edition) -- M5 task T116. Snippet store covers built-in loading,
/// user-snippet CRUD, shortcode lookup, and immutability of built-ins.
/// </summary>
public sealed class SnippetStoreTests
{
    private static ISnippetStore Build() => new SnippetStore(new InMemoryIndexedDbAdapter());

    [Fact]
    public async Task ListAsync_returns_built_ins_on_a_fresh_store()
    {
        var store = Build();
        var list = await store.ListAsync();

        Assert.Contains(list, s => s.Metadata.Id == "builtin.ssf");
        Assert.Contains(list, s => s.Metadata.Id == "builtin.cte");
        Assert.All(list, s => Assert.True(s.IsBuiltIn));
    }

    [Fact]
    public async Task GetByShortcodeAsync_resolves_a_built_in()
    {
        var store = Build();
        var ssf = await store.GetByShortcodeAsync("ssf");
        Assert.NotNull(ssf);
        Assert.Equal("SELECT * FROM", ssf!.Metadata.Title);
    }

    [Fact]
    public async Task GetByShortcodeAsync_is_case_insensitive()
    {
        var store = Build();
        var found = await store.GetByShortcodeAsync("SSF");
        Assert.NotNull(found);
    }

    [Fact]
    public async Task SaveAsync_user_snippet_round_trips()
    {
        var store = Build();
        var s = new WebSnippet
        {
            Metadata = new WebSnippetMetadata { Id = "user.greet", Shortcode = "hi", Title = "Hi" },
            Body = new[] { "SELECT 'hi';" },
        };
        await store.SaveAsync(s);

        var fetched = await store.GetByIdAsync("user.greet");
        Assert.NotNull(fetched);
        Assert.Equal("Hi", fetched!.Metadata.Title);
        Assert.False(fetched.IsBuiltIn);
    }

    [Fact]
    public async Task SaveAsync_rejects_built_in_id()
    {
        var store = Build();
        var s = new WebSnippet { Metadata = new WebSnippetMetadata { Id = "builtin.ssf" } };
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => store.SaveAsync(s));
    }

    [Fact]
    public async Task DeleteAsync_rejects_built_in_id()
    {
        var store = Build();
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => store.DeleteAsync("builtin.ssf"));
    }

    [Fact]
    public async Task DeleteAsync_drops_user_snippet()
    {
        var store = Build();
        var s = new WebSnippet { Metadata = new WebSnippetMetadata { Id = "user.x", Shortcode = "x" } };
        await store.SaveAsync(s);
        await store.DeleteAsync("user.x");
        Assert.Null(await store.GetByIdAsync("user.x"));
    }

    [Fact]
    public async Task SaveAsync_generates_id_when_missing()
    {
        var store = Build();
        var s = new WebSnippet { Metadata = new WebSnippetMetadata { Shortcode = "auto" } };
        await store.SaveAsync(s);
        Assert.False(string.IsNullOrEmpty(s.Metadata.Id));
        Assert.NotNull(await store.GetByIdAsync(s.Metadata.Id!));
    }

    [Fact]
    public async Task ListAsync_returns_built_ins_before_user_snippets()
    {
        var store = Build();
        var u = new WebSnippet { Metadata = new WebSnippetMetadata { Id = "user.aaa", Shortcode = "aaa", Title = "AAA" } };
        await store.SaveAsync(u);

        var list = await store.ListAsync();
        var firstUserIdx = list.ToList().FindIndex(s => !s.IsBuiltIn);
        var lastBuiltInIdx = list.ToList().FindLastIndex(s => s.IsBuiltIn);
        Assert.True(lastBuiltInIdx < firstUserIdx);
    }
}

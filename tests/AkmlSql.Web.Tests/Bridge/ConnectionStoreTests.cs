using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 021 (web edition) -- M3 task T067. ConnectionStore round-trips, validation,
/// active-id pointer, and the data-model.md E2 invariant that non-localhost
/// connections must reference a wrapped bearer-token id.
/// </summary>
public sealed class ConnectionStoreTests
{
    private static IConnectionStore Build() => new ConnectionStore(new InMemoryIndexedDbAdapter());

    [Fact]
    public async Task ListAsync_returns_empty_on_a_fresh_store()
    {
        var store = Build();
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task AddAsync_then_GetAsync_returns_the_record()
    {
        var store = Build();
        var connection = new EngineConnection
        {
            Id = "c1", Name = "My local", Host = "127.0.0.1", Port = 5081, IsLocalhost = true,
        };
        await store.AddAsync(connection);

        var fetched = await store.GetAsync("c1");

        Assert.NotNull(fetched);
        Assert.Equal("My local", fetched!.Name);
    }

    [Fact]
    public async Task AddAsync_rejects_duplicate_id()
    {
        var store = Build();
        var c = new EngineConnection { Id = "c1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true };
        await store.AddAsync(c);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task AddAsync_rejects_LAN_connection_without_bearer_token()
    {
        // data-model.md E2 invariant: non-localhost connections must carry a
        // BearerTokenWrappedRef.
        var store = Build();
        var c = new EngineConnection
        {
            Id = "lan1",
            Host = "10.0.0.5",
            Port = 5081,
            IsLocalhost = false,
            BearerTokenWrappedRef = null,
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task AddAsync_rejects_port_outside_range()
    {
        var store = Build();
        var c = new EngineConnection { Id = "c1", Host = "127.0.0.1", Port = 80, IsLocalhost = true };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task UpdateAsync_replaces_existing_record()
    {
        var store = Build();
        var c = new EngineConnection { Id = "c1", Name = "v1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true };
        await store.AddAsync(c);

        c.Name = "v2";
        await store.UpdateAsync(c);

        var fetched = await store.GetAsync("c1");
        Assert.Equal("v2", fetched!.Name);
    }

    [Fact]
    public async Task UpdateAsync_throws_on_unknown_id()
    {
        var store = Build();
        var c = new EngineConnection { Id = "nope", Host = "127.0.0.1", Port = 5081, IsLocalhost = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(c));
    }

    [Fact]
    public async Task RemoveAsync_drops_record_and_clears_active_pointer_when_it_matched()
    {
        var store = Build();
        var c = new EngineConnection { Id = "c1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true };
        await store.AddAsync(c);
        await store.SetActiveIdAsync("c1");

        await store.RemoveAsync("c1");

        Assert.Null(await store.GetAsync("c1"));
        Assert.Null(await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task ListAsync_excludes_internal_active_id_pointer()
    {
        var store = Build();
        await store.AddAsync(new EngineConnection { Id = "c1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true });
        await store.SetActiveIdAsync("c1");

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal("c1", list[0].Id);
    }
}

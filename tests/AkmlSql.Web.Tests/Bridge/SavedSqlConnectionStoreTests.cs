using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Phase 4 (web connection manager). SavedSqlConnectionStore round-trips, validation,
/// the active-id pointer, corrupt-row skipping, and the load-bearing security invariant:
/// NO password is ever serialized (SavedSqlConnection has no password field at all).
/// Mirrors ConnectionStoreTests — binds the store to the in-memory adapter, no browser.
/// </summary>
public sealed class SavedSqlConnectionStoreTests
{
    private static ISavedSqlConnectionStore Build(out InMemoryIndexedDbAdapter adapter)
    {
        adapter = new InMemoryIndexedDbAdapter();
        return new SavedSqlConnectionStore(adapter);
    }

    private static SavedSqlConnection Sample(string id = "c1", string name = "Local master") =>
        new() { Id = id, Name = name, Server = "localhost", Database = "master", WindowsAuth = true };

    [Fact]
    public async Task ListAsync_returns_empty_on_a_fresh_store()
    {
        var store = Build(out _);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task AddAsync_then_GetAsync_returns_the_record()
    {
        var store = Build(out _);
        await store.AddAsync(Sample());

        var fetched = await store.GetAsync("c1");

        Assert.NotNull(fetched);
        Assert.Equal("Local master", fetched!.Name);
        Assert.Equal("localhost", fetched.Server);
        Assert.True(fetched.WindowsAuth);
    }

    [Fact]
    public async Task AddAsync_assigns_a_guid_when_id_is_empty()
    {
        var store = Build(out _);
        var c = Sample(id: "");
        await store.AddAsync(c);

        Assert.False(string.IsNullOrEmpty(c.Id));
        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(c.Id, list[0].Id);
    }

    [Fact]
    public async Task AddAsync_rejects_duplicate_id()
    {
        var store = Build(out _);
        await store.AddAsync(Sample());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Sample()));
    }

    [Fact]
    public async Task AddAsync_rejects_empty_name()
    {
        var store = Build(out _);
        var c = Sample();
        c.Name = "   ";
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task AddAsync_rejects_empty_server()
    {
        var store = Build(out _);
        var c = Sample();
        c.Server = "";
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task AddAsync_rejects_empty_database()
    {
        var store = Build(out _);
        var c = Sample();
        c.Database = "";
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(c));
    }

    [Fact]
    public async Task UpdateAsync_replaces_existing_record()
    {
        var store = Build(out _);
        var c = Sample();
        await store.AddAsync(c);

        c.Name = "Renamed";
        c.Database = "AdventureWorks";
        await store.UpdateAsync(c);

        var fetched = await store.GetAsync("c1");
        Assert.Equal("Renamed", fetched!.Name);
        Assert.Equal("AdventureWorks", fetched.Database);
    }

    [Fact]
    public async Task UpdateAsync_throws_on_unknown_id()
    {
        var store = Build(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(Sample(id: "nope")));
    }

    [Fact]
    public async Task RemoveAsync_drops_record_and_clears_active_pointer_when_it_matched()
    {
        var store = Build(out _);
        await store.AddAsync(Sample());
        await store.SetActiveIdAsync("c1");

        await store.RemoveAsync("c1");

        Assert.Null(await store.GetAsync("c1"));
        Assert.Null(await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task RemoveAsync_keeps_active_pointer_when_a_different_row_is_removed()
    {
        var store = Build(out _);
        await store.AddAsync(Sample("c1", "First"));
        await store.AddAsync(Sample("c2", "Second"));
        await store.SetActiveIdAsync("c1");

        await store.RemoveAsync("c2");

        Assert.Equal("c1", await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task ListAsync_sorts_by_name_and_excludes_active_pointer_sentinel()
    {
        var store = Build(out _);
        await store.AddAsync(Sample("c1", "Zeta"));
        await store.AddAsync(Sample("c2", "alpha")); // lower-case to prove OrdinalIgnoreCase ordering
        await store.AddAsync(Sample("c3", "Mid"));
        await store.SetActiveIdAsync("c3");

        var list = await store.ListAsync();

        // The "_active" sentinel must NOT appear as a row, and order is by Name ignoring case.
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { "alpha", "Mid", "Zeta" }, list.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task ListAsync_skips_a_corrupt_row()
    {
        var store = Build(out var adapter);
        await store.AddAsync(Sample("c1", "Good"));
        // Inject an un-deserializable record directly into the bucket.
        adapter.Seed(StoreNames.SavedSqlConnections, "c-bad", "{ this is not valid json");

        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Equal("Good", list[0].Name);
    }

    [Fact]
    public async Task GetActiveId_is_null_until_set()
    {
        var store = Build(out _);
        Assert.Null(await store.GetActiveIdAsync());
        await store.SetActiveIdAsync("c1");
        Assert.Equal("c1", await store.GetActiveIdAsync());
        await store.SetActiveIdAsync(null);
        Assert.Null(await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task Serialized_record_never_contains_a_password_field()
    {
        // The load-bearing security invariant: SavedSqlConnection has no password property, so the
        // persisted JSON can never carry one — even for a SQL-auth row that stores a login username.
        var store = Build(out var adapter);
        var c = Sample();
        c.WindowsAuth = false;
        c.Login = "sa";
        await store.AddAsync(c);

        var raw = await adapter.GetAsync(StoreNames.SavedSqlConnections, "c1");
        Assert.NotNull(raw);

        using var doc = JsonDocument.Parse(raw!);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            Assert.DoesNotContain("password", prop.Name, StringComparison.OrdinalIgnoreCase);
        }
        // Belt-and-suspenders: scan the WHOLE serialized payload case-insensitively, so the guard
        // keeps holding even if the model ever grows a nested object (the top-level loop wouldn't).
        Assert.DoesNotContain("password", raw!, StringComparison.OrdinalIgnoreCase);
        // The login username (not a secret) IS persisted — assert the exact key/value pair.
        Assert.Contains("\"Login\":\"sa\"", raw);
    }
}

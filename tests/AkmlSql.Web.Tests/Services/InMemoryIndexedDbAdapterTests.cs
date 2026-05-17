using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 foundation. Covers <see cref="InMemoryIndexedDbAdapter"/>:
/// distinct stores are isolated, round-trips preserve values, deletes work, and clear
/// drops every record in the targeted store only.
/// </summary>
public sealed class InMemoryIndexedDbAdapterTests
{
    [Fact]
    public async Task Set_then_Get_returns_the_persisted_value()
    {
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync("s1", "k1", "v1");

        Assert.Equal("v1", await db.GetAsync("s1", "k1"));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_key()
    {
        var db = new InMemoryIndexedDbAdapter();
        Assert.Null(await db.GetAsync("s1", "missing"));
    }

    [Fact]
    public async Task Delete_removes_the_record()
    {
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync("s1", "k1", "v1");
        await db.DeleteAsync("s1", "k1");

        Assert.Null(await db.GetAsync("s1", "k1"));
    }

    [Fact]
    public async Task List_returns_every_record_in_the_store()
    {
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync("s1", "a", "1");
        await db.SetAsync("s1", "b", "2");
        await db.SetAsync("s1", "c", "3");

        var entries = await db.ListAsync("s1");
        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { "1", "2", "3" }, entries.Select(e => e.Value).OrderBy(v => v));
    }

    [Fact]
    public async Task Stores_are_isolated_from_each_other()
    {
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync("s1", "k", "v-s1");
        await db.SetAsync("s2", "k", "v-s2");

        Assert.Equal("v-s1", await db.GetAsync("s1", "k"));
        Assert.Equal("v-s2", await db.GetAsync("s2", "k"));
    }

    [Fact]
    public async Task Clear_drops_every_record_in_the_target_store_only()
    {
        var db = new InMemoryIndexedDbAdapter();
        await db.SetAsync("s1", "a", "1");
        await db.SetAsync("s2", "a", "1");

        await db.ClearAsync("s1");

        Assert.Equal(0, db.Count("s1"));
        Assert.Equal(1, db.Count("s2"));
    }
}

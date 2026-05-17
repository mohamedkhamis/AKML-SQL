using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T038. Covers profile persistence via the in-memory
/// IndexedDB adapter: built-in defaults are always present, user profiles round-trip,
/// active-id selection persists, and built-ins reject writes/deletes.
/// </summary>
public sealed class ProfileStoreTests
{
    private static IProfileStore CreateStore() => new ProfileStore(new InMemoryIndexedDbAdapter());

    [Fact]
    public async Task ListAsync_returns_built_ins_on_a_fresh_store()
    {
        var store = CreateStore();
        var list = await store.ListAsync();

        Assert.Contains(list, p => p.Id == "builtin.default");
        Assert.Contains(list, p => p.Id == "builtin.ansi");
        Assert.All(list.Where(p => p.Id.StartsWith("builtin.")), p =>
            Assert.Equal(ProfileOrigin.BuiltIn, p.Origin));
    }

    [Fact]
    public async Task SaveAsync_user_profile_round_trips_through_the_store()
    {
        var store = CreateStore();
        var profile = new FormattingProfile();
        profile.Metadata.Name = "My Profile";
        profile.Casing.ReservedKeywords = "lowercase";

        await store.SaveAsync("user.my-profile", profile);

        var fetched = await store.GetAsync("user.my-profile");
        Assert.NotNull(fetched);
        Assert.Equal("My Profile", fetched!.Name);
        Assert.Equal(ProfileOrigin.User, fetched.Origin);
        Assert.Equal("lowercase", fetched.Profile.Casing.ReservedKeywords);
    }

    [Fact]
    public async Task SaveAsync_rejects_builtin_id()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            store.SaveAsync("builtin.default", new FormattingProfile()));
    }

    [Fact]
    public async Task DeleteAsync_rejects_builtin_id()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            store.DeleteAsync("builtin.default"));
    }

    [Fact]
    public async Task GetActiveIdAsync_defaults_to_builtin_default()
    {
        var store = CreateStore();
        var active = await store.GetActiveIdAsync();
        Assert.Equal("builtin.default", active);
    }

    [Fact]
    public async Task SetActiveIdAsync_persists_and_GetActiveIdAsync_returns_it()
    {
        var store = CreateStore();
        await store.SetActiveIdAsync("builtin.ansi");
        Assert.Equal("builtin.ansi", await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task ListAsync_returns_built_ins_before_user_profiles()
    {
        var store = CreateStore();
        var p = new FormattingProfile();
        p.Metadata.Name = "ZZZ User Profile";
        await store.SaveAsync("user.zzz", p);

        var list = await store.ListAsync();
        var builtIns = list.Where(x => x.Origin == ProfileOrigin.BuiltIn).Select(x => x.Id).ToList();
        var users = list.Where(x => x.Origin == ProfileOrigin.User).Select(x => x.Id).ToList();

        // All built-ins precede all user entries -- so their indexes must be lower.
        var maxBuiltInIndex = builtIns.Max(id => list.ToList().FindIndex(p => p.Id == id));
        var minUserIndex = users.Min(id => list.ToList().FindIndex(p => p.Id == id));
        Assert.True(maxBuiltInIndex < minUserIndex);
    }
}

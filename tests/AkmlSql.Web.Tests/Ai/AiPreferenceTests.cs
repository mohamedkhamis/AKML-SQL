using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

public sealed class AiPreferenceTests
{
    [Fact]
    public async Task GetActiveAsync_returns_empty_on_a_fresh_store()
    {
        var pref = new AiPreference(new InMemoryIndexedDbAdapter());
        Assert.Equal(string.Empty, await pref.GetActiveAsync());
    }

    [Fact]
    public async Task SetActiveAsync_then_GetActiveAsync_returns_the_persisted_id()
    {
        var pref = new AiPreference(new InMemoryIndexedDbAdapter());
        await pref.SetActiveAsync("openai");
        Assert.Equal("openai", await pref.GetActiveAsync());
    }

    [Fact]
    public async Task SetActiveAsync_with_empty_clears_active()
    {
        var pref = new AiPreference(new InMemoryIndexedDbAdapter());
        await pref.SetActiveAsync("openai");
        await pref.SetActiveAsync("");
        Assert.Equal(string.Empty, await pref.GetActiveAsync());
    }
}

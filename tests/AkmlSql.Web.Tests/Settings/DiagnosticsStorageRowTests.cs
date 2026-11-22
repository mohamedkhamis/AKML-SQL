using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 047 (WEB-003, contracts/connections-and-storage.md §4): Settings › Diagnostics says whether this browser keeps the
/// saved connections and pairings, or may clear them when space runs low, and can ask again.
/// </summary>
public sealed class DiagnosticsStorageRowTests : IDisposable
{
    private readonly SettingsPageHost _host = new();
    private readonly ScriptedStorage _storage = new();

    public DiagnosticsStorageRowTests() => _host.Ctx.Services.AddSingleton<IBrowserStorageState>(_storage);

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(true, "Kept until you clear it (persistent).")]
    [InlineData(false, "Best effort: the browser may clear it when the disk is low. Saved connections and pairings are kept per address.")]
    [InlineData(null, "This browser does not report it.")]
    public void The_row_says_how_this_browser_keeps_its_data(bool? persisted, string text)
    {
        _storage.Persisted = persisted;

        var cut = _host.Open("diagnostics");

        cut.WaitForAssertion(() => Assert.Equal(text, cut.Find("[data-testid=diag-storage]").TextContent.Trim()));
        Assert.Equal(persisted == false, cut.FindAll("[data-testid=diag-storage-ask]").Count == 1);
    }

    [Fact]
    public void Ask_again_requests_persistence_once_more_and_shows_the_new_answer()
    {
        _storage.Persisted = false;
        var cut = _host.Open("diagnostics");
        cut.WaitForElement("[data-testid=diag-storage-ask]");

        _storage.Persisted = true;
        cut.Find("[data-testid=diag-storage-ask]").Click();

        Assert.Equal(1, _storage.ForcedRequests);
        cut.WaitForAssertion(() => Assert.Equal("Kept until you clear it (persistent).", cut.Find("[data-testid=diag-storage]").TextContent.Trim()));
    }

    private sealed class ScriptedStorage : IBrowserStorageState
    {
        public bool? Persisted { get; set; }
        public int ForcedRequests { get; private set; }
        public Task<bool?> RequestPersistenceAsync(bool force = false)
        {
            if (force) ForcedRequests++;
            return Task.FromResult(Persisted);
        }
        public Task<bool?> IsPersistedAsync() => Task.FromResult(Persisted);
    }
}

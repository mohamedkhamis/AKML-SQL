using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 047 (WEB-003, contracts/connections-and-storage.md §5): saved connections and pairings belong to the address the
/// page was opened at. Opened at 127.0.0.1 (or [::1]) with nothing saved there, the page says the data may be at
/// localhost — the address the installer gives — and can be dismissed.
/// </summary>
public sealed class OriginBannerTests : BunitContext
{
    private const string DismissKey = "akml.originBanner.dismissed";

    private readonly SavedSqlConnectionStore _saved;
    private readonly ConnectionStore _engines;
    private readonly FakePageEngine _page = new() { Result = new(PageEngineKind.Engine, new("127.0.0.1", 59417, "ws")) };

    public OriginBannerTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        _saved = new SavedSqlConnectionStore(adapter);
        _engines = new ConnectionStore(adapter);
        Services.AddSingleton<ISavedSqlConnectionStore>(_saved);
        Services.AddSingleton<IConnectionStore>(_engines);
        Services.AddSingleton<IPageEngine>(_page);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<OriginBanner> Render(string pageBase) =>
        Render<OriginBanner>(p => p.Add(x => x.PageBaseUri, pageBase));

    [Theory]
    [InlineData("http://127.0.0.1:59416/", "http://localhost:59416/")]
    [InlineData("https://[::1]:8443/", "https://localhost:8443/")]
    public void An_empty_profile_on_a_loopback_alias_names_the_localhost_address(string page, string canonical)
    {
        var cut = Render(page);

        var banner = cut.WaitForElement("[data-testid=origin-banner]");
        Assert.Contains("Saved connections and pairings belong to the address you opened.", banner.TextContent);
        Assert.Contains($"If you used {canonical} before, open it there, or import your settings here.", banner.TextContent);
        Assert.Equal(canonical, cut.Find("[data-testid=origin-banner-link]").GetAttribute("href"));
    }

    [Theory]
    [InlineData("http://localhost:59416/")]
    [InlineData("https://sql-host/")]
    public void Localhost_or_a_LAN_address_never_shows_it(string page)
    {
        var cut = Render(page);
        cut.WaitForAssertion(() => Assert.True(_page.Calls >= 0));
        Assert.Empty(cut.FindAll("[data-testid=origin-banner]"));
    }

    [Fact]
    public async Task A_saved_connection_hides_it()
    {
        await _saved.AddAsync(new SavedSqlConnection { Name = "Local", Server = "localhost", Database = "master", WindowsAuth = true });
        Assert.Empty(Render("http://127.0.0.1:59416/").FindAll("[data-testid=origin-banner]"));
    }

    [Fact]
    public async Task A_saved_engine_hides_it()
    {
        await _engines.AddAsync(new EngineConnection { Name = "This computer", Host = "127.0.0.1", Port = 59417 });
        Assert.Empty(Render("http://127.0.0.1:59416/").FindAll("[data-testid=origin-banner]"));
    }

    [Fact]
    public void A_page_with_no_known_engine_does_not_show_it()
    {
        _page.Result = new PageEngineResult(PageEngineKind.Static);
        Assert.Empty(Render("http://127.0.0.1:59416/").FindAll("[data-testid=origin-banner]"));
    }

    [Fact]
    public void Dismissing_it_is_remembered_for_this_address()
    {
        var cut = Render("http://127.0.0.1:59416/");
        cut.WaitForElement("[data-testid=origin-banner-dismiss]").Click();

        Assert.Empty(cut.FindAll("[data-testid=origin-banner]"));
        var set = Assert.Single(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem");
        Assert.Equal(new object?[] { DismissKey, "1" }, set.Arguments.ToArray());
    }

    [Fact]
    public void An_earlier_dismissal_keeps_it_hidden()
    {
        JSInterop.Setup<string?>("localStorage.getItem", DismissKey).SetResult("1");
        Assert.Empty(Render("http://127.0.0.1:59416/").FindAll("[data-testid=origin-banner]"));
    }

    [Fact]
    public void Unreadable_browser_storage_still_renders()
    {
        JSInterop.Setup<string?>("localStorage.getItem", DismissKey).SetException(new Microsoft.JSInterop.JSException("blocked"));
        var cut = Render("http://127.0.0.1:59416/");
        cut.WaitForElement("[data-testid=origin-banner]");
    }
}

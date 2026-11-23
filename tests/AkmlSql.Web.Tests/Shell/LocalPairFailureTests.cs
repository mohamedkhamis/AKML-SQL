using System;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 047 (WEB-001, contracts/engine-pairing.md §3): when "Use this computer's engine" fails, the status bar says which
/// address it tried and offers Add engine… with that address filled in. It no longer jumps to Settings on its own.
/// </summary>
public sealed class LocalPairFailureTests : BunitContext
{
    private readonly FakePageEngine _page = new();
    private readonly ScriptedAutoConnect _auto = new();

    public LocalPairFailureTests()
    {
        var bridge = new FakeEngineBridge();
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IConnectionStore>(new ConnectionStore(adapter));
        Services.AddSingleton<ISqlConnectionService>(new SqlConnectionService(bridge, new FakeDiagnosticsRingBuffer()));
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(new ConnectionManagerController());
        Services.AddSingleton<IEngineAutoConnect>(_auto);
        Services.AddSingleton<IPageEngine>(_page);
    }

    private IRenderedComponent<StatusBar> RenderAndClickUseLocal()
    {
        var cut = Render<StatusBar>();
        cut.Find("[data-testid=engine-use-local]").Click();
        return cut;
    }

    [Fact]
    public void Nothing_answering_names_the_address_and_offers_Add_engine()
    {
        _auto.Next = new LocalPairResult(false, "localhost:59417", LocalPairFailure.NotReachable);
        var nav = Services.GetRequiredService<NavigationManager>();
        var before = nav.Uri;

        var cut = RenderAndClickUseLocal();

        var notice = cut.WaitForElement("[data-testid=local-engine-failure]");
        Assert.Contains("No engine answered on localhost:59417. This install may use another port.", notice.TextContent);
        Assert.Equal(before, nav.Uri);   // no automatic jump to Settings any more
        cut.Find("[data-testid=local-engine-add]").Click();
        Assert.EndsWith("settings/connections", nav.Uri);
        Assert.Equal(new PageEngineInfo("localhost", 59417, "ws"), _page.PairingPrefill);
    }

    [Fact]
    public void An_engine_that_needs_its_PIN_offers_to_pair()
    {
        _auto.Next = new LocalPairResult(false, "sql-host:59417", LocalPairFailure.PinRequired);

        var cut = RenderAndClickUseLocal();

        var notice = cut.WaitForElement("[data-testid=local-engine-failure]");
        Assert.Contains("This engine needs its pairing PIN.", notice.TextContent);
        Assert.NotNull(cut.Find("[data-testid=local-engine-pair]"));
    }

    [Fact]
    public void A_refusal_says_the_engine_refused_and_offers_Settings()
    {
        _auto.Next = new LocalPairResult(false, "localhost:59417", LocalPairFailure.Rejected);

        var cut = RenderAndClickUseLocal();

        var notice = cut.WaitForElement("[data-testid=local-engine-failure]");
        Assert.Contains("The engine at localhost:59417 refused the connection.", notice.TextContent);
        Assert.NotNull(cut.Find("[data-testid=local-engine-settings]"));
    }

    // UX pass 2026-11-23: the answer to "Use this computer's engine" and the general "No engine paired yet." strip shared the
    // bar and cut each other short. The answer stands alone; Dismiss brings the general strip back.
    [Fact]
    public void The_answer_stands_alone_until_it_is_dismissed()
    {
        _auto.Next = new LocalPairResult(false, "localhost:59417", LocalPairFailure.NotReachable);
        var cut = RenderAndClickUseLocal();
        cut.WaitForElement("[data-testid=local-engine-failure]");

        Assert.Empty(cut.FindAll("[data-testid=engine-issue]"));

        cut.Find("[data-testid=local-engine-dismiss]").Click();

        Assert.NotNull(cut.Find("[data-testid=engine-issue]"));
        Assert.NotNull(cut.Find("[data-testid=engine-use-local]"));
    }

    [Fact]
    public void Dismiss_hides_the_notice_and_success_shows_none()
    {
        _auto.Next = new LocalPairResult(false, "localhost:59417", LocalPairFailure.NotReachable);
        var cut = RenderAndClickUseLocal();
        cut.WaitForElement("[data-testid=local-engine-failure]");
        cut.Find("[data-testid=local-engine-dismiss]").Click();
        Assert.Empty(cut.FindAll("[data-testid=local-engine-failure]"));

        _auto.Next = new LocalPairResult(true, "localhost:59417", null);
        cut.Find("[data-testid=engine-use-local]").Click();
        Assert.Empty(cut.FindAll("[data-testid=local-engine-failure]"));
    }

    [Fact]
    public void The_button_names_the_address_it_will_try()
    {
        var cut = Render<StatusBar>();
        Assert.Equal("Pair with the engine on this computer (localhost:47291)",
            cut.Find("[data-testid=engine-use-local]").GetAttribute("title"));
    }

    private sealed class ScriptedAutoConnect : IEngineAutoConnect
    {
        internal LocalPairResult Next { get; set; } = new(false, "localhost:47291", LocalPairFailure.NotReachable);
        public EngineConnectFailure? LastFailure => new(EngineConnectFailureKind.NotPaired, "No engine paired yet.", null);
        public event Action? FailureChanged { add { } remove { } }
        public Task StartAsync() => Task.CompletedTask;
        public Task RetryNowAsync() => Task.CompletedTask;
        public Task<LocalPairResult> PairLocalEngineAsync() => Task.FromResult(Next);
    }
}

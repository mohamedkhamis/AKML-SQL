using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T121, FR-006). A feature the paired engine does not offer says so, in place, with the capability it needs and
/// the engine version it found, so the person knows what to update. Nothing is shown while the engine offers it. The wrapper
/// behaviour the component had since spec 021 (task T076) is unchanged and is pinned here too.
/// </summary>
public sealed class CapabilityNoticeTests : BunitContext
{
    private const string Capability = "dialect.postgresql.v1";

    // ---- the spec 021 behaviour: the bridge decides, the content is the child ---------------------------------------

    [Fact]
    public void With_the_capability_on_the_bridge_the_child_content_renders_and_no_notice_does()
    {
        Services.AddSingleton<IEngineBridge>(new FakeEngineBridge { EngineCapabilities = new[] { "snippets.write" } });

        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.RequiredCapability, "snippets.write")
            .AddChildContent("<p data-testid=\"inner\">Save snippets</p>"));

        Assert.NotEmpty(cut.FindAll("[data-testid=inner]"));
        Assert.Empty(cut.FindAll(".akml-capability-notice"));
    }

    [Fact]
    public void Without_it_the_original_notice_renders_in_place_of_the_child_content()
    {
        Services.AddSingleton<IEngineBridge>(new FakeEngineBridge { EngineCapabilities = new[] { "core.format.v1" } });

        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.RequiredCapability, "snippets.write")
            .AddChildContent("<p data-testid=\"inner\">Save snippets</p>"));

        Assert.Empty(cut.FindAll("[data-testid=inner]"));
        var notice = cut.Find(".akml-capability-notice");
        Assert.Contains("snippets.write", notice.TextContent);
        Assert.Contains("does not advertise", notice.TextContent);
        Assert.Contains("Update the engine", notice.TextContent);
    }

    [Fact]
    public void A_host_with_no_bridge_reads_as_the_capability_not_offered()
    {
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.RequiredCapability, "snippets.write")
            .AddChildContent("<p data-testid=\"inner\">Save snippets</p>"));

        Assert.Empty(cut.FindAll("[data-testid=inner]"));
        Assert.NotEmpty(cut.FindAll(".akml-capability-notice"));
    }

    [Fact]
    public void The_notice_follows_the_bridge_when_the_engine_reconnects_with_the_capability()
    {
        var bridge = new FakeEngineBridge();
        Services.AddSingleton<IEngineBridge>(bridge);
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.RequiredCapability, "snippets.write")
            .AddChildContent("<p data-testid=\"inner\">Save snippets</p>"));
        Assert.Empty(cut.FindAll("[data-testid=inner]"));

        bridge.EngineCapabilities = new[] { "snippets.write" };
        bridge.SetState(BridgeState.Open);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=inner]")));
    }

    // ---- spec 042: a caller that already knows -----------------------------------------------------------------

    [Fact]
    public void Nothing_is_shown_while_the_engine_offers_the_capability()
    {
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.Available, true)
            .Add(x => x.Feature, "PostgreSQL")
            .Add(x => x.RequiredCapability, Capability));

        Assert.Empty(cut.FindAll("[data-testid=capability-notice]"));
        Assert.Empty(cut.FindAll(".akml-capability-notice"));
    }

    [Fact]
    public void An_engine_without_it_is_named_with_the_capability_and_its_version()
    {
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.Available, false)
            .Add(x => x.Feature, "PostgreSQL")
            .Add(x => x.RequiredCapability, Capability)
            .Add(x => x.EngineVersion, "1.26.1001.1200+abc"));

        var notice = cut.Find("[data-testid=capability-notice]");
        Assert.Equal("note", notice.GetAttribute("role"));
        Assert.Equal(Capability, notice.GetAttribute("data-capability"));
        Assert.Contains("PostgreSQL", notice.TextContent);
        Assert.Contains(Capability, notice.TextContent);
        Assert.Contains("1.26.1001.1200", notice.TextContent);
        Assert.DoesNotContain("+abc", notice.TextContent);
        Assert.Contains("update the engine", notice.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_engine_that_is_not_paired_at_all_is_asked_for_first()
    {
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.Available, false)
            .Add(x => x.EngineConnected, false)
            .Add(x => x.Feature, "PostgreSQL")
            .Add(x => x.RequiredCapability, Capability));

        var notice = cut.Find("[data-testid=capability-notice]");
        Assert.Contains("Pair an engine", notice.TextContent);
        Assert.DoesNotContain("update the engine", notice.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_test_id_is_the_callers()
    {
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.Available, false)
            .Add(x => x.Feature, "PostgreSQL")
            .Add(x => x.RequiredCapability, Capability)
            .Add(x => x.TestId, "conn-kind-hint"));

        Assert.NotEmpty(cut.FindAll("[data-testid=conn-kind-hint]"));
    }

    [Fact]
    public void A_caller_that_passes_the_answer_needs_no_bridge_at_all()
    {
        // No IEngineBridge is registered in this context.
        var cut = Render<CapabilityNotice>(p => p
            .Add(x => x.Available, false)
            .Add(x => x.Feature, "PostgreSQL")
            .Add(x => x.RequiredCapability, Capability));

        Assert.NotEmpty(cut.FindAll(".akml-capability-notice"));
    }

    // ---- the helper -------------------------------------------------------------------------------------------

    [Fact]
    public void The_bridge_helper_reads_the_capabilities_of_the_last_handshake()
    {
        var bridge = new FakeEngineBridge { EngineCapabilities = new[] { "core.format.v1", Capability } };

        Assert.True(bridge.Advertises(Capability));
        Assert.True(bridge.Advertises("core.format.v1"));
        Assert.False(bridge.Advertises("session.database.v1"));
        Assert.False(new FakeEngineBridge().Advertises(Capability));
    }
}

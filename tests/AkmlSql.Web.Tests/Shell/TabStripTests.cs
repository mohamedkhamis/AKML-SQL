using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T020, research R58, FR-070/FR-071) — every tab strip in the app shares one look and one
/// keyboard behaviour: arrows and Home/End move focus AND switch the tab at once, the active tab is
/// announced, and badges update in place. Existing tab ids (ai-tab-chat, preview-mysql) survive.
/// </summary>
public sealed class TabStripTests : BunitContext
{
    private static readonly TabItem[] Three =
    {
        new("results", "Results"),
        new("messages", "Messages", TestId: "results-tab-messages"),
        new("problems", "Problems", Badge: "3", BadgeKind: "warning", BadgeTestId: "results-problems-badge"),
    };

    public TabStripTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<TabStrip> RenderStrip(string active, Action<string>? onChanged = null, RenderFragment? trailing = null)
        => Render<TabStrip>(p => p
            .Add(t => t.Tabs, Three)
            .Add(t => t.ActiveId, active)
            .Add(t => t.AriaLabel, "Results pane")
            .Add(t => t.IdPrefix, "rp")
            .Add(t => t.Name, "results")
            .Add(t => t.ActiveIdChanged, EventCallback.Factory.Create<string>(this, id => onChanged?.Invoke(id)))
            .Add(t => t.Trailing, trailing));

    [Fact]
    public void Renders_a_labelled_tablist_of_tabs()
    {
        var cut = RenderStrip("results");

        var list = cut.Find("[role=tablist]");
        Assert.Equal("Results pane", list.GetAttribute("aria-label"));
        var tabs = cut.FindAll("[role=tab]");
        Assert.Equal(3, tabs.Count);
        Assert.Equal("tabstrip-results", cut.Find(".akml-tabs").GetAttribute("data-testid"));
    }

    [Fact]
    public void The_active_tab_is_selected_and_the_only_tab_stop()
    {
        var cut = RenderStrip("messages");

        var tabs = cut.FindAll("[role=tab]");
        Assert.Equal(new[] { "false", "true", "false" }, tabs.Select(t => t.GetAttribute("aria-selected")));
        Assert.Equal(new[] { "-1", "0", "-1" }, tabs.Select(t => t.GetAttribute("tabindex")));
    }

    [Fact]
    public void Each_tab_controls_its_panel()
    {
        var cut = RenderStrip("results");

        var tab = cut.FindAll("[role=tab]")[1];
        Assert.Equal("rp-tab-messages", tab.Id);
        Assert.Equal("rp-panel-messages", tab.GetAttribute("aria-controls"));
        Assert.Equal("rp-panel-messages", TabStrip.PanelId("rp", "messages"));
        Assert.Equal("rp-tab-messages", TabStrip.TabId("rp", "messages"));
    }

    [Fact]
    public void TestId_is_kept_and_defaults_to_tab_id()
    {
        var cut = RenderStrip("results");

        var tabs = cut.FindAll("[role=tab]");
        Assert.Equal("tab-results", tabs[0].GetAttribute("data-testid"));
        Assert.Equal("results-tab-messages", tabs[1].GetAttribute("data-testid"));
    }

    [Theory]
    [InlineData("results", "ArrowRight", "messages")]
    [InlineData("messages", "ArrowRight", "problems")]
    [InlineData("problems", "ArrowRight", "results")]   // wraps
    [InlineData("results", "ArrowLeft", "problems")]    // wraps
    [InlineData("problems", "ArrowLeft", "messages")]
    [InlineData("messages", "Home", "results")]
    [InlineData("results", "End", "problems")]
    public void Arrow_and_Home_End_keys_activate_at_once(string active, string key, string expected)
    {
        string? changed = null;
        var cut = RenderStrip(active, id => changed = id);

        cut.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, changed);
        // Controlled or not, the strip shows the new tab as selected straight away.
        Assert.Equal("true", cut.Find($"#rp-tab-{expected}").GetAttribute("aria-selected"));
    }

    [Fact]
    public void Other_keys_do_nothing()
    {
        string? changed = null;
        var cut = RenderStrip("results", id => changed = id);

        cut.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Null(changed);
    }

    [Fact]
    public void Clicking_a_tab_activates_it()
    {
        string? changed = null;
        var cut = RenderStrip("results", id => changed = id);

        cut.FindAll("[role=tab]")[2].Click();

        Assert.Equal("problems", changed);
    }

    [Fact]
    public void A_badge_renders_with_its_kind_and_updates_in_place()
    {
        var cut = RenderStrip("results");

        var badge = cut.Find("[data-testid=results-problems-badge]");
        Assert.Equal("3", badge.TextContent);
        Assert.Equal("warning", badge.GetAttribute("data-kind"));

        cut.Render(p => p.Add(t => t.Tabs, new[]
        {
            Three[0], Three[1],
            new TabItem("problems", "Problems", Badge: "5", BadgeKind: "error", BadgeTestId: "results-problems-badge"),
        }));

        var updated = cut.Find("[data-testid=results-problems-badge]");
        Assert.Equal("5", updated.TextContent);
        Assert.Equal("error", updated.GetAttribute("data-kind"));
    }

    [Fact]
    public void No_badge_renders_when_none_is_set()
    {
        var cut = RenderStrip("results");
        Assert.Single(cut.FindAll(".akml-tab-badge"));
    }

    [Fact]
    public void Trailing_content_sits_beside_the_tablist_never_inside_it()
    {
        var cut = RenderStrip("results", trailing: b =>
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "data-testid", "ai-close");
            b.AddContent(2, "Close");
            b.CloseElement();
        });

        var close = cut.Find("[data-testid=ai-close]");
        Assert.Null(close.Closest("[role=tablist]"));
        Assert.NotNull(close.Closest(".akml-tabs"));
    }

    [Fact]
    public void The_key_trap_is_installed_on_first_render()
    {
        var module = JSInterop.SetupModule("./js/akml-ui.js");
        module.SetupVoid("trapTabStripKeys", _ => true);

        RenderStrip("results");

        module.VerifyInvoke("trapTabStripKeys");
    }
}

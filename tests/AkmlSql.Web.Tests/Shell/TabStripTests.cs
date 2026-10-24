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

    // ── Spec 043 (T016, research R10): a closable strip, for the query tabs ───────────────────────────────────

    private static readonly TabItem[] Queries =
    {
        new("q1", "SQLQuery1", TestId: "doc-tab", Title: "SQLQuery1"),
        new("q2", "orders-audit", TestId: "doc-tab", Title: "orders-audit", IsModified: true),
        new("q3", "SQLQuery3", TestId: "doc-tab", Title: "SQLQuery3"),
    };

    private static ComponentParameterCollectionBuilder<TabStrip> ClosableParameters(
        ComponentParameterCollectionBuilder<TabStrip> p, IReadOnlyList<TabItem> tabs, string active) => p
            .Add(t => t.Tabs, tabs)
            .Add(t => t.ActiveId, active)
            .Add(t => t.AriaLabel, "Query tabs")
            .Add(t => t.IdPrefix, "doc")
            .Add(t => t.Name, "doc")
            .Add(t => t.Closable, true);

    private IRenderedComponent<TabStrip> RenderClosable(string active, Action<string>? onChanged = null, Action<string>? onClose = null)
        => Render<TabStrip>(p => p
            .Add(t => t.Tabs, Queries)
            .Add(t => t.ActiveId, active)
            .Add(t => t.AriaLabel, "Query tabs")
            .Add(t => t.IdPrefix, "doc")
            .Add(t => t.Name, "doc")
            .Add(t => t.Closable, true)
            .Add(t => t.ActiveIdChanged, EventCallback.Factory.Create<string>(this, id => onChanged?.Invoke(id)))
            .Add(t => t.OnClose, EventCallback.Factory.Create<string>(this, id => onClose?.Invoke(id))));

    [Fact]
    public void A_closable_tab_is_not_a_button_and_holds_a_close_button_that_is_not_a_tab_stop()
    {
        var cut = RenderClosable("q1");

        var tabs = cut.FindAll("[role=tab]");
        Assert.Equal(3, tabs.Count);
        Assert.All(tabs, t => Assert.Equal("DIV", t.TagName));   // a button cannot hold a button
        var close = tabs[1].QuerySelector("[data-testid=doc-tab-close]");
        Assert.NotNull(close);
        Assert.Equal("BUTTON", close!.TagName);
        Assert.Equal("-1", close.GetAttribute("tabindex"));
        Assert.Equal("Close orders-audit", close.GetAttribute("aria-label"));
        Assert.Equal(new[] { "q1", "q2", "q3" }, tabs.Select(t => t.GetAttribute("data-tab-id")));
        Assert.Equal(new[] { "true", "false", "false" }, tabs.Select(t => t.GetAttribute("data-active")));
    }

    [Fact]
    public void A_modified_tab_shows_its_mark_with_an_accessible_name()
    {
        var cut = RenderClosable("q1");

        var tabs = cut.FindAll("[role=tab]");
        Assert.Null(tabs[0].QuerySelector(".akml-tab-modified"));
        var mark = tabs[1].QuerySelector(".akml-tab-modified");
        Assert.NotNull(mark);
        Assert.Equal("*", mark!.TextContent.Trim());
        Assert.Equal("modified", mark.GetAttribute("aria-label"));
        Assert.Equal("true", tabs[1].GetAttribute("data-modified"));
    }

    [Fact]
    public void The_close_button_closes_without_activating()
    {
        string? changed = null, closed = null;
        var cut = RenderClosable("q1", id => changed = id, id => closed = id);

        cut.FindAll("[data-testid=doc-tab-close]")[2].Click();

        Assert.Equal("q3", closed);
        Assert.Null(changed);
    }

    [Fact]
    public void Delete_closes_the_focused_tab_and_a_middle_click_closes_the_one_under_the_pointer()
    {
        string? closed = null;
        var cut = RenderClosable("q2", onClose: id => closed = id);

        cut.FindAll("[role=tab]")[1].KeyDown(new KeyboardEventArgs { Key = "Delete" });
        Assert.Equal("q2", closed);

        cut.FindAll("[role=tab]")[0].TriggerEvent("onmouseup", new MouseEventArgs { Button = 0 });
        Assert.Equal("q2", closed);   // a left release is a click, not a close
        cut.FindAll("[role=tab]")[0].TriggerEvent("onmouseup", new MouseEventArgs { Button = 1 });
        Assert.Equal("q1", closed);
    }

    [Fact]
    public void Enter_and_Space_activate_a_closable_tab_and_the_arrows_still_move()
    {
        string? changed = null;
        var cut = RenderClosable("q1", id => changed = id);

        cut.FindAll("[role=tab]")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal("q2", changed);

        cut.FindAll("[role=tab]")[2].Click();
        Assert.Equal("q3", changed);
    }

    // Review of US1 (finding 25): the keys act on the tab that has focus, which need not be the active one.

    [Fact]
    public void Delete_closes_the_tab_that_has_focus_even_when_another_is_active()
    {
        string? closed = null;
        var cut = RenderClosable("q1", onClose: id => closed = id);

        cut.FindAll("[role=tab]")[2].KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Equal("q3", closed);
    }

    [Fact]
    public void A_held_Delete_closes_one_tab_not_its_neighbours()
    {
        var closed = new List<string>();
        var cut = RenderClosable("q2", onClose: id => closed.Add(id));

        cut.FindAll("[role=tab]")[1].KeyDown(new KeyboardEventArgs { Key = "Delete" });
        cut.FindAll("[role=tab]")[1].KeyDown(new KeyboardEventArgs { Key = "Delete", Repeat = true });
        cut.FindAll("[role=tab]")[1].KeyDown(new KeyboardEventArgs { Key = "Delete", Repeat = true });

        Assert.Equal(new[] { "q2" }, closed);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void Enter_and_Space_activate_the_tab_that_has_focus(string key)
    {
        string? changed = null;
        var cut = RenderClosable("q1", id => changed = id);

        cut.FindAll("[role=tab]")[2].KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal("q3", changed);
    }

    [Fact]
    public void The_arrows_move_from_the_tab_that_has_focus()
    {
        string? changed = null;
        var cut = RenderClosable("q1", id => changed = id);

        cut.FindAll("[role=tab]")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal("q2", changed);
    }

    [Fact]
    public void Closing_a_tab_hands_focus_to_the_active_tab_when_the_closed_one_had_it()
    {
        var module = JSInterop.SetupModule("./js/akml-ui.js");
        module.SetupVoid("trapTabStripKeys", _ => true).SetVoidResult();
        module.SetupVoid("revealTab", _ => true).SetVoidResult();
        module.Setup<bool>("refocusTabStrip", _ => true).SetResult(true);
        var cut = RenderClosable("q2");
        Assert.Empty(module.Invocations.Where(i => i.Identifier == "refocusTabStrip"));

        // The page closed q2 and made the tab before it active.
        cut.Render(p => ClosableParameters(p, new[] { Queries[0], Queries[2] }, "q1"));

        var call = Assert.Single(module.Invocations.Where(i => i.Identifier == "refocusTabStrip"));
        Assert.Equal(TabStrip.TabId("doc", "q1"), call.Arguments[1]);
    }

    [Fact]
    public void Closing_the_only_tab_hands_focus_to_the_tab_that_replaces_it()
    {
        var module = JSInterop.SetupModule("./js/akml-ui.js");
        module.SetupVoid("trapTabStripKeys", _ => true).SetVoidResult();
        module.SetupVoid("revealTab", _ => true).SetVoidResult();
        module.Setup<bool>("refocusTabStrip", _ => true).SetResult(true);
        var only = new[] { Queries[0] };
        var cut = Render<TabStrip>(p => ClosableParameters(p, only, "q1"));

        // The page renders once while the replacement is being made: no tabs, and the closed tab still named active.
        cut.Render(p => ClosableParameters(p, Array.Empty<TabItem>(), "q1"));
        Assert.Empty(module.Invocations.Where(i => i.Identifier == "refocusTabStrip"));
        var replacement = new TabItem("q9", "SQLQuery9", TestId: "doc-tab");
        cut.Render(p => ClosableParameters(p, new[] { replacement }, "q9"));

        var call = Assert.Single(module.Invocations.Where(i => i.Identifier == "refocusTabStrip"));
        Assert.Equal(TabStrip.TabId("doc", "q9"), call.Arguments[1]);
    }

    [Fact]
    public void A_re_render_that_closes_nothing_moves_no_focus()
    {
        var module = JSInterop.SetupModule("./js/akml-ui.js");
        module.SetupVoid("trapTabStripKeys", _ => true).SetVoidResult();
        module.SetupVoid("revealTab", _ => true).SetVoidResult();
        module.Setup<bool>("refocusTabStrip", _ => true).SetResult(true);
        var cut = RenderClosable("q1");

        cut.Render(p => ClosableParameters(p, Queries, "q3"));

        Assert.Empty(module.Invocations.Where(i => i.Identifier == "refocusTabStrip"));
    }

    [Fact]
    public void A_strip_that_is_not_closable_keeps_its_button_tabs()
    {
        var cut = RenderStrip("results");

        Assert.All(cut.FindAll("[role=tab]"), t => Assert.Equal("BUTTON", t.TagName));
        Assert.Empty(cut.FindAll("[data-testid=doc-tab-close]"));
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

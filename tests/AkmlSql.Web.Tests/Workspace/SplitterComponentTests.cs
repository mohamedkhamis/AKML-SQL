using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Workspace;

/// <summary>
/// Spec 041 (T113, research R35, FR-044) — a splitter is an ARIA separator that the keyboard moves: Arrow keys
/// by 16 px (Shift: 64), Home / End to the panel's minimum / maximum, Enter and Space collapse and restore the
/// panel. Arrow keys move the splitter itself, so which one grows the panel depends on the panel's side.
/// </summary>
public sealed class SplitterComponentTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public SplitterComponentTests() => _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<SplitterComponent> Render(string side, int value, List<int> committed, Action? toggled = null) =>
        _ctx.Render<SplitterComponent>(p => p
            .Add(x => x.Id, "left")
            .Add(x => x.Side, side)
            .Add(x => x.Value, value)
            .Add(x => x.Min, 180)
            .Add(x => x.Max, 600)
            .Add(x => x.Controls, "workspace-schema")
            .Add(x => x.Label, "Resize the Schema panel")
            .Add(x => x.CssVar, "--akml-ws-left")
            .Add(x => x.TestId, "workspace-splitter-left")
            .Add(x => x.OnCommitted, (int px) => committed.Add(px))
            .Add(x => x.OnCollapseToggled, () => toggled?.Invoke()));

    private static void Key(IRenderedComponent<SplitterComponent> cut, string key, bool shift = false) =>
        cut.Find("[data-testid=workspace-splitter-left]").KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift });

    [Fact]
    public void It_is_a_focusable_ARIA_separator()
    {
        var cut = Render("left", 260, new());

        var el = cut.Find("[data-testid=workspace-splitter-left]");
        Assert.Equal("separator", el.GetAttribute("role"));
        Assert.Equal("0", el.GetAttribute("tabindex"));
        Assert.Equal("vertical", el.GetAttribute("aria-orientation"));
        Assert.Equal("260", el.GetAttribute("aria-valuenow"));
        Assert.Equal("180", el.GetAttribute("aria-valuemin"));
        Assert.Equal("600", el.GetAttribute("aria-valuemax"));
        Assert.Equal("workspace-schema", el.GetAttribute("aria-controls"));
        Assert.Equal("Resize the Schema panel", el.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_results_splitter_is_horizontal()
    {
        var cut = Render("bottom", 300, new());
        Assert.Equal("horizontal", cut.Find("[data-testid=workspace-splitter-left]").GetAttribute("aria-orientation"));
    }

    [Fact]
    public void Arrows_move_the_splitter_16_px_and_64_with_Shift()
    {
        var committed = new List<int>();
        var cut = Render("left", 260, committed);

        Key(cut, "ArrowRight");
        Key(cut, "ArrowLeft");
        Key(cut, "ArrowRight", shift: true);

        Assert.Equal(new[] { 276, 244, 324 }, committed);
    }

    [Fact]
    public void Arrows_grow_the_panel_on_their_side()
    {
        var ai = new List<int>();
        var right = Render("right", 380, ai);
        Key(right, "ArrowLeft");   // the splitter moves left: the AI panel on its right grows
        Assert.Equal(new[] { 396 }, ai);

        var results = new List<int>();
        var bottom = Render("bottom", 300, results);
        Key(bottom, "ArrowUp");    // the splitter moves up: the results pane below it grows
        Key(bottom, "ArrowDown");
        Assert.Equal(new[] { 316, 284 }, results);
    }

    [Fact]
    public void Home_and_End_go_to_the_limits_and_sizes_never_pass_them()
    {
        var committed = new List<int>();
        var cut = Render("left", 590, committed);

        Key(cut, "End");
        Key(cut, "Home");
        Key(cut, "ArrowRight", shift: true);   // from 590: past the max

        Assert.Equal(new[] { 600, 180, 600 }, committed);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void Enter_and_Space_collapse_and_restore(string key)
    {
        int toggled = 0;
        var cut = Render("left", 260, new(), () => toggled++);

        Key(cut, key);

        Assert.Equal(1, toggled);
    }

    [Fact]
    public void Tab_and_other_keys_do_nothing()
    {
        var committed = new List<int>();
        var cut = Render("left", 260, committed);

        Key(cut, "Tab");
        Key(cut, "ArrowUp");   // a vertical separator ignores up / down

        Assert.Empty(committed);
    }

    [Fact]
    public void It_attaches_the_pointer_drag_and_the_key_trap()
    {
        var module = _ctx.JSInterop.SetupModule("./js/akml-workspace.js");
        module.Mode = JSRuntimeMode.Loose;

        Render("left", 260, new());

        Assert.NotEmpty(module.Invocations["attachSplitter"]);
        Assert.NotEmpty(module.Invocations["trapSeparatorKeys"]);
        Assert.Equal("--akml-ws-left", module.Invocations["attachSplitter"].Single().Arguments[2]);
    }

    [Fact]
    public async Task A_drag_that_ends_reports_its_size()
    {
        var committed = new List<int>();
        var cut = Render("left", 260, committed);

        await cut.InvokeAsync(() => cut.Instance.OnSplitterCommitted("left", 333));

        Assert.Equal(new[] { 333 }, committed);
    }
}

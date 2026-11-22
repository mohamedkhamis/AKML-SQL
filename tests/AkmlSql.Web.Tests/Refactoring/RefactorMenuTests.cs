using System.Linq;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 047 (WEB-011, WEB-012, contracts/workspace-ai.md §3) — the Refactor menu is a keyboard dialog: its items are buttons,
/// an item that cannot run says why (readable without hovering, and tied to the button for a screen reader), focus moves
/// into it when it opens, Arrow keys move between the items that can run, and Escape closes it and gives focus back.
/// Before, the items were clickable list items with no keyboard access, and Escape did nothing.
/// </summary>
public sealed class RefactorMenuTests : BunitContext
{
    private readonly BunitJSModuleInterop _ui;

    public RefactorMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ui = JSInterop.SetupModule("./js/akml-ui.js");
        _ui.Mode = JSRuntimeMode.Loose;
    }

    private static readonly RefactorMenuItem[] Items =
    {
        new("ExpandInsertColumns", "Expand INSERT columns", false, RefactorSchema.NeedsSchemaReason),
        new("RemoveSemicolons", "Remove semicolons", true, null),
        new("ConvertOldStyleJoins", "Convert old-style joins", false, RefactorSchema.StillLoadingReason),
        new("Unformat", "Unformat", true, null),
        new("heavy-0", "Smart Rename", false, RefactorSchema.HeavyNeedsEngineReason, "Schema-aware (requires engine)"),
    };

    private IRenderedComponent<RefactorMenu> RenderMenu(System.Action<RefactorMenuItem>? chose = null, System.Action? closed = null) =>
        Render<RefactorMenu>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.OnChoose, (RefactorMenuItem i) => chose?.Invoke(i))
            .Add(x => x.OnClose, () => closed?.Invoke()));

    [Fact]
    public void Items_are_buttons_and_a_disabled_one_is_described_by_its_reason()
    {
        var cut = RenderMenu();

        var buttons = cut.FindAll("li > button[data-testid=refactor-item]");
        Assert.Equal(5, buttons.Count);
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));

        var expand = buttons[0];
        Assert.True(expand.HasAttribute("disabled"));
        var reasonId = expand.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(reasonId));
        Assert.Equal("Needs the database schema. Connect to a database first.", cut.Find("#" + reasonId).TextContent.Trim());

        Assert.False(buttons[1].HasAttribute("disabled"));
        Assert.Null(buttons[1].GetAttribute("aria-describedby"));
        Assert.Equal("Needs a paired engine.", cut.Find("#" + buttons[4].GetAttribute("aria-describedby")).TextContent.Trim());
        Assert.Contains("Schema-aware (requires engine)", cut.Find(".akml-picker-sep").TextContent);
    }

    [Fact]
    public void Choosing_an_enabled_item_reports_it_and_gives_focus_back_first()
    {
        RefactorMenuItem? chosen = null;
        var cut = RenderMenu(chose: i => chosen = i);

        cut.FindAll("[data-testid=refactor-item]")[1].Click();

        Assert.Equal("RemoveSemicolons", chosen?.Id);
        _ui.VerifyInvoke("restoreFocus");
    }

    [Fact]
    public void Opening_moves_focus_into_the_menu_and_keeps_Tab_inside()
    {
        RenderMenu();

        _ui.VerifyInvoke("stashFocus");
        var first = _ui.VerifyInvoke("focusFirst");
        Assert.Equal("button:not([disabled])", first.Arguments[1]);
        _ui.VerifyInvoke("trapTab");
    }

    [Fact]
    public void Escape_closes_it_and_gives_focus_back()
    {
        var closed = false;
        var cut = RenderMenu(closed: () => closed = true);

        cut.Find("[data-testid=refactor-menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
        _ui.VerifyInvoke("restoreFocus");
    }

    [Fact]
    public void Arrow_keys_move_between_the_items_that_can_run_and_wrap()
    {
        var cut = RenderMenu();
        var menu = cut.Find("[data-testid=refactor-menu]");
        cut.FindAll("[data-testid=refactor-item]")[1].Focus();   // Remove semicolons, the first that can run

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("refactor-item-Unformat", LastFocusedId());

        cut.FindAll("[data-testid=refactor-item]")[3].Focus();
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("refactor-item-RemoveSemicolons", LastFocusedId());   // past the last, back to the first

        cut.FindAll("[data-testid=refactor-item]")[1].Focus();
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("refactor-item-Unformat", LastFocusedId());
    }

    private string LastFocusedId() =>
        (string)_ui.Invocations.Where(i => i.Identifier == "focusById").Last().Arguments[0]!;
}

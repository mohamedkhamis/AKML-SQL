using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T022, research R7) — the one pointer-positioned, keyboard-operable menu: the grid's cell
/// and header menus and the editor's View menu. Items show their keys; a disabled item says why; a
/// submenu opens with Right and closes with Left or Escape.
/// </summary>
public sealed class ContextMenuTests : BunitContext
{
    private readonly List<string> _ran = new();
    private int _closed;

    public ContextMenuTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private MenuItem Item(string label, string? shortcut = null, bool disabled = false, string? reason = null, bool? isChecked = null)
        => new()
        {
            Label = label,
            Shortcut = shortcut,
            Disabled = disabled,
            DisabledReason = reason,
            IsChecked = isChecked,
            TestId = "item-" + label.Replace(' ', '-').ToLowerInvariant(),
            OnSelect = () => { _ran.Add(label); return Task.CompletedTask; },
        };

    private IRenderedComponent<ContextMenu> RenderMenu(params MenuItem[] items)
        => Render<ContextMenu>(p => p
            .Add(m => m.Items, items)
            .Add(m => m.X, 120)
            .Add(m => m.Y, 80)
            .Add(m => m.TestId, "results-context-menu")
            .Add(m => m.AriaLabel, "Cell")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => _closed++)));

    private MenuItem[] Standard() => new[]
    {
        Item("Copy", "Ctrl+C"),
        Item("Copy with headers", "Ctrl+Shift+C"),
        MenuItem.Separator(),
        Item("Set to NULL", "Ctrl+0", disabled: true, reason: "This column does not allow NULL."),
        Item("Select all", "Ctrl+A"),
    };

    [Fact]
    public void Renders_a_labelled_menu_of_menu_items_with_their_keys()
    {
        var cut = RenderMenu(Standard());

        var menu = cut.Find("[data-testid=results-context-menu]");
        Assert.Equal("menu", menu.GetAttribute("role"));
        Assert.Equal("Cell", menu.GetAttribute("aria-label"));
        var items = cut.FindAll("[role=menuitem]");
        Assert.Equal(new[] { "Copy", "Copy with headers", "Set to NULL", "Select all" },
            items.Select(i => i.QuerySelector(".akml-menu-label")!.TextContent));
        Assert.Equal("Ctrl+Shift+C", items[1].QuerySelector(".akml-menu-shortcut")!.TextContent);
        Assert.Single(cut.FindAll("[role=separator]"));
    }

    [Fact]
    public void Clicking_an_item_runs_it_and_closes_the_menu()
    {
        var cut = RenderMenu(Standard());

        cut.Find("[data-testid=item-copy]").Click();

        Assert.Equal(new[] { "Copy" }, _ran);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public void A_disabled_item_says_why_and_does_nothing()
    {
        var cut = RenderMenu(Standard());

        var item = cut.Find("[data-testid=item-set-to-null]");
        Assert.Equal("true", item.GetAttribute("aria-disabled"));
        Assert.Equal("This column does not allow NULL.", item.GetAttribute("title"));

        item.Click();
        Assert.Empty(_ran);
        Assert.Equal(0, _closed);
    }

    [Fact]
    public void Up_and_Down_move_the_roving_focus_and_skip_separators()
    {
        var cut = RenderMenu(Standard());
        var menu = cut.Find("[role=menu]");

        Assert.Equal("0", cut.Find("[data-testid=item-copy]").GetAttribute("tabindex"));
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("0", cut.Find("[data-testid=item-set-to-null]").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[data-testid=item-copy]").GetAttribute("tabindex"));

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("0", cut.Find("[data-testid=item-copy-with-headers]").GetAttribute("tabindex"));

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });   // wraps to the last item
        Assert.Equal("0", cut.Find("[data-testid=item-select-all]").GetAttribute("tabindex"));
    }

    [Fact]
    public void Enter_activates_the_focused_item()
    {
        var cut = RenderMenu(Standard());
        var menu = cut.Find("[role=menu]");

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(new[] { "Copy with headers" }, _ran);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public void Space_activates_too_but_not_on_a_disabled_item()
    {
        var cut = RenderMenu(Standard());
        var menu = cut.Find("[role=menu]");

        menu.KeyDown(new KeyboardEventArgs { Key = "End" });          // Select all
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });      // Set to NULL (disabled)
        menu.KeyDown(new KeyboardEventArgs { Key = " " });
        Assert.Empty(_ran);

        menu.KeyDown(new KeyboardEventArgs { Key = "Home" });
        menu.KeyDown(new KeyboardEventArgs { Key = " " });
        Assert.Equal(new[] { "Copy" }, _ran);
    }

    [Fact]
    public void Escape_closes_without_running_anything()
    {
        var cut = RenderMenu(Standard());

        cut.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(_ran);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public void Type_ahead_moves_to_the_next_item_starting_with_the_letter()
    {
        var cut = RenderMenu(Standard());
        var menu = cut.Find("[role=menu]");

        menu.KeyDown(new KeyboardEventArgs { Key = "s" });
        Assert.Equal("0", cut.Find("[data-testid=item-set-to-null]").GetAttribute("tabindex"));
        menu.KeyDown(new KeyboardEventArgs { Key = "S" });
        Assert.Equal("0", cut.Find("[data-testid=item-select-all]").GetAttribute("tabindex"));
        menu.KeyDown(new KeyboardEventArgs { Key = "c" });
        Assert.Equal("0", cut.Find("[data-testid=item-copy]").GetAttribute("tabindex"));
    }

    [Fact]
    public void Right_opens_a_submenu_and_Left_or_Escape_closes_it()
    {
        var copyAs = new MenuItem
        {
            Label = "Copy as",
            TestId = "item-copy-as",
            Children = new[] { Item("CSV"), Item("JSON") },
        };
        var cut = RenderMenu(Item("Copy", "Ctrl+C"), copyAs);
        var menu = cut.Find("[data-testid=results-context-menu]");

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("true", cut.Find("[data-testid=item-copy-as]").GetAttribute("aria-haspopup"));
        Assert.Equal("false", cut.Find("[data-testid=item-copy-as]").GetAttribute("aria-expanded"));

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal("true", cut.Find("[data-testid=item-copy-as]").GetAttribute("aria-expanded"));
        Assert.Equal(2, cut.FindAll("[role=menu]").Count);
        Assert.Equal("0", cut.Find("[data-testid=item-csv]").GetAttribute("tabindex"));

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("0", cut.Find("[data-testid=item-json]").GetAttribute("tabindex"));

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Single(cut.FindAll("[role=menu]"));
        Assert.Equal("0", cut.Find("[data-testid=item-copy-as]").GetAttribute("tabindex"));

        menu.KeyDown(new KeyboardEventArgs { Key = "Enter" });       // Enter on a parent opens it
        Assert.Equal(2, cut.FindAll("[role=menu]").Count);
        menu.KeyDown(new KeyboardEventArgs { Key = "Escape" });      // Escape closes only the submenu
        Assert.Single(cut.FindAll("[role=menu]"));
        Assert.Equal(0, _closed);

        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        menu.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(new[] { "JSON" }, _ran);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public void Clicking_a_parent_item_opens_its_submenu()
    {
        var copyAs = new MenuItem { Label = "Copy as", TestId = "item-copy-as", Children = new[] { Item("CSV") } };
        var cut = RenderMenu(copyAs);

        cut.Find("[data-testid=item-copy-as]").Click();
        cut.Find("[data-testid=item-csv]").Click();

        Assert.Equal(new[] { "CSV" }, _ran);
    }

    [Fact]
    public void A_checked_item_shows_the_check_icon()
    {
        var cut = RenderMenu(Item("Schema panel", "F8", isChecked: true), Item("AI panel", isChecked: false));

        var items = cut.FindAll("[role=menuitemcheckbox]");
        Assert.Equal(2, items.Count);
        Assert.Equal("true", items[0].GetAttribute("aria-checked"));
        Assert.Equal("false", items[1].GetAttribute("aria-checked"));
        Assert.NotNull(items[0].QuerySelector("use[href$='akml-icon-check']"));
        Assert.Null(items[1].QuerySelector("use[href$='akml-icon-check']"));
    }

    [Fact]
    public void Clicking_the_scrim_closes_the_menu()
    {
        var cut = RenderMenu(Standard());

        cut.Find(".akml-menu-scrim").Click();

        Assert.Equal(1, _closed);
        Assert.Empty(_ran);
    }

    [Fact]
    public void The_menu_is_placed_through_the_ui_module()
    {
        var module = JSInterop.SetupModule("./js/akml-ui.js");
        module.SetupVoid("stashFocus", _ => true).SetVoidResult();
        module.SetupVoid("placeMenu", _ => true);

        RenderMenu(Standard());

        var call = module.VerifyInvoke("placeMenu");
        Assert.Equal(120.0, Convert.ToDouble(call.Arguments[1]));
        Assert.Equal(80.0, Convert.ToDouble(call.Arguments[2]));
    }
}

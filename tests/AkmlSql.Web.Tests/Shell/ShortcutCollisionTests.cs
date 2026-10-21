using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T028, research R74, FR-049) — the editor page's key table, checked statically. The bug
/// class this prevents shipped once already: the Format chord's second stroke (Ctrl+F) also opened
/// CodeMirror's Find. No key may be bound twice, none may take a key CodeMirror owns (except as a
/// chord's second stroke, which the page claims before CodeMirror sees it), and none may use a key the
/// browser reserves outright.
/// </summary>
public sealed class ShortcutCollisionTests
{
    // CodeMirror's own bindings in the editor (contracts/ui.md §2) plus the clipboard and completion keys.
    private static readonly (string Code, bool Ctrl, bool Shift, bool Alt)[] CodeMirrorKeys =
    {
        ("Enter", true, false, false),     // Mod-Enter: execute inside CodeMirror
        ("KeyF", true, false, false),      // Mod-F: find
        ("F3", false, false, false),       // find next
        ("KeyG", true, false, false),      // Mod-G: find next
        ("F12", false, false, false),      // go to definition
        ("Escape", false, false, false),
        ("Tab", false, false, false),
        ("Tab", false, true, false),
        ("KeyZ", true, false, false),
        ("KeyY", true, false, false),
        ("KeyZ", true, true, false),
        ("Slash", true, false, false),     // Mod-/: toggle comment
        ("KeyD", true, false, false),      // Mod-D: select next occurrence
        ("KeyA", true, false, false),
        ("KeyC", true, false, false),
        ("KeyV", true, false, false),
        ("KeyX", true, false, false),
        ("Space", true, false, false),     // completion
    };

    // Keys the browser keeps whatever the page does; never attempted (FR-049(b)). Spec 043 (R9) adds the
    // browser's own tab keys, so the query tabs never claim them: Ctrl+F4 and Ctrl+Shift+W close a browser
    // tab or window, Ctrl+PageDown / Ctrl+PageUp switch browser tabs in Chromium, Edge and Firefox.
    private static readonly (string Code, bool Ctrl, bool Shift, bool Alt)[] BrowserReserved =
    {
        ("KeyN", true, false, false),
        ("KeyT", true, false, false),
        ("KeyW", true, false, false),
        ("Tab", true, false, false),
        ("Tab", true, true, false),
        ("F4", true, false, false),
        ("PageDown", true, false, false),
        ("PageUp", true, false, false),
        ("KeyW", true, true, false),
    };

    // Owned by akml-command-palette.js, which registers its own capture listener on every route.
    private static readonly (string Code, bool Ctrl, bool Shift, bool Alt)[] PaletteKeys =
    {
        ("KeyP", true, false, false),
        ("KeyP", true, true, false),
    };

    [Fact]
    public void No_key_is_bound_twice()
    {
        var dupes = WorkspaceKeyMap.Bindings
            .GroupBy(b => (b.Code, b.Ctrl, b.Shift, b.Alt, b.ChordPrefix))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void No_direct_binding_takes_a_CodeMirror_key()
    {
        var hits = WorkspaceKeyMap.Bindings
            .Where(b => b.ChordPrefix == null)
            .Where(b => CodeMirrorKeys.Contains((b.Code, b.Ctrl, b.Shift, b.Alt)))
            .Select(b => b.Display)
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void No_binding_uses_a_browser_reserved_key_or_a_palette_key()
    {
        var hits = WorkspaceKeyMap.Bindings
            .Where(b => b.ChordPrefix == null)
            .Where(b => BrowserReserved.Contains((b.Code, b.Ctrl, b.Shift, b.Alt)) || PaletteKeys.Contains((b.Code, b.Ctrl, b.Shift, b.Alt)))
            .Select(b => b.Display)
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Every_chord_prefix_is_itself_free()
    {
        // Ctrl+K starts the chords; it must not also be a command of its own.
        foreach (var prefix in WorkspaceKeyMap.Bindings.Where(b => b.ChordPrefix != null).Select(b => b.ChordPrefix!).Distinct())
        {
            Assert.DoesNotContain(WorkspaceKeyMap.Bindings, b => b.ChordPrefix == null && WorkspaceKeyMap.Signature(b) == prefix);
        }
    }

    [Fact]
    public void The_table_holds_the_contract_keys()
    {
        Assert.Equal("F5", WorkspaceKeyMap.DisplayFor("editor:execute"));
        Assert.Equal("Ctrl+F5", WorkspaceKeyMap.DisplayFor("editor:parse"));
        Assert.Equal("Ctrl+R", WorkspaceKeyMap.DisplayFor("view:toggle-results"));
        Assert.Equal("F8", WorkspaceKeyMap.DisplayFor("view:toggle-schema"));
        Assert.Equal("F6", WorkspaceKeyMap.DisplayFor("view:cycle-panes"));
        Assert.Equal("Ctrl+K, Ctrl+F", WorkspaceKeyMap.DisplayFor("editor:format"));
        Assert.Equal("Ctrl+K, Ctrl+L", WorkspaceKeyMap.DisplayFor("editor:analyse"));
        Assert.Equal("Ctrl+K, Ctrl+S", WorkspaceKeyMap.DisplayFor("editor:surround"));
        Assert.Equal("Ctrl+S", WorkspaceKeyMap.DisplayFor("editor:save"));
        Assert.Equal("Ctrl+Shift+S or Ctrl+K, Ctrl+A", WorkspaceKeyMap.DisplayFor("editor:saveas"));
        Assert.Equal("Ctrl+Alt+N", WorkspaceKeyMap.DisplayFor("editor:new"));
        Assert.Equal("Ctrl+Alt+O", WorkspaceKeyMap.DisplayFor("editor:open"));
        Assert.Null(WorkspaceKeyMap.DisplayFor("editor:refactor"));
        // Spec 043 (R9, contracts/web-ui.md §2): the query tabs. The browser keeps Ctrl+Tab, Ctrl+PageDown, Ctrl+W and Ctrl+F4,
        // so the keys gain Alt, as Visual Studio's next and previous document do.
        Assert.Equal("Ctrl+Alt+PageDown", WorkspaceKeyMap.DisplayFor("editor:next-tab"));
        Assert.Equal("Ctrl+Alt+PageUp", WorkspaceKeyMap.DisplayFor("editor:previous-tab"));
        Assert.Equal("Ctrl+Alt+W", WorkspaceKeyMap.DisplayFor("editor:close-tab"));
    }

    [Fact]
    public void Display_text_is_derived_from_the_key()
    {
        foreach (var b in WorkspaceKeyMap.Bindings)
        {
            var expected = b.ChordPrefix == null
                ? WorkspaceKeyMap.Describe(b.Code, b.Ctrl, b.Shift, b.Alt)
                : WorkspaceKeyMap.DescribeSignature(b.ChordPrefix) + ", " + WorkspaceKeyMap.Describe(b.Code, b.Ctrl, b.Shift, b.Alt);
            Assert.Equal(expected, b.Display);
        }
    }
}

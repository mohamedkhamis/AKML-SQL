using AkmlSql.UiTests.Driver;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AkmlSql.UiTests;

/// <summary>
/// Spec 040 (T193) — the navigation behind <see cref="SsmsScreenshotTour.Capture_spec_040_windows"/>:
/// the AKML SQL menu, Options pages in a chosen theme, SQL History's Advanced search and the Format
/// Styles window. Each step leaves SSMS as it found it (menus closed, Options cancelled).
/// </summary>
internal sealed class Tour040(SsmsWindow window, int processId)
{
    private const string OptionsTitle = "AKML SQL – Options";
    private const string StylesTitle = "AKML SQL – Format styles";

    /// <summary>
    /// Waits for the AKML SQL menu (the package adds it a while after SSMS shows its window) and
    /// answers SQL History's start-up "Restore queries" prompt with "Not now" if it is up.
    /// </summary>
    public void Prepare()
    {
        window.TopLevelMenu("AKML SQL", 180);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var prompt = FindWindow("Restore queries", ControlType.Window);
            var notNow = prompt?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => string.Equals((b.Name ?? "").Trim(), "Not now", StringComparison.OrdinalIgnoreCase));
            if (notNow != null) { Click(notNow); Thread.Sleep(1000); return; }
            Thread.Sleep(500);
        }
    }

    public string MenuShot()
    {
        var menu = window.TopLevelMenu("AKML SQL", 60);
        Activate(menu);
        Thread.Sleep(900);
        var activeStyle = Find(() => menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .FirstOrDefault(m => (m.Name ?? "").StartsWith("Active Style", StringComparison.OrdinalIgnoreCase)), "Active Style ▸");
        Activate(activeStyle);
        Thread.Sleep(1200);
        var shot = Shot.Element(window.Raw, "spec040-akml-menu-active-style");
        for (var i = 0; i < 3; i++) { Keyboard.Press(VirtualKeyShort.ESCAPE); Thread.Sleep(200); }
        return shot;
    }

    public string Theme()
    {
        var options = OpenOptions();
        try { return ComboText(ThemeCombo(options)); }
        finally { Cancel(options); }
    }

    public void SetTheme(string theme)
    {
        var options = OpenOptions();
        var combo = ThemeCombo(options);
        if (!string.Equals(ComboText(combo), theme, StringComparison.OrdinalIgnoreCase))
        {
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            Thread.Sleep(600);
            var item = Find(() => combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(i => Says(i, theme)), $"theme '{theme}'");
            item.Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(300);
            try { combo.Patterns.ExpandCollapse.Pattern.Collapse(); } catch { /* closed by the pick */ }
            Thread.Sleep(1500);
            // A theme that changes the look reopens Options: take the window again.
            options = WaitWindow(OptionsTitle, 20);
        }
        Click(Button(options, "OK"));
        WaitGone(OptionsTitle, 15);
    }

    public IEnumerable<string> OptionsShots(string theme)
    {
        SetTheme(theme);
        var options = OpenOptions();
        var shots = new List<string>();
        foreach (var (group, page) in new[] { ("Suggestions", "Behavior"), ("Queries", "History"), ("Queries", "Color") })
        {
            Page(options, group, page);
            shots.Add(Shot.Element(options, $"spec040-options-{theme.ToLowerInvariant()}-{page.ToLowerInvariant()}"));
        }
        Cancel(options);
        return shots;
    }

    public string HistoryShot()
    {
        AkmlMenu("SQL History");
        var history = Find(() => FindWindow("SQL History", ControlType.Window, ControlType.Pane), "SQL History");
        Thread.Sleep(1500);
        var advanced = Find(() => history.FindAllDescendants()
            .FirstOrDefault(e => (e.Name ?? "").StartsWith("Advanced search", StringComparison.OrdinalIgnoreCase)), "Advanced search");
        Activate(advanced);
        Thread.Sleep(1200);
        var shot = Shot.Element(history, "spec040-history-advanced-search");
        Activate(advanced);   // closed again
        return shot;
    }

    public string StylesShot()
    {
        AkmlMenu("Formatting", "Edit Formatting Styles");
        var styles = WaitWindow(StylesTitle, 30);
        Thread.Sleep(3000);
        var lists = Find(() => styles.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem))
            .FirstOrDefault(i => (i.Name ?? "").StartsWith("Lists", StringComparison.OrdinalIgnoreCase)), "the Lists page");
        lists.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(1500);
        var shot = Shot.Element(styles, "spec040-format-styles-lists");
        Click(Button(styles, "Close"));
        WaitGone(StylesTitle, 10);
        return shot;
    }

    /// <summary>
    /// Every option page of the Format Styles window (Lists, Parentheses, … Operators), one capture
    /// per page, named <c>{prefix}-NN-page</c>. The window is closed again at the end.
    /// </summary>
    public IEnumerable<string> StylesPageShots(string prefix)
    {
        AkmlMenu("Formatting", "Edit Formatting Styles");
        var styles = WaitWindow(StylesTitle, 30);
        Thread.Sleep(3000);
        var shots = new List<string> { Shot.Element(styles, $"{prefix}-00-open") };
        var tree = Find(() => styles.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tree)), "the option tree");
        var pages = new List<string>();
        foreach (var group in tree.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem)))
        {
            if (group.Patterns.ExpandCollapse.IsSupported) group.Patterns.ExpandCollapse.Pattern.Expand();
            Thread.Sleep(200);
            var children = group.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem));
            if (children.Length == 0) pages.Add(group.Name ?? "");
            pages.AddRange(children.Select(c => c.Name ?? ""));
        }
        var n = 1;
        foreach (var name in pages.Where(p => p.Length > 0))
        {
            var item = Find(() => tree.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem))
                .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)), $"page '{name}'");
            try { item.Patterns.ScrollItem.Pattern.ScrollIntoView(); } catch { /* already in view */ }
            item.Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(1800);
            var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            shots.Add(Shot.Element(styles, $"{prefix}-{n++:00}-{slug}"));
        }

        // The 2026-10 redesign: the style list folded away, and a SELECT example in the preview.
        var hide = styles.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(b => string.Equals(b.Name, "Hide the style list", StringComparison.Ordinal));
        if (hide != null)
        {
            var join = Find(() => tree.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem))
                .FirstOrDefault(i => string.Equals(i.Name, "Join", StringComparison.OrdinalIgnoreCase)), "page 'Join'");
            join.Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(800);
            Click(hide);
            Thread.Sleep(800);
            var source = styles.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox).And(cf.ByName("Preview source")));
            if (source != null)
            {
                source.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(600);
                var revenue = source.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                    .FirstOrDefault(i => (i.Name ?? "").StartsWith("SELECT: Revenue", StringComparison.Ordinal));
                revenue?.Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(300);
                try { source.Patterns.ExpandCollapse.Pattern.Collapse(); } catch { /* closed by the pick */ }
            }
            Thread.Sleep(2500);
            shots.Add(Shot.Element(styles, $"{prefix}-15-wide-select-example"));
            var show = styles.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => string.Equals(b.Name, "Show the style list", StringComparison.Ordinal));
            if (show != null) { Click(show); Thread.Sleep(500); }
        }
        Click(Button(styles, "Close"));
        WaitGone(StylesTitle, 10);
        return shots;
    }

    // ---- Options ----------------------------------------------------------------------------

    private Window OpenOptions()
    {
        var existing = FindWindow(OptionsTitle, ControlType.Window);
        if (existing != null) return existing.AsWindow();
        AkmlMenu("Options");
        var options = WaitWindow(OptionsTitle, 30);
        Thread.Sleep(1500);
        return options;
    }

    private void Cancel(Window options)
    {
        Click(Button(options, "Cancel"));
        WaitGone(OptionsTitle, 10);
    }

    private static AutomationElement ThemeCombo(Window options)
    {
        Page(options, "General", null);
        var content = Content(options);
        return content.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox).And(cf.ByName("Theme")))
               ?? content.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox))
               ?? throw new InvalidOperationException("No theme drop-down on Options › General");
    }

    private static void Page(Window options, string group, string? page)
    {
        var tree = options.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tree))
                   ?? throw new InvalidOperationException("No Options tree");
        var target = tree.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem))
            .First(i => string.Equals(i.Name, group, StringComparison.OrdinalIgnoreCase));
        if (page != null)
        {
            if (target.Patterns.ExpandCollapse.IsSupported) target.Patterns.ExpandCollapse.Pattern.Expand();
            Thread.Sleep(300);
            target = target.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem))
                .First(i => string.Equals(i.Name, page, StringComparison.OrdinalIgnoreCase));
        }
        try { target.Patterns.ScrollItem.Pattern.ScrollIntoView(); } catch { /* already in view */ }
        target.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(900);
    }

    private static AutomationElement Content(Window options) =>
        options.FindAllChildren(cf => cf.ByControlType(ControlType.Pane)).First(p => p.ClassName == "ScrollViewer");

    private static string ComboText(AutomationElement combo)
    {
        if (combo.Patterns.Value.IsSupported) return combo.Patterns.Value.Pattern.Value.Value ?? "";
        var selected = combo.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault?.FirstOrDefault();
        return selected?.Name ?? "";
    }

    // ---- menus, windows ---------------------------------------------------------------------

    private void AkmlMenu(params string[] path)
    {
        var menu = window.TopLevelMenu("AKML SQL", 60);
        Activate(menu);
        Thread.Sleep(900);
        AutomationElement container = menu;
        foreach (var step in path)
        {
            var item = Find(() => container.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .FirstOrDefault(m => (m.Name ?? "").StartsWith(step, StringComparison.OrdinalIgnoreCase)), $"menu item '{step}'");
            Activate(item);
            Thread.Sleep(700);
            container = item;
        }
    }

    /// <summary>
    /// A window of this SSMS whose title holds <paramref name="title"/>: top level, or owned by
    /// another SSMS window (owned dialogs sit under their owner in the UI Automation tree).
    /// </summary>
    private AutomationElement? FindWindow(string title, params ControlType[] types)
    {
        bool Match(AutomationElement e) =>
            (e.Name ?? "").Contains(title, StringComparison.OrdinalIgnoreCase) && types.Contains(e.ControlType);
        var desktop = window.Raw.Automation.GetDesktop();
        foreach (var top in desktop.FindAllChildren(cf => cf.ByProcessId(processId)))
        {
            if (Match(top)) return top;
            var inner = top.FindAllDescendants().FirstOrDefault(Match);
            if (inner != null) return inner;
        }
        return null;
    }

    private Window WaitWindow(string title, int seconds) =>
        Find(() => FindWindow(title, ControlType.Window), $"window '{title}'", seconds).AsWindow();

    private void WaitGone(string title, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (FindWindow(title, ControlType.Window) != null && DateTime.UtcNow < deadline) Thread.Sleep(300);
    }

    private static AutomationElement Button(AutomationElement root, string name) =>
        root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(b => string.Equals((b.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No '{name}' button");

    private static bool Says(AutomationElement e, string text) =>
        (e.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)
        || e.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Any(t => (t.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));

    private static void Activate(AutomationElement item)
    {
        if (item.Patterns.ExpandCollapse.IsSupported
            && item.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value != ExpandCollapseState.LeafNode)
            item.Patterns.ExpandCollapse.Pattern.Expand();
        else if (item.Patterns.Invoke.IsSupported)
            item.Patterns.Invoke.Pattern.Invoke();
        else
            item.Click();
    }

    private static void Click(AutomationElement button)
    {
        if (button.Patterns.Invoke.IsSupported) button.Patterns.Invoke.Pattern.Invoke();
        else button.Click();
    }

    private static T Find<T>(Func<T?> find, string what, int seconds = 10) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var found = find();
            if (found != null) return found;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"No {what} within {seconds} s");
            Thread.Sleep(300);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace AkmlSql.UiTests.Driver;

/// <summary>
/// A driver for one SSMS 22 window — the <c>Page</c> of this harness.
///
/// <para>
/// SSMS 22 is the Visual Studio 17.x shell, so its UI is WPF and exposes a genuinely rich UI
/// Automation tree: stable AutomationIds for the editor (<c>WpfTextView</c>), the margins
/// (<c>WpfEditorUIGlyphMarginGrid</c>, <c>WpfEditorUILineNumberMargin</c>), the menu bar
/// (<c>MenuBar</c>), and tool windows keyed by their VS window GUID (<c>ST:0:0:{guid}</c>). That is
/// what makes a Playwright-shaped API possible here: these are real selectors, not screen
/// coordinates.
/// </para>
///
/// <para>
/// The editor additionally implements the UIA TextPattern, so document content can be read back
/// directly instead of being OCR'd or inferred from a screenshot. Assertions in this suite should
/// prefer that over pixels wherever the thing being asserted is text.
/// </para>
/// </summary>
public sealed class SsmsWindow
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    private const int SW_RESTORE = 9;

    private readonly Window _window;
    private readonly AutomationBase _automation;

    internal SsmsWindow(Window window, AutomationBase automation)
    {
        _window = window;
        _automation = automation;
    }

    /// <summary>The underlying FlaUI window, for anything this wrapper does not cover.</summary>
    public Window Raw => _window;

    /// <summary>The window title, which SSMS keeps in sync with the active document and connection.</summary>
    public string Title => _window.Title;

    /// <summary>
    /// Restores and focuses the window.
    ///
    /// <para>
    /// Mandatory before any capture. A minimised window still answers UI Automation queries
    /// perfectly well — the tree is live, text can be read — but it reports its position as
    /// (-32000,-32000), so a screenshot of "where the element is" grabs nothing at all. This is the
    /// most confusing failure in the whole area, because the automation half looks healthy.
    /// </para>
    /// </summary>
    public SsmsWindow BringToFront()
    {
        var h = _window.Properties.NativeWindowHandle.ValueOrDefault;
        if (h != IntPtr.Zero)
        {
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            SetForegroundWindow(h);
        }
        _window.SetForeground();
        // WPF composition lags the restore; without a beat the first capture catches a half-painted
        // frame with the previous window's pixels still showing through.
        Thread.Sleep(600);
        return this;
    }

    // ---- locators -----------------------------------------------------------

    /// <summary>
    /// The active SQL editor surface. Auto-waits, like a Playwright locator: SSMS finishes painting
    /// its frame well before the editor is actually live.
    /// </summary>
    public AutomationElement Editor(int timeoutSeconds = 30) =>
        WaitFor(() => _window.FindFirstDescendant(cf => cf.ByAutomationId("WpfTextView")),
                timeoutSeconds, "SQL editor (WpfTextView)");

    /// <summary>The glyph margin — where AKML draws its analysis warning glyphs.</summary>
    public AutomationElement GlyphMargin(int timeoutSeconds = 30) =>
        WaitFor(() => _window.FindFirstDescendant(cf => cf.ByAutomationId("WpfEditorUIGlyphMarginGrid")),
                timeoutSeconds, "glyph margin");

    /// <summary>A top-level menu by name, e.g. "AKML SQL", "Tools", "View".</summary>
    public AutomationElement TopLevelMenu(string name, int timeoutSeconds = 20) =>
        WaitFor(() =>
        {
            var bar = _window.FindFirstDescendant(cf => cf.ByAutomationId("MenuBar"));
            return bar?.FindFirstChild(cf => cf.ByName(name).And(cf.ByControlType(ControlType.MenuItem)));
        }, timeoutSeconds, $"menu '{name}'");

    /// <summary>Names of every top-level menu — a cheap way to assert the extension loaded at all.</summary>
    public IReadOnlyList<string> TopLevelMenuNames()
    {
        var bar = _window.FindFirstDescendant(cf => cf.ByAutomationId("MenuBar"));
        if (bar is null) return [];
        return bar.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem))
                  .Select(m => m.Name)
                  .Where(n => !string.IsNullOrWhiteSpace(n))
                  .ToList();
    }

    // ---- modal prompts ------------------------------------------------------

    /// <summary>
    /// Answers the prompts SSMS raises on its way up, and returns how many it handled.
    ///
    /// <para>
    /// The one that matters is <i>"Connect to the following server?"</i>. Passing <c>-S</c> on the
    /// command line does not connect — it asks first, with a modal dialog, and until that is
    /// answered the query window stays disconnected, the Query menu never appears and the Error
    /// List stays empty. Nothing about that is obvious from the automation side: the main window is
    /// present and responsive, and the document is loaded, so the run looks healthy right up until
    /// every assertion about connected behaviour fails.
    /// </para>
    /// <para>
    /// This is the desktop equivalent of Playwright's dialog handler, and like that one it should be
    /// pumped repeatedly rather than called once — the prompts do not all arrive at the same moment.
    /// </para>
    /// </summary>
    public int DismissKnownPrompts()
    {
        var handled = 0;
        foreach (var modal in _window.ModalWindows)
        {
            var title = modal.Title ?? string.Empty;
            var body = string.Join(" ", modal.FindAllDescendants()
                                             .Select(e => e.Properties.Name.ValueOrDefault)
                                             .Where(n => !string.IsNullOrWhiteSpace(n)));

            // "Connect to the following server?" -> Yes. Anything else is left alone deliberately:
            // blindly clicking the default button on an unrecognised dialog is how automation
            // silently discards data-loss warnings.
            var accept = body.Contains("Connect to the following server", StringComparison.OrdinalIgnoreCase)
                ? "Yes"
                : null;

            if (accept is null)
            {
                LastUnhandledPrompt = $"{title}: {body}".Trim();
                continue;
            }

            var button = modal.FindFirstDescendant(cf => cf.ByName(accept).And(cf.ByControlType(ControlType.Button)));
            if (button is null) continue;

            button.AsButton().Invoke();
            handled++;
            Thread.Sleep(400);
        }
        return handled;
    }

    /// <summary>The most recent modal this driver saw but chose not to answer. Useful in a failure message.</summary>
    public string? LastUnhandledPrompt { get; private set; }

    /// <summary>
    /// Pumps <see cref="DismissKnownPrompts"/> until <paramref name="isReady"/> holds. Every UI suite
    /// needs one of these: readiness in an IDE is not a single event but a sequence of them.
    /// </summary>
    public void WaitUntilReady(Func<SsmsWindow, bool> isReady, int timeoutSeconds, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                DismissKnownPrompts();
                if (isReady(this)) return;
            }
            catch (Exception) { /* the tree churns while the shell loads */ }
            Thread.Sleep(500);
        }
        throw new TimeoutException(
            $"Timed out after {timeoutSeconds}s waiting for {description}." +
            (LastUnhandledPrompt is { } p ? $" An unhandled dialog was on screen: {p}" : string.Empty));
    }

    /// <summary>True once the document is attached to a server (the title carries the connection).</summary>
    public bool IsConnected() =>
        !_window.FindAllDescendants(cf => cf.ByName("Disconnected.")).Any();

    // ---- content ------------------------------------------------------------

    /// <summary>
    /// Reads the editor's full text through the UIA TextPattern. This is the assertion channel to
    /// prefer: it reports what the document actually contains, with no dependence on scroll
    /// position, theme, font or whether the window happens to be visible.
    /// </summary>
    public string EditorText(int timeoutSeconds = 30)
    {
        var editor = Editor(timeoutSeconds);
        var pattern = editor.Patterns.Text.PatternOrDefault
            ?? throw new InvalidOperationException("The editor does not expose a UIA TextPattern.");
        return pattern.DocumentRange.GetText(-1);
    }

    /// <summary>Waits until the editor text satisfies <paramref name="predicate"/>.</summary>
    public string WaitForEditorText(Func<string, bool> predicate, int timeoutSeconds, string description)
    {
        var last = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            try { last = EditorText(5); if (predicate(last)) return last; }
            catch (Exception) { /* the editor can be momentarily absent while SSMS swaps documents */ }
            Thread.Sleep(250);
        }
        throw new TimeoutException(
            $"Timed out after {timeoutSeconds}s waiting for {description}. Editor text was:\n{last}");
    }

    /// <summary>Clicks into the editor and types, using real synthetic input so SSMS reacts exactly as it would to a person.</summary>
    public SsmsWindow TypeInEditor(string text)
    {
        Editor().Click();
        Thread.Sleep(150);
        Keyboard.Type(text);
        return this;
    }

    /// <summary>Selects all editor text and deletes it.</summary>
    public SsmsWindow ClearEditor()
    {
        Editor().Click();
        Thread.Sleep(150);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(VirtualKeyShort.DELETE);
        return this;
    }

    // ---- waiting ------------------------------------------------------------

    /// <summary>
    /// Polls until <paramref name="find"/> returns something, then returns it. The auto-waiting that
    /// makes Playwright reliable and that raw UI Automation conspicuously lacks: a plain FindFirst
    /// against an IDE mid-render returns null, and a suite written without this is a suite that
    /// fails one run in five for no reproducible reason.
    /// </summary>
    private static AutomationElement WaitFor(
        Func<AutomationElement?> find, int timeoutSeconds, string description)
    {
        var result = Retry.WhileNull(
            find,
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(250),
            throwOnTimeout: false,
            ignoreException: true);

        return result.Result
            ?? throw new TimeoutException($"Timed out after {timeoutSeconds}s waiting for {description}.");
    }
}

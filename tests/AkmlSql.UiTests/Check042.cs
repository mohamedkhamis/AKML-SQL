using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AkmlSql.UiTests.Driver;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.UiTests;

/// <summary>
/// Spec 042 (T129) - what SSMS 22 shows and does with the engine behind it, written to a file so two runs can be compared.
///
/// <para>
/// The spec promises that adding PostgreSQL to the engine changes nothing for SSMS: no new menu entry, no new prompt, and completion,
/// formatting, analysis and history behave as before. This test does not judge that. It records, for one SSMS started on a Northwind script, the
/// menus, what the Error List says, what completion commits, what Format Document makes of the script, what SQL History lists after an execution
/// and every window or prompt the product put up, plus the warnings and errors the logs hold. Run it once before the new engine is installed and once
/// after (<c>AKML_CHECK042_LABEL=before</c> / <c>after</c>) and compare the two files; the lines that differ are the finding.
/// </para>
///
/// <para>
/// It does nothing unless <c>AKML_CHECK042_LABEL</c> is set, because it drives the real desktop. <c>AKML_CHECK042_OUT</c> is the folder for the
/// observation file, the screenshots and the sandboxed app data (<c>AKML_APP_DATA_ROOT</c>, so the person's own history and settings are not touched).
/// </para>
/// </summary>
[Collection("SSMS desktop")]
public sealed class Check042(ITestOutputHelper output)
{
    private const string LabelVariable = "AKML_CHECK042_LABEL";
    private const string OutVariable = "AKML_CHECK042_OUT";

    // Line 2 is run on its own (selected, F5); the last line is the incomplete one completion works on.
    private const string Script =
        "-- check042\r\n" +
        "select o.orderid,c.companyname from dbo.orders o join dbo.customers c on c.customerid=o.customerid where o.orderid<10300 order by o.orderid;\r\n" +
        "GO\r\n" +
        "SELECT * FROM dbo.Ord";

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    private readonly List<string> _notes = [];
    private SsmsWindow _window = null!;
    private int _pid;
    private string _outDir = "";

    [Fact]
    public void Observe_ssms_with_the_engine_behind_it()
    {
        var label = Environment.GetEnvironmentVariable(LabelVariable);
        if (string.IsNullOrWhiteSpace(label))
        {
            output.WriteLine($"{LabelVariable} is not set; this observation drives the real desktop and runs only on request. Skipping.");
            return;
        }

        _outDir = Environment.GetEnvironmentVariable(OutVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "akml-check042");
        Directory.CreateDirectory(_outDir);
        Shot.ArtifactDirectory = _outDir;

        // The product's settings, history and logs for this run live under the output folder, not under the person's profile.
        var sandbox = Path.Combine(_outDir, "appdata-" + label);
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("AKML_APP_DATA_ROOT", sandbox);

        Preconditions.WaitForInteractiveDesktop(timeoutSeconds: 120);
        var idleBy = DateTime.UtcNow.AddSeconds(180);
        while (!Preconditions.DesktopIsIdle(5) && DateTime.UtcNow < idleBy) Thread.Sleep(1000);
        Assert.True(Preconditions.DesktopIsIdle(5), "Someone is using the keyboard or mouse; synthetic input would collide with theirs. Run again when the desktop is idle.");

        var (deployed, _, message) = Preconditions.CheckExtension();
        Note($"extension: {message}");
        Assert.True(deployed, message);
        Note("engine (informational, differs by design): " + EngineFacts());

        var sqlFile = Path.Combine(_outDir, "check042.sql");
        File.WriteAllText(sqlFile, Script);

        try
        {
            using var app = SsmsApp.Launch(sqlFile, server: "(local)", database: "Northwind");
            _pid = app.ProcessId;
            _window = app.MainWindow(timeoutSeconds: 240);

            Step("ready", () =>
            {
                _window.WaitUntilReady(w => w.EditorText(5).Contains("check042", StringComparison.Ordinal), 120, "the script to load");
                _window.WaitUntilReady(w => w.IsConnected(), 90, "the query window to attach to the server");
                _window.TopLevelMenu("AKML SQL", 180);
                DismissRestorePrompt();
                Note("connected: yes");
                Thread.Sleep(15000);                                   // the schema cache loads and analysis settles
                Windows("after start-up");
            });

            Step("menus", ListMenus);
            Step("error list (before anything is run or changed)", () => ErrorList("start"));
            Step("execute the second line, then SQL History", ExecuteAndHistory);
            Step("completion at the end of the script", Completion);
            Step("format document", FormatDocument);
            Step("error list (after formatting)", () => ErrorList("formatted"));
            Step("windows at the end", () => Windows("end"));
        }
        finally
        {
            Step("logs", () => LogFingerprint(sandbox));
            var file = Path.Combine(_outDir, $"observations-{label}.txt");
            File.WriteAllLines(file, _notes, Encoding.UTF8);
            output.WriteLine($"Wrote {file}");
        }
    }

    // ---- steps ----------------------------------------------------------------------------------

    private void ListMenus()
    {
        Note("top-level menus: " + string.Join(" | ", _window.TopLevelMenuNames()));
        var menu = _window.TopLevelMenu("AKML SQL", 60);
        Activate(menu);
        Thread.Sleep(900);
        var entries = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).ToList();
        Note("AKML SQL: " + string.Join(" | ", entries.Select(e => Clean(e.Name)).Where(n => n.Length > 0)));
        foreach (var entry in entries)
        {
            try
            {
                if (!entry.Patterns.ExpandCollapse.IsSupported
                    || entry.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.LeafNode) continue;
                entry.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(500);
                var inner = entry.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                    .Select(e => Clean(e.Name)).Where(n => n.Length > 0);
                Note($"AKML SQL > {Clean(entry.Name)}: " + string.Join(" | ", inner));
                entry.Patterns.ExpandCollapse.Pattern.Collapse();
            }
            catch (Exception ex) { Note($"AKML SQL > {Clean(entry.Name)}: (not listed: {ex.GetType().Name})"); }
        }
        for (var i = 0; i < 3; i++) { Keyboard.Press(VirtualKeyShort.ESCAPE); Thread.Sleep(200); }
        Shot.Element(_window.Raw, "check042-" + Label() + "-after-menus");
    }

    private void ErrorList(string tag)
    {
        var grid = _window.Raw.FindFirstDescendant(cf => cf.ByAutomationId("Tracking List View"));
        if (grid is null)
        {
            try
            {
                var view = _window.TopLevelMenu("View", 20);
                Activate(view);
                Thread.Sleep(700);
                var item = view.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                    .FirstOrDefault(m => Clean(m.Name).StartsWith("Error List", StringComparison.OrdinalIgnoreCase));
                if (item is not null) Activate(item);
                else Keyboard.Press(VirtualKeyShort.ESCAPE);
                Thread.Sleep(2000);
            }
            catch (Exception) { /* reported below as not reachable */ }
            grid = _window.Raw.FindFirstDescendant(cf => cf.ByAutomationId("Tracking List View"));
        }
        if (grid is null) { Note($"error list [{tag}]: not reachable"); return; }

        var cells = grid.FindAllDescendants().Select(e => Clean(e.Name)).Where(n => n.Length > 0).Distinct().Take(120).ToList();
        Note($"error list [{tag}]: {cells.Count} distinct cells");
        foreach (var cell in cells) Note("  | " + cell);
    }

    private void ExecuteAndHistory()
    {
        EnsureForeground();
        _window.Editor().Click();
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
        Keyboard.Type(VirtualKeyShort.DOWN);
        Keyboard.Type(VirtualKeyShort.HOME);
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.END);
        Thread.Sleep(200);
        Keyboard.Type(VirtualKeyShort.F5);

        var finished = WaitFor(() => _window.Raw.FindAllDescendants()
            .Select(e => e.Name ?? "").Any(n => n.Contains("Query executed", StringComparison.OrdinalIgnoreCase)
                                             || n.Contains("Query completed", StringComparison.OrdinalIgnoreCase)), 60);
        Note("execute: " + (finished ? "finished" : "no completion text seen within 60 s"));
        var status = _window.Raw.FindAllDescendants().Select(e => Clean(e.Name))
            .Where(n => n.StartsWith("Query ", StringComparison.OrdinalIgnoreCase) && n.Length < 120).Distinct();
        Note("status text: " + string.Join(" | ", status));
        Thread.Sleep(3000);                                            // the engine records the execution in the background

        var menu = _window.TopLevelMenu("AKML SQL", 30);
        Activate(menu);
        Thread.Sleep(700);
        var history = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .FirstOrDefault(m => Clean(m.Name).StartsWith("SQL History", StringComparison.OrdinalIgnoreCase));
        if (history is null) { Note("sql history: menu entry not found"); Keyboard.Press(VirtualKeyShort.ESCAPE); return; }
        Activate(history);
        Thread.Sleep(3500);
        var panel = _window.Raw.FindAllDescendants(cf => cf.ByControlType(ControlType.Pane))
            .FirstOrDefault(p => Clean(p.Name).Contains("SQL History", StringComparison.OrdinalIgnoreCase));
        if (panel is null) { Note("sql history: window not found"); return; }
        var texts = panel.FindAllDescendants().Select(e => Clean(e.Name)).Where(n => n.Length > 0).Distinct().Take(80).ToList();
        Note($"sql history: {texts.Count} distinct texts");
        foreach (var text in texts) Note("  | " + Shorten(text));
        Note("sql history lists the executed query: " + texts.Any(t => t.Contains("companyname", StringComparison.OrdinalIgnoreCase)
                                                                    && t.Contains("orderid", StringComparison.OrdinalIgnoreCase)));
        Shot.Element(_window.Raw, "check042-" + Label() + "-history");
        try { _window.CloseToolWindow("SQL History"); } catch (Exception) { /* leave it open rather than lose the document */ }
    }

    private void Completion()
    {
        EnsureForeground();
        _window.Editor().Click();
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Thread.Sleep(300);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SPACE);
        Thread.Sleep(4500);

        var popup = PopupItems();
        Note($"completion popup: {popup.Count} items" + (popup.Count > 0 ? " | " + string.Join(" | ", popup.Take(25)) : " (none readable)"));
        Shot.Element(_window.Raw, "check042-" + Label() + "-completion");
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(1500);
        Note("last line after the first suggestion is committed: " + Shorten(_window.EditorText().Replace("\r\n", "\n").Split('\n').Last()));
    }

    private void FormatDocument()
    {
        EnsureForeground();
        var menu = _window.TopLevelMenu("AKML SQL", 30);
        Activate(menu);
        Thread.Sleep(700);
        var item = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .FirstOrDefault(m => Clean(m.Name).StartsWith("Format Document", StringComparison.OrdinalIgnoreCase));
        if (item is null) { Note("format document: menu entry not found"); Keyboard.Press(VirtualKeyShort.ESCAPE); return; }
        Activate(item);
        Thread.Sleep(5000);
        var text = _window.EditorText().Replace("\r\n", "\n");
        Note("format document result:");
        foreach (var line in text.Split('\n')) Note("  | " + line);
        Shot.Element(_window.Raw, "check042-" + Label() + "-formatted");
    }

    // ---- observations that are not a step of their own --------------------------------------------

    private void Windows(string when)
    {
        var desktop = _window.Raw.Automation.GetDesktop();
        var shown = desktop.FindAllChildren(cf => cf.ByProcessId(_pid))
            .Select(w => $"{Clean(w.Name)} [{w.ClassName}]").Where(n => !n.StartsWith(" [")).Distinct().ToList();
        Note($"windows of ssms [{when}]: {shown.Count}");
        foreach (var w in shown) Note("  | " + Shorten(w));
        var modals = _window.Raw.ModalWindows.Select(m => Clean(m.Title)).ToList();
        Note($"modal dialogs [{when}]: " + (modals.Count == 0 ? "none" : string.Join(" | ", modals)));
    }

    private void LogFingerprint(string sandbox)
    {
        var folder = Path.Combine(sandbox, "AKML SQL", "logs");
        if (!Directory.Exists(folder)) { Note("logs: no folder " + folder); return; }
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = 0;
        foreach (var file in Directory.GetFiles(folder, "*.log"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lines++;
                if (!line.Contains("[WRN]") && !line.Contains("[ERR]") && !line.Contains("[FTL]")) continue;
                var normal = Shorten(Regex.Replace(line[(line.IndexOf(" [", StringComparison.Ordinal) + 1)..], @"\d+", "#"));
                counts[normal] = counts.GetValueOrDefault(normal) + 1;
            }
        }
        Note($"logs: {lines} lines, {counts.Count} distinct warning/error messages");
        foreach (var (message, n) in counts) Note($"  | x{n}  {message}");
    }

    private static string EngineFacts()
    {
        var folder = @"C:\Program Files (x86)\AKML SQL\Engine";
        var exe = Path.Combine(folder, "AkmlSql.Engine.exe");
        if (!File.Exists(exe)) return "not found at " + folder;
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
        return $"{version}; AkmlSql.PostgreSql.dll {(File.Exists(Path.Combine(folder, "AkmlSql.PostgreSql.dll")) ? "present" : "absent")}";
    }

    // ---- small helpers ---------------------------------------------------------------------------------

    private List<string> PopupItems()
    {
        var items = new List<string>();
        try
        {
            var desktop = _window.Raw.Automation.GetDesktop();
            foreach (var top in desktop.FindAllChildren(cf => cf.ByProcessId(_pid)))
            {
                if (top.Properties.NativeWindowHandle.ValueOrDefault == _window.Raw.Properties.NativeWindowHandle.ValueOrDefault) continue;
                items.AddRange(top.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Select(i => Clean(i.Name)).Where(n => n.Length > 0));
            }
        }
        catch (Exception) { /* best effort: the committed text is the observation that counts */ }
        return items;
    }

    private void DismissRestorePrompt()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var modal in _window.Raw.ModalWindows)
            {
                if (!(modal.Title ?? "").Contains("Restore queries", StringComparison.OrdinalIgnoreCase)) continue;
                var notNow = modal.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                    .FirstOrDefault(b => string.Equals((b.Name ?? "").Trim(), "Not now", StringComparison.OrdinalIgnoreCase));
                if (notNow is null) continue;
                Note("prompt seen: Restore queries (answered Not now)");
                notNow.AsButton().Invoke();
                Thread.Sleep(800);
                return;
            }
            Thread.Sleep(500);
        }
    }

    private void EnsureForeground()
    {
        _window.BringToFront();
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        if (owner != (uint)_pid)
            throw new InvalidOperationException("SSMS is not the foreground window; refusing to send keystrokes to whatever is.");
    }

    private void Step(string name, Action body)
    {
        Note($"## {name}");
        try { body(); }
        catch (Exception ex) { Note($"STEP FAILED: {ex.GetType().Name}: {Shorten(ex.Message.Split('\n')[0])}"); }
    }

    private void Note(string line) { _notes.Add(line); output.WriteLine(line); }

    private static bool WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (condition()) return true; } catch (Exception) { /* the tree churns while SSMS works */ }
            Thread.Sleep(500);
        }
        return false;
    }

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

    private static string Clean(string? text) => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";

    private static string Label() => Environment.GetEnvironmentVariable(LabelVariable) ?? "run";
}

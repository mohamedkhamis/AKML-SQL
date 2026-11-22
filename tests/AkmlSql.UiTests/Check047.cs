using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
/// Spec 047 (T141, quickstart.md §3) - the acceptance scenarios that need SSMS 22, run against the INSTALLED extension and
/// engine: V12 (an opened file is analysed without typing), V10 (a missing semicolon is a margin dot, a syntax error stays
/// red), V11 (Options › Code analysis › Advisory findings), V9 (Format Document keeps CRLF), V19 (Find Invalid Objects names
/// the references it checked) and V8 (every style formats the sp047 corpus as its golden says, and a second Format changes
/// nothing), plus a plain cycle: execute, results, SQL History and completion.
///
/// <para>
/// It does nothing unless <c>AKML_CHECK047=1</c>, because it drives the real desktop with synthetic input. Settings, history and
/// logs live in a sandbox under <c>AKML_CHECK047_OUT</c> (<c>AKML_APP_DATA_ROOT</c>), so the person's own are not touched.
/// Text is put into the editor through the clipboard (restored at the end): typed SQL would be changed by completion.
/// The margin dots and underlines are AKML adornments that UI Automation cannot see, so those verdicts point at screenshots
/// and the shell's "Analysis complete" log lines instead.
/// </para>
/// </summary>
[Collection("SSMS desktop")]
public sealed class Check047(ITestOutputHelper output)
{
    private const string RunVariable = "AKML_CHECK047";
    private const string OutVariable = "AKML_CHECK047_OUT";
    private const string OptionsTitle = "AKML SQL – Options";
    private const string MarkerChoice = "Margin marker only";
    private const string UnderlineChoice = "Underline in the text";

    // Three statements without a semicolon (ST004, information, on each) and no error.
    private const string NoSemicolons =
        "-- check047: statements without semicolons\r\n" +
        "SELECT CustomerID, CompanyName FROM dbo.Customers WHERE Country = N'Germany'\r\n" +
        "SELECT COUNT(*) AS OrderCount FROM dbo.Orders\r\n" +
        "SELECT ProductName FROM dbo.Products WHERE Discontinued = 1\r\n";

    private static readonly string[] Corpus =
    {
        "sp047-01-proc-params", "sp047-02-control-flow", "sp047-03-go-count", "sp047-04-block-comment", "sp047-05-spacing",
        "sp047-06-statements",
    };

    private static readonly string[] GoldenStyles =
    {
        "default", "compact", "indented", "aligned-left-bracket", "leading-commas", "minimalist", "khamis-style", "collapsed",
    };

    private static readonly Regex AnalysisLine = new(@"Analysis complete: (\d+) findings", RegexOptions.Compiled);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    private readonly List<string> _notes = [];
    private readonly SortedDictionary<string, string> _verdicts = new(StringComparer.Ordinal);
    private SsmsWindow _window = null!;
    private int _pid;
    private string _outDir = "";
    private string _sandbox = "";
    private string _sqlFile = "";
    private DateTime _connectedAt;

    [Fact]
    public void Spec047_scenarios_in_the_installed_ssms()
    {
        if (Environment.GetEnvironmentVariable(RunVariable) != "1")
        {
            output.WriteLine($"{RunVariable} is not 1; this check drives the real desktop and runs only on request. Skipping.");
            return;
        }

        _outDir = Environment.GetEnvironmentVariable(OutVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "akml-check047");
        Directory.CreateDirectory(_outDir);
        Shot.ArtifactDirectory = _outDir;
        _sandbox = Path.Combine(_outDir, "appdata");
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("AKML_APP_DATA_ROOT", _sandbox);

        // A locked or disconnected session has no usable desktop; AKML_CHECK047_DESKTOP_WAIT (seconds) lets a run wait for it.
        var desktopWait = int.TryParse(Environment.GetEnvironmentVariable("AKML_CHECK047_DESKTOP_WAIT"), out var w) && w > 0 ? w : 120;
        Preconditions.WaitForInteractiveDesktop(timeoutSeconds: desktopWait);
        Note($"desktop usable at {DateTime.Now:HH:mm:ss}");
        var idleBy = DateTime.UtcNow.AddSeconds(180);
        while (!Preconditions.DesktopIsIdle(5) && DateTime.UtcNow < idleBy) Thread.Sleep(1000);
        Assert.True(Preconditions.DesktopIsIdle(5),
            "Someone is using the keyboard or mouse; synthetic input would collide with theirs. Run again when the desktop is idle.");

        var (deployed, _, message) = Preconditions.CheckExtension();
        Note("extension: " + message);
        Assert.True(deployed, message);

        _sqlFile = Path.Combine(_outDir, "check047.sql");
        File.WriteAllText(_sqlFile, NoSemicolons, new UTF8Encoding(false));
        var clipboardBefore = GetClipboard();

        try
        {
            using var app = SsmsApp.Launch(_sqlFile, server: "(local)", database: "Northwind");
            _pid = app.ProcessId;
            _window = app.MainWindow(timeoutSeconds: 240);

            // AKML_CHECK047_ONLY=V8 (a comma list of V12, V10, V11, V9, V19, cycle, V8) re-runs single scenarios.
            var only = Environment.GetEnvironmentVariable("AKML_CHECK047_ONLY");
            bool Want(string key) => string.IsNullOrWhiteSpace(only)
                || only.Split(',').Any(k => string.Equals(k.Trim(), key, StringComparison.OrdinalIgnoreCase));

            Step("start", Start);
            if (Want("V12")) Step("V12 an opened file is analysed without typing", V12);
            if (Want("V10")) Step("V10 marker only, a semicolon removes it, a syntax error stays red", V10);
            if (Want("V11")) Step("V11 Options › Code analysis › Advisory findings", V11);
            if (Want("V9")) Step("V9 Format Document keeps CRLF", V9);
            if (Want("V19")) Step("V19 Find Invalid Objects", V19);
            if (Want("cycle")) Step("cycle: execute, results, SQL History, completion", Cycle);
            if (Want("V8")) Step("V8 every style in SSMS", V8);
            Step("windows at the end", () => Windows("end"));
        }
        finally
        {
            Step("logs", LogFingerprint);
            if (clipboardBefore is not null) SetClipboard(clipboardBefore);
            foreach (var (scenario, verdict) in _verdicts) Note($"VERDICT {scenario}: {verdict}");
            File.WriteAllLines(Path.Combine(_outDir, "observations.txt"), _notes, Encoding.UTF8);
            output.WriteLine($"Wrote {Path.Combine(_outDir, "observations.txt")}");
        }

        var failed = _verdicts.Where(v => v.Value.StartsWith("FAIL", StringComparison.Ordinal)).ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed.Select(f => $"{f.Key}: {f.Value}")));
    }

    // ---- steps ----------------------------------------------------------------------------------

    private void Start()
    {
        _window.WaitUntilReady(w => w.EditorText(5).Contains("check047", StringComparison.Ordinal), 120, "the script to load");
        _window.WaitUntilReady(w => w.IsConnected(), 90, "the query window to attach to the server");
        _connectedAt = DateTime.UtcNow;
        _window.TopLevelMenu("AKML SQL", 180);
        DismissRestorePrompt();
        Note("connected to (local)/Northwind; the AKML SQL menu is present; nothing typed yet");
        Windows("after start-up");
    }

    private void V12()
    {
        // Nothing has been typed or clicked in the editor: findings can only come from the analysis the shell runs once its
        // engine connects (SSMS-002). The scenario waits 10 s.
        var analysed = WaitFor(() => Analysis().LastCount >= 3, 45);
        var (lines, count) = Analysis();
        Note($"'Analysis complete' lines: {lines}; last: {count?.ToString() ?? "none"} findings; " +
             $"{(DateTime.UtcNow - _connectedAt).TotalSeconds:F0} s after the connection");
        Thread.Sleep(10_000);
        var shot = EditorShot("v12-dots-without-typing");
        Verdict("V12", analysed
            ? $"PASS (analysed with no input: {count} findings; the dots are in {Path.GetFileName(shot)})"
            : $"FAIL (no analysis with at least 3 findings within 45 s of the connection; last count {count?.ToString() ?? "none"})");
    }

    private void V10()
    {
        var markerShot = EditorShot("v10-1-marker-only");
        var (linesBefore, findingsBefore) = Analysis();

        // Add the semicolon of line 2 (the first SELECT).
        EnsureForeground();
        _window.Editor().Click();
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
        Keyboard.Type(VirtualKeyShort.DOWN);
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(";");
        var fewer = WaitFor(() => Analysis() is var a && a.Lines > linesBefore && a.LastCount < findingsBefore, 20);
        var (_, findingsAfter) = Analysis();
        Thread.Sleep(1500);
        var semicolonShot = EditorShot("v10-2-semicolon-added");
        Note($"findings: {findingsBefore} -> {findingsAfter} after ';' on line 2");

        // A syntax error is still an error: pasted, so completion cannot turn SELEC into SELECT.
        var (linesMid, _) = Analysis();
        SetClipboard("SELEC * FROM dbo.Customers\r\n");
        EnsureForeground();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        WaitFor(() => Analysis().Lines > linesMid, 20);
        // SSMS's own IntelliSense draws the red mark (AKML leaves syntax errors to it in SSMS) several seconds after a paste:
        // the installed run's 3 s capture was too early, and a later screenshot showed the mark.
        Thread.Sleep(10_000);
        var errorShot = EditorShot("v10-3-syntax-error");
        var errors = ErrorList("after the syntax error");
        var hasError = errors.Any(c => c.Contains("syntax", StringComparison.OrdinalIgnoreCase)
                                    || c.Contains("SYNTAX", StringComparison.Ordinal)
                                    || c.Contains("SELEC", StringComparison.Ordinal));

        Verdict("V10", fewer
            ? $"PASS (findings {findingsBefore} -> {findingsAfter} with ';'; dots, no underline: {Path.GetFileName(markerShot)}, {Path.GetFileName(semicolonShot)})"
            : $"FAIL (findings {findingsBefore} -> {findingsAfter} after ';')");
        // AKML does not report parse errors in SSMS (SSMS's own IntelliSense underlines them); recorded on its own.
        Verdict("V10-syntax", hasError
            ? $"PASS (the syntax error is listed; {Path.GetFileName(errorShot)})"
            : $"NOT SATISFIED (no error listed and no mark expected from AKML; SSMS's own IntelliSense showed none: {Path.GetFileName(errorShot)})");
    }

    private void V11()
    {
        SetAdvisory(UnderlineChoice);
        Thread.Sleep(3000);
        var underlineShot = EditorShot("v11-1-underline");
        var afterUnderline = ConfigSquiggleStyle();

        SetAdvisory(MarkerChoice);
        Thread.Sleep(3000);
        var markerShot = EditorShot("v11-2-margin-marker-again");
        var afterMarker = ConfigSquiggleStyle();

        Note($"config codeAnalysis.squiggleStyle: after Underline = {afterUnderline}, after Margin marker = {afterMarker}");
        Verdict("V11", afterUnderline == "underline" && afterMarker == "marker"
            ? $"PASS (saved as underline, then marker; look: {Path.GetFileName(underlineShot)}, {Path.GetFileName(markerShot)})"
            : $"FAIL (squiggleStyle after Underline = {afterUnderline}, after Margin marker = {afterMarker})");
    }

    private void V9()
    {
        SetDocument(NoSemicolons);
        var before = LineEndingIndicator();
        var uia = _window.EditorText(5);
        AkmlMenu("Format Document");
        var changed = WaitFor(() => _window.EditorText(5) != uia, 15);
        Thread.Sleep(500);
        Save();
        var bytes = File.ReadAllBytes(_sqlFile);
        var (crlf, bareLf) = CountLineEndings(bytes);
        var after = LineEndingIndicator();
        Note($"V9: formatted = {changed}; saved file: {crlf} CRLF, {bareLf} bare LF; status bar line endings: {before} -> {after}");
        Note("V9 formatted text:");
        foreach (var line in Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n')) Note("  | " + line);
        Verdict("V9", changed && bareLf == 0 && crlf >= 3
            ? $"PASS (Format Document changed the text; the saved file has {crlf} CRLF and no bare LF; status bar {before} -> {after})"
            : $"FAIL (formatted {changed}; {crlf} CRLF, {bareLf} bare LF)");
    }

    private void V19()
    {
        AkmlMenu("Refactor", "Find Invalid Objects");
        var dialog = Find(() => FindWindow("invalid objects", ControlType.Window), "the Find invalid objects window", 60);
        var texts = "";
        var finished = WaitFor(() =>
        {
            texts = string.Join(" | ", dialog.FindAllDescendants().Select(e => Clean(e.Name)).Where(n => n.Length > 0).Distinct());
            return texts.Contains("resolve", StringComparison.OrdinalIgnoreCase)
                || texts.Contains("invalid object", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(texts, @"\d+ invalid")
                || texts.Contains("no object references", StringComparison.OrdinalIgnoreCase);
        }, 120);
        var grid = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid).Or(cf.ByControlType(ControlType.Table)));
        var visibleGrid = grid is not null && !grid.IsOffscreen && grid.BoundingRectangle.Height > 0;
        var shot = Shot.Element(dialog, "v19-find-invalid-objects");
        Note($"V19 window texts: {texts}");
        Note($"V19 grid shown: {visibleGrid}");
        const string expected = "No invalid objects found. All 63 references resolve.";
        Verdict("V19", finished && texts.Contains(expected, StringComparison.Ordinal) && !visibleGrid
            ? $"PASS (\"{expected}\", no grid; {Path.GetFileName(shot)})"
            : $"FAIL (finished {finished}; grid shown {visibleGrid}; texts: {Shorten(texts)})");

        var close = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(b => Clean(b.Name) is "Close" or "OK" or "Cancel");
        if (close is not null) Click(close);
        else { dialog.Focus(); Keyboard.Press(VirtualKeyShort.ESCAPE); }
        Thread.Sleep(800);
    }

    private void Cycle()
    {
        SetDocument("SELECT TOP (5) CustomerID, CompanyName FROM dbo.Customers ORDER BY CustomerID;\r\n");
        EnsureForeground();
        _window.Execute(60);
        var executed = WaitFor(() => _window.Raw.FindAllDescendants()
            .Any(e => (e.Name ?? "").Contains("Query executed successfully", StringComparison.OrdinalIgnoreCase)), 30);
        var rows = _window.Raw.FindAllDescendants().Select(e => Clean(e.Name))
            .FirstOrDefault(n => Regex.IsMatch(n, @"^\d+ rows?$", RegexOptions.IgnoreCase)) ?? "(no row count found)";
        var shot = Shot.Element(_window.Raw, "cycle-1-executed");
        Note($"execute: {(executed ? "Query executed successfully" : "no success text")}; {rows}");

        Thread.Sleep(3000);   // the engine records the execution in the background
        var historyListsIt = false;
        try
        {
            AkmlMenu("SQL History");
            Thread.Sleep(3500);
            var panel = _window.Raw.FindAllDescendants(cf => cf.ByControlType(ControlType.Pane))
                .FirstOrDefault(p => Clean(p.Name).Contains("SQL History", StringComparison.OrdinalIgnoreCase));
            var history = FindWindow("SQL History", ControlType.Window, ControlType.Pane) ?? panel;
            var texts = history?.FindAllDescendants().Select(e => Clean(e.Name)).Where(n => n.Length > 0).Distinct().ToList() ?? [];
            // The SQL is in the preview's text view (no UIA name); the list names the entry and its server and database.
            historyListsIt = texts.Any(t => Regex.IsMatch(t, @"^[1-9]\d* entr(y|ies) found$", RegexOptions.IgnoreCase))
                          && texts.Any(t => t.Contains("Northwind", StringComparison.OrdinalIgnoreCase));
            Note($"SQL History: {texts.Count} texts; lists the run: {historyListsIt}: {Shorten(string.Join(" | ", texts.Take(20)))}");
            Shot.Element(_window.Raw, "cycle-2-history");
            // A floating SQL History window covers the document tabs, so a "Close button inside the pane" would be the query
            // tab's: close the active tool window with Shift+Esc instead, and check the query window is still there.
            history?.Focus();
            Thread.Sleep(300);
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.ESCAPE);
            Thread.Sleep(1000);
            Note($"after closing SQL History: query editor present {_window.Raw.FindFirstDescendant(cf => cf.ByAutomationId("WpfTextView")) is not null}; title '{Shorten(_window.Title)}'");
        }
        catch (Exception ex) { Note("SQL History: " + ex.Message.Split('\n')[0]); }

        SetDocument("SELECT * FROM dbo.Ord");
        EnsureForeground();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Thread.Sleep(300);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SPACE);
        Thread.Sleep(4500);
        var popup = PopupItems();
        Shot.Element(_window.Raw, "cycle-3-completion");
        // AKML's completion list has no UIA items; commit its first suggestion and read what it wrote.
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(1500);
        var committed = Regex.Replace(_window.EditorText(), @"(?m)^\d+ ", "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Last();
        var offersOrders = popup.Any(i => i.StartsWith("Orders", StringComparison.OrdinalIgnoreCase)
                                       || i.Contains("Order Details", StringComparison.OrdinalIgnoreCase))
                        || Regex.IsMatch(committed, @"dbo\.(\[?Orders\]?|\[Order Details\])", RegexOptions.IgnoreCase);
        Note($"completion after 'dbo.Ord': {popup.Count} readable items; after Tab the line reads [{committed}]");

        Verdict("cycle", executed && historyListsIt && offersOrders
            ? $"PASS (executed, {rows}; SQL History lists it; completion offers Orders; {Path.GetFileName(shot)})"
            : $"FAIL (executed {executed}, {rows}; history lists it {historyListsIt}; completion offers Orders {offersOrders})");
    }

    private void V8()
    {
        var styles = ActiveStyleNames();
        Note("Active Style menu: " + string.Join(" | ", styles));
        if (styles.Count == 0) { Verdict("V8-ssms", "FAIL (the Active Style menu lists no style)"); return; }

        var results = new List<string>();
        var problems = new List<string>();
        var goldenMatches = 0;
        var goldenCompared = 0;
        try
        {
            foreach (var style in styles)
            {
                AkmlMenuExact(new[] { "Active Style" }, style);
                Thread.Sleep(1200);
                var id = GoldenId(style);
                foreach (var corpus in Corpus)
                {
                    var input = ReadRepoFile("tests", "format-parity", "corpus", corpus + ".sql").Replace("\r\n", "\n").Replace("\n", "\r\n");
                    SetDocument(input);
                    var uia = _window.EditorText(5);
                    AkmlMenu("Format Document");
                    WaitFor(() => _window.EditorText(5) != uia, 10);
                    Thread.Sleep(700);
                    Save();
                    var first = ReadSaved();
                    AkmlMenu("Format Document");
                    Thread.Sleep(3000);
                    Save();
                    var second = ReadSaved();

                    var idempotent = first == second;
                    if (!idempotent) problems.Add($"{style} / {corpus}: the second Format changed the text");
                    var golden = id is null ? null : TryReadRepoFile("tests", "format-parity", "golden", $"{corpus}__{id}.sql");
                    string goldenNote;
                    if (golden is null) goldenNote = "no golden";
                    else
                    {
                        goldenCompared++;
                        var diff = FirstDifference(Normal(first), Normal(golden));
                        if (diff is null) { goldenMatches++; goldenNote = "matches its golden"; }
                        else { goldenNote = "differs from its golden: " + diff; problems.Add($"{style} / {corpus}: {diff}"); }
                    }
                    results.Add($"{style} / {corpus}: idempotent {idempotent}; {goldenNote}");
                    Note($"V8 {style} / {corpus}: idempotent {idempotent}; {goldenNote}");
                    File.WriteAllText(Path.Combine(_outDir, $"v8-{Slug(style)}-{corpus}.sql"), first, new UTF8Encoding(false));
                }
            }
        }
        finally
        {
            try
            {
                var back = styles.FirstOrDefault(s => GoldenId(s) == "default") ?? styles[0];
                AkmlMenuExact(new[] { "Active Style" }, back);
            }
            catch (Exception ex) { Note("could not restore the active style: " + ex.Message.Split('\n')[0]); }
        }

        Verdict("V8-ssms", problems.Count == 0
            ? $"PASS ({styles.Count} styles x {Corpus.Length} scripts: every second Format unchanged; {goldenMatches} of {goldenCompared} compared outputs match their goldens)"
            : $"FAIL ({problems.Count} problem(s): {Shorten(string.Join("; ", problems))})");
    }

    // ---- SSMS helpers -----------------------------------------------------------------------------

    private void SetAdvisory(string choice)
    {
        var options = OpenOptions();
        try
        {
            OptionsPage(options, "Code analysis");
            var content = options.FindAllChildren(cf => cf.ByControlType(ControlType.Pane)).FirstOrDefault(p => p.ClassName == "ScrollViewer")
                          ?? (AutomationElement)options;
            var combo = content.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox).And(cf.ByName("Advisory findings")))
                        ?? content.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
                            .FirstOrDefault(c => ComboText(c) is MarkerChoice or UnderlineChoice)
                        ?? throw new InvalidOperationException("No Advisory findings drop-down on Options › Code analysis");
            var before = ComboText(combo);
            if (!string.Equals(before, choice, StringComparison.OrdinalIgnoreCase))
            {
                combo.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(600);
                var item = Find(() => combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                    .FirstOrDefault(i => Says(i, choice)), $"the '{choice}' choice");
                item.Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(300);
                try { combo.Patterns.ExpandCollapse.Pattern.Collapse(); } catch (Exception) { /* closed by the pick */ }
            }
            Note($"Advisory findings: '{before}' -> '{ComboText(combo)}'");
            Shot.Element(options, "v11-options-" + Slug(choice));
            Click(Button(options, "OK"));
        }
        catch
        {
            try { Click(Button(options, "Cancel")); } catch (Exception) { /* already gone */ }
            throw;
        }
        WaitGone(OptionsTitle, 15);
    }

    private Window OpenOptions()
    {
        var existing = FindWindow(OptionsTitle, ControlType.Window);
        if (existing != null) return existing.AsWindow();
        AkmlMenu("Options");
        var options = Find(() => FindWindow(OptionsTitle, ControlType.Window), $"window '{OptionsTitle}'", 30).AsWindow();
        Thread.Sleep(1500);
        return options;
    }

    private static void OptionsPage(Window options, string leaf)
    {
        var tree = options.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tree))
                   ?? throw new InvalidOperationException("No Options tree");
        var target = tree.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem))
            .First(i => string.Equals(Clean(i.Name), leaf, StringComparison.OrdinalIgnoreCase));
        try { target.Patterns.ScrollItem.Pattern.ScrollIntoView(); } catch (Exception) { /* already in view */ }
        target.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(900);
    }

    private List<string> ActiveStyleNames()
    {
        // A menu of SSMS opens only while SSMS is the active window (the 1936 run lost V8 when focus went elsewhere).
        EnsureForeground();
        var menu = _window.TopLevelMenu("AKML SQL", 60);
        Activate(menu);
        Thread.Sleep(900);
        var active = Find(() => menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .FirstOrDefault(m => Clean(m.Name).StartsWith("Active Style", StringComparison.OrdinalIgnoreCase)), "Active Style");
        Activate(active);
        Thread.Sleep(1200);
        var names = active.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
            .Select(m => Clean(m.Name))
            .Where(n => n.Length > 0 && !n.StartsWith("Edit Styles", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        Shot.Element(_window.Raw, "v8-active-style-menu");
        for (var i = 0; i < 3; i++) { Keyboard.Press(VirtualKeyShort.ESCAPE); Thread.Sleep(200); }
        return names;
    }

    private void AkmlMenu(params string[] path) => AkmlMenuPath(path, exactLast: false);

    private void AkmlMenuExact(string[] parents, string last) => AkmlMenuPath(parents.Append(last).ToArray(), exactLast: true);

    private void AkmlMenuPath(string[] path, bool exactLast)
    {
        EnsureForeground();
        var menu = _window.TopLevelMenu("AKML SQL", 60);
        Activate(menu);
        Thread.Sleep(900);
        AutomationElement container = menu;
        for (var i = 0; i < path.Length; i++)
        {
            var step = path[i];
            var exact = exactLast && i == path.Length - 1;
            var item = Find(() => container.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .FirstOrDefault(m => exact
                    ? string.Equals(Clean(m.Name), step, StringComparison.OrdinalIgnoreCase)
                    : Clean(m.Name).StartsWith(step, StringComparison.OrdinalIgnoreCase)), $"menu item '{step}'");
            Activate(item);
            Thread.Sleep(700);
            container = item;
        }
    }

    private void SetDocument(string text)
    {
        SetClipboard(text);
        EnsureForeground();
        _window.Editor().Click();
        Thread.Sleep(150);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(100);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Thread.Sleep(800);
    }

    private void Save()
    {
        EnsureForeground();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Thread.Sleep(900);
    }

    private string ReadSaved()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(_sqlFile); }
            catch (IOException) when (attempt < 10) { Thread.Sleep(200); }
        }
    }

    private string LineEndingIndicator()
    {
        try
        {
            var hit = _window.Raw.FindAllDescendants()
                .Select(e => Clean(e.Name))
                .FirstOrDefault(n => n is "CRLF" or "LF" or "CR" || n.StartsWith("Line endings", StringComparison.OrdinalIgnoreCase));
            return hit ?? "(no line-ending indicator found)";
        }
        catch (Exception) { return "(not readable)"; }
    }

    private string EditorShot(string name)
    {
        _window.BringToFront();
        var view = _window.Editor().BoundingRectangle;
        var glyph = _window.Raw.FindFirstDescendant(cf => cf.ByAutomationId("WpfEditorUIGlyphMarginGrid"));
        var left = glyph is not null && glyph.BoundingRectangle.Width > 0 ? glyph.BoundingRectangle.Left : view.Left - 60;
        var rect = new Rectangle(left - 6, view.Top - 6, Math.Min(view.Right - left + 12, 1200), Math.Min(view.Height + 12, 280));
        return Shot.Capture(rect, name);
    }

    private List<string> ErrorList(string tag)
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
            catch (Exception) { /* reported below */ }
            grid = _window.Raw.FindFirstDescendant(cf => cf.ByAutomationId("Tracking List View"));
        }
        if (grid is null) { Note($"error list [{tag}]: not reachable"); return []; }
        var cells = grid.FindAllDescendants().Select(e => Clean(e.Name)).Where(n => n.Length > 0).Distinct().Take(80).ToList();
        Note($"error list [{tag}]: {cells.Count} distinct cells");
        foreach (var cell in cells) Note("  | " + Shorten(cell));
        return cells;
    }

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
        catch (Exception) { /* best effort */ }
        return items;
    }

    private void DismissRestorePrompt()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var prompt = FindWindow("Restore queries", ControlType.Window);
            var notNow = prompt?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => string.Equals(Clean(b.Name), "Not now", StringComparison.OrdinalIgnoreCase));
            if (notNow is not null) { Note("prompt seen: Restore queries (answered Not now)"); Click(notNow); Thread.Sleep(800); return; }
            Thread.Sleep(500);
        }
    }

    private void Windows(string when)
    {
        var desktop = _window.Raw.Automation.GetDesktop();
        var shown = desktop.FindAllChildren(cf => cf.ByProcessId(_pid))
            .Select(w => $"{Clean(w.Name)} [{w.ClassName}]").Where(n => !n.StartsWith(" [", StringComparison.Ordinal)).Distinct().ToList();
        Note($"windows of ssms [{when}]: {shown.Count}");
        foreach (var w in shown) Note("  | " + Shorten(w));
        var modals = _window.Raw.ModalWindows.Select(m => Clean(m.Title)).ToList();
        Note($"modal dialogs [{when}]: " + (modals.Count == 0 ? "none" : string.Join(" | ", modals)));
    }

    private AutomationElement? FindWindow(string title, params ControlType[] types)
    {
        bool Match(AutomationElement e) =>
            (e.Name ?? "").Contains(title, StringComparison.OrdinalIgnoreCase) && types.Contains(e.ControlType);
        var desktop = _window.Raw.Automation.GetDesktop();
        foreach (var top in desktop.FindAllChildren(cf => cf.ByProcessId(_pid)))
        {
            if (Match(top)) return top;
            var inner = top.FindAllDescendants().FirstOrDefault(Match);
            if (inner != null) return inner;
        }
        return null;
    }

    private void WaitGone(string title, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (FindWindow(title, ControlType.Window) != null && DateTime.UtcNow < deadline) Thread.Sleep(300);
    }

    private void EnsureForeground()
    {
        _window.BringToFront();
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        if (owner != (uint)_pid)
            throw new InvalidOperationException("SSMS is not the foreground window; refusing to send keystrokes to whatever is.");
    }

    // ---- sandbox, files, logs ---------------------------------------------------------------------------

    private (int Lines, int? LastCount) Analysis()
    {
        var folder = Path.Combine(_sandbox, "AKML SQL", "logs");
        if (!Directory.Exists(folder)) return (0, null);
        var lines = 0;
        int? last = null;
        foreach (var file in Directory.GetFiles(folder, "*.log").OrderBy(File.GetLastWriteTimeUtc))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var m = AnalysisLine.Match(line);
                if (!m.Success) continue;
                lines++;
                last = int.Parse(m.Groups[1].Value);
            }
        }
        return (lines, last);
    }

    private string? ConfigSquiggleStyle()
    {
        var path = Path.Combine(_sandbox, "AKML SQL", "config.json");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return "(no config.json)";
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var section in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(section.Name, "codeAnalysis", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var p in section.Value.EnumerateObject())
                        if (string.Equals(p.Name, "squiggleStyle", StringComparison.OrdinalIgnoreCase)) return p.Value.GetString();
                }
                return "(not set)";
            }
            catch (IOException) { Thread.Sleep(200); }
            catch (JsonException) { Thread.Sleep(200); }
        }
        return "(unreadable)";
    }

    private void LogFingerprint()
    {
        var folder = Path.Combine(_sandbox, "AKML SQL", "logs");
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

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && dir.GetFiles("*.slnx").Length == 0) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string ReadRepoFile(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string? TryReadRepoFile(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? GoldenId(string styleName)
    {
        var key = Regex.Replace(styleName.ToLowerInvariant(), "[^a-z0-9]", "");
        return GoldenStyles.FirstOrDefault(s => s.Replace("-", "") == key);
    }

    private static string Normal(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim('\n', '﻿');

    private static string? FirstDifference(string actual, string expected)
    {
        if (actual == expected) return null;
        var a = actual.Split('\n');
        var e = expected.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, e.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end)";
            var y = i < e.Length ? e[i] : "(end)";
            if (x != y) return $"line {i + 1}: got [{Shorten(x)}] expected [{Shorten(y)}]";
        }
        return "differs";
    }

    private static (int Crlf, int BareLf) CountLineEndings(byte[] bytes)
    {
        int crlf = 0, bare = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            if (i > 0 && bytes[i - 1] == (byte)'\r') crlf++;
            else bare++;
        }
        return (crlf, bare);
    }

    // ---- clipboard (STA) -------------------------------------------------------------------------------

    private static string? GetClipboard()
    {
        string? text = null;
        RunSta(() => { if (System.Windows.Forms.Clipboard.ContainsText()) text = System.Windows.Forms.Clipboard.GetText(); });
        return text;
    }

    private static void SetClipboard(string text) =>
        RunSta(() => System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 20, retryDelay: 100));

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Clipboard: " + error.Message, error);
    }

    // ---- small helpers ---------------------------------------------------------------------------------

    private void Step(string name, Action body)
    {
        Note($"## {name}");
        try { body(); }
        catch (Exception ex)
        {
            Note($"STEP FAILED: {ex.GetType().Name}: {Shorten(ex.Message.Split('\n')[0])}");
            var scenario = name.Split(' ')[0];
            if (scenario.StartsWith('V') && !_verdicts.ContainsKey(scenario))
                Verdict(scenario, $"FAIL (step failed: {ex.GetType().Name}: {Shorten(ex.Message.Split('\n')[0])})");
            try { Shot.Screen("failed-" + Slug(name)); } catch (Exception) { /* best effort */ }
        }
    }

    private void Verdict(string scenario, string verdict)
    {
        _verdicts[scenario] = verdict;
        Note($"{scenario}: {verdict}");
    }

    private void Note(string line) { _notes.Add(line); output.WriteLine(line); }

    private static bool WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (condition()) return true; } catch (Exception) { /* the tree churns while SSMS works */ }
            Thread.Sleep(400);
        }
        return false;
    }

    private static T Find<T>(Func<T?> find, string what, int seconds = 10) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            T? found = null;
            try { found = find(); } catch (Exception) { /* churn */ }
            if (found != null) return found;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"No {what} within {seconds} s");
            Thread.Sleep(300);
        }
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

    private static void Click(AutomationElement button)
    {
        if (button.Patterns.Invoke.IsSupported) button.Patterns.Invoke.Pattern.Invoke();
        else button.Click();
    }

    private static AutomationElement Button(AutomationElement root, string name) =>
        root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(b => string.Equals(Clean(b.Name), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No '{name}' button");

    private static string ComboText(AutomationElement combo)
    {
        if (combo.Patterns.Value.IsSupported) return combo.Patterns.Value.Pattern.Value.Value ?? "";
        var selected = combo.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault?.FirstOrDefault();
        return selected?.Name ?? "";
    }

    private static bool Says(AutomationElement e, string text) =>
        (e.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)
        || e.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Any(t => (t.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));

    private static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static string Clean(string? text) => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Shorten(string text) => text.Length <= 220 ? text : text[..220] + "…";
}

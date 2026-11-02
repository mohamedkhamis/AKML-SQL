using AkmlSql.UiTests.Driver;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.UiTests;

[Collection("SSMS desktop")]
public sealed class InstalledSsmsCycleTests(ITestOutputHelper output)
{
    [Fact]
    public void Installed_extension_formats_and_executes_a_read_only_query()
    {
        Preconditions.RequireInteractiveDesktop();
        var (deployed, _, message) = Preconditions.CheckExtension();
        output.WriteLine(message);
        Assert.True(deployed, message);
        var script = Path.Combine(Path.GetTempPath(), $"akml-cycle-{Guid.NewGuid():N}.sql");
        const string originalSql = "select    42    as    AKML_CYCLE_RESULT_045;\n";
        File.WriteAllText(script, originalSql);
        try
        {
            using var app = SsmsApp.Launch(script, database: "tempdb");
            var window = app.MainWindow(240);
            window.WaitUntilReady(w => w.EditorText(5).Contains("AKML_CYCLE_RESULT_045"), 120,
                "the read-only acceptance script");
            window.WaitUntilReady(w => w.IsConnected(), 60, "the local SQL connection");
            new Tour040(window, app.ProcessId).Prepare();
            window.BringToFront();
            output.WriteLine("Initial UIA text: " + window.EditorText().Trim());
            Assert.Equal(originalSql.Trim(), SqlText(window.EditorText()).Trim());
            var menu = window.TopLevelMenu("AKML SQL");
            menu.Patterns.ExpandCollapse.Pattern.Expand();
            window.WaitUntilReady(_ => menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .Any(e => e.Name.StartsWith("Format Document", StringComparison.Ordinal)), 15, "the Format Document menu item");
            var format = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .Single(e => e.Name.StartsWith("Format Document", StringComparison.Ordinal));
            format.Patterns.Invoke.Pattern.Invoke();
            var formatted = window.WaitForEditorText(
                text => SqlText(text).Trim() != originalSql.Trim(), 30, "the engine's formatted SQL");
            formatted = SqlText(formatted);
            Assert.Matches(@"(?is)^\s*select\s+42\s+as\s+\[?AKML_CYCLE_RESULT_045\]?\s*;?\s*$", formatted);
            output.WriteLine("Format Document normalized the whitespace through the installed engine: " + formatted.Trim());

            window.Execute(timeoutSeconds: 2);
            window.WaitUntilReady(w => w.Raw.FindFirstDescendant(cf => cf.ByName("Query executed successfully.")) != null,
                60, "successful execution of SELECT 42");
            Assert.Contains(Directory.GetFiles(@"\\.\pipe\"), path =>
                Path.GetFileName(path).StartsWith("akmlsql-engine-", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("-" + app.ProcessId, StringComparison.Ordinal));
            var shot = Shot.Element(window.Raw, "ssms-installed-read-only-cycle");
            Assert.False(Shot.LooksBlank(shot));
            output.WriteLine($"SELECT 42 executed successfully; SSMS PID {app.ProcessId}; screenshot: {shot}");
        }
        finally { File.Delete(script); }
    }

    // SSMS's UIA TextPattern includes its rendered line numbers on this host.
    private static string SqlText(string rendered) => Regex.Replace(rendered, @"(?m)^\d+ ", "");
}

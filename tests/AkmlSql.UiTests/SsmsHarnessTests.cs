using AkmlSql.UiTests.Driver;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.UiTests;

/// <summary>
/// Proves the primitives this harness is built on, against a real SSMS 22.
///
/// <para>
/// These run in one collection so they never share the desktop with each other. There is exactly
/// one mouse pointer and one foreground window on a machine, which makes UI automation the one kind
/// of test that genuinely cannot be parallelised.
/// </para>
/// </summary>
[Collection("SSMS desktop")]
public sealed class SsmsHarnessTests(ITestOutputHelper output)
{
    private const string ViolatingSql = "DELETE FROM dbo.Orders;\n";

    [Fact]
    public void Environment_can_drive_and_photograph_the_desktop()
    {
        Preconditions.RequireInteractiveDesktop();

        var path = Shot.Screen("00-desktop-precheck");
        output.WriteLine($"Captured {path}");

        Assert.False(Shot.LooksBlank(path),
            "The desktop captured as a flat colour. That is what a disconnected RDP session looks " +
            "like — reconnect, or redirect it with: tscon.exe %SESSIONNAME% /dest:console");
    }

    [Fact]
    public void Extension_deployment_is_reported_so_a_stale_build_cannot_be_mistaken_for_a_bug()
    {
        var (deployed, builtUtc, message) = Preconditions.CheckExtension();
        output.WriteLine(message);

        Assert.True(deployed,
            $"{message} Deploy the extension before running UI tests, or they will exercise an IDE " +
            "that does not contain the code under test.");

        // Not an assertion failure — a loud note. A UI suite passing against a months-old build is
        // worse than one that fails, because it reports confidence it has not earned.
        if (builtUtc is { } built && DateTime.UtcNow - built > TimeSpan.FromDays(1))
        {
            output.WriteLine(
                $"WARNING: the deployed build is {(DateTime.UtcNow - built).TotalDays:F1} days old. " +
                "Anything newer in the working tree is NOT what these tests just exercised.");
        }
    }

    [Fact]
    public void Attaching_to_a_running_ssms_can_read_its_editor_and_menus()
    {
        if (System.Diagnostics.Process.GetProcessesByName("Ssms").Length == 0)
        {
            output.WriteLine("No SSMS running; nothing to attach to. Skipping.");
            return;
        }

        using var app = SsmsApp.AttachToRunning();
        var window = app.MainWindow(timeoutSeconds: 60);

        // Deliberately read-only: no BringToFront, no typing. Attaching to an instance somebody is
        // using should not move their windows around or touch their unsaved work.
        output.WriteLine($"Attached to PID {app.ProcessId}: {window.Title}");

        var menus = window.TopLevelMenuNames();
        output.WriteLine("Menus: " + string.Join(", ", menus));
        Assert.Contains("AKML SQL", menus);

        var text = window.EditorText(timeoutSeconds: 20);
        output.WriteLine($"Editor holds {text.Length} chars; first line: " +
                         text.Split('\n').FirstOrDefault()?.Trim());
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void A_dedicated_instance_opens_a_script_and_can_be_photographed()
    {
        Preconditions.RequireInteractiveDesktop();

        var sqlFile = Path.Combine(Path.GetTempPath(), $"akml-ui-{Guid.NewGuid():N}.sql");
        File.WriteAllText(sqlFile, ViolatingSql);
        output.WriteLine($"Scratch script: {sqlFile}");

        try
        {
            using var app = SsmsApp.Launch(sqlFile);
            output.WriteLine($"Launched SSMS PID {app.ProcessId}");

            var window = app.MainWindow(timeoutSeconds: 240);
            output.WriteLine($"Main window: {window.Title}");

            // Answer the "Connect to the following server?" prompt and wait for the document.
            window.WaitUntilReady(
                w => w.EditorText(5).Contains("DELETE FROM dbo.Orders", StringComparison.OrdinalIgnoreCase),
                timeoutSeconds: 120,
                description: "the scratch script to load into the editor");
            output.WriteLine($"Editor text round-tripped: {window.EditorText().Trim()}");

            window.WaitUntilReady(w => w.IsConnected(), timeoutSeconds: 60,
                description: "the query window to attach to the server");
            output.WriteLine($"Connected. Title now: {window.Title}");

            output.WriteLine("Menus: " + string.Join(", ", window.TopLevelMenuNames()));

            window.BringToFront();

            var windowShot = Shot.Element(window.Raw, "10-ssms-window");
            var editorShot = Shot.Element(window.Editor(), "11-ssms-editor");
            var marginShot = Shot.ElementWithContext(window.GlyphMargin(), "12-ssms-glyph-margin", pad: 8);
            output.WriteLine($"Captured:\n  {windowShot}\n  {editorShot}\n  {marginShot}");

            Assert.False(Shot.LooksBlank(windowShot), "The SSMS window captured blank.");
            Assert.False(Shot.LooksBlank(editorShot), "The editor captured blank.");
        }
        finally
        {
            try { File.Delete(sqlFile); } catch { /* scratch file */ }
        }
    }
}

/// <summary>Serialises every UI test: one desktop, one pointer, one foreground window.</summary>
[CollectionDefinition("SSMS desktop", DisableParallelization = true)]
public sealed class SsmsDesktopCollection;

using AkmlSql.UiTests.Driver;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.UiTests;

/// <summary>
/// Captures the SSMS 22 imagery used on the product site.
///
/// <para>
/// Everything is shot against <b>Northwind</b>, never a real database. The images this replaces
/// were taken against live working data and published publicly; sample data is the only safe thing
/// to put in a screenshot, and the assertions at the end of each capture enforce that.
/// </para>
///
/// <para>Run explicitly — it drives a real desktop:</para>
/// <code>
/// dotnet test tests/AkmlSql.UiTests --filter FullyQualifiedName~SsmsScreenshotTour
/// </code>
/// </summary>
[Collection("SSMS desktop")]
public sealed class SsmsScreenshotTour(ITestOutputHelper output)
{
    /// <summary>
    /// A realistic Northwind report, plus one deliberate <c>SELECT *</c> so the analyser has
    /// something to flag — the squiggle and margin glyph are the point of the picture.
    /// </summary>
    private const string TourSql =
        """
        -- Top customers by order value, 1997
        SELECT TOP (20)
               c.CompanyName,
               c.Country,
               COUNT(DISTINCT o.OrderID)              AS Orders,
               SUM(od.UnitPrice * od.Quantity)        AS Revenue
        FROM dbo.Orders AS o
             INNER JOIN dbo.Customers AS c ON c.CustomerID = o.CustomerID
             INNER JOIN dbo.[Order Details] AS od ON od.OrderID = o.OrderID
        WHERE o.OrderDate >= '1997-01-01'
          AND o.OrderDate <  '1998-01-01'
        GROUP BY c.CompanyName, c.Country
        ORDER BY Revenue DESC;

        GO

        CREATE OR ALTER VIEW dbo.ProductCatalogue AS
        SELECT * FROM dbo.Products;
        """;

    [Fact]
    public async Task Capture_ssms_with_northwind()
    {
        // Wait rather than fail: a remote session's desktop comes back when its owner reconnects,
        // and this run should finish itself when that happens.
        output.WriteLine("Waiting for an interactive desktop...");
        Preconditions.WaitForInteractiveDesktop(timeoutSeconds: 900);
        output.WriteLine("Desktop available.");

        var (deployed, builtUtc, message) = Preconditions.CheckExtension();
        output.WriteLine(message);
        Assert.True(deployed, message);

        var outDir = Path.Combine(Path.GetTempPath(), "akml-ssms-tour");
        Directory.CreateDirectory(outDir);
        Shot.ArtifactDirectory = outDir;

        var sqlFile = Path.Combine(Path.GetTempPath(), "Northwind sample.sql");
        await File.WriteAllTextAsync(sqlFile, TourSql);

        try
        {
            using var app = SsmsApp.Launch(sqlFile, server: "(local)", database: "Northwind");
            output.WriteLine($"SSMS PID {app.ProcessId}");

            var window = app.MainWindow(timeoutSeconds: 240);

            window.WaitUntilReady(
                w => w.EditorText(5).Contains("Northwind", StringComparison.OrdinalIgnoreCase)
                  || w.EditorText(5).Contains("CompanyName", StringComparison.OrdinalIgnoreCase),
                timeoutSeconds: 120,
                description: "the sample script to load");

            window.WaitUntilReady(w => w.IsConnected(), timeoutSeconds: 90,
                description: "the query window to attach to the server");
            output.WriteLine($"Connected: {window.Title}");

            window.BringToFront();

            // SSMS restores whatever panels were last open. Left alone, the shot ends up with a
            // third of the frame given over to somebody else's chat panel.
            foreach (var panel in new[] { "GitHub Copilot Chat", "Copilot" })
            {
                if (window.CloseToolWindow(panel))
                {
                    output.WriteLine($"Closed tool window: {panel}");
                    break;
                }
            }

            // Run it, so the picture shows a result grid rather than an empty pane.
            window.Execute(timeoutSeconds: 90);
            await Task.Delay(TimeSpan.FromSeconds(3));

            // Analysis is debounced and the engine warms its schema cache first, so the squiggles
            // and margin glyphs are not there the instant the document opens.
            await Task.Delay(TimeSpan.FromSeconds(10));

            var full = Shot.Element(window.Raw, "ssms-editor");
            var editor = Shot.Element(window.Editor(), "ssms-editor-closeup");
            output.WriteLine($"Captured:\n  {full}\n  {editor}");

            Assert.False(Shot.LooksBlank(full), "The SSMS window captured blank.");

            // The safety net: whatever is on screen must be sample data.
            var text = window.EditorText();
            foreach (var forbidden in new[] { "aqmar", "martyrs", "Toledo" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            try { File.Delete(sqlFile); } catch { /* scratch file */ }
        }
    }

    /// <summary>
    /// Spec 040 (T193): the windows spec 040 reworked — the AKML SQL menu and its Active Style
    /// submenu, Options in light and dark (Behavior, History, Color), SQL History with Advanced
    /// search open, and the Format Styles window at its default size on the Lists page. The theme
    /// the user had is put back at the end.
    /// </summary>
    [Fact]
    public async Task Capture_spec_040_windows()
    {
        output.WriteLine("Waiting for an interactive desktop...");
        Preconditions.WaitForInteractiveDesktop(timeoutSeconds: 900);
        var (deployed, _, message) = Preconditions.CheckExtension();
        output.WriteLine(message);
        Assert.True(deployed, message);

        Shot.ArtifactDirectory = Path.Combine(Path.GetTempPath(), "akml-ssms-tour");
        Directory.CreateDirectory(Shot.ArtifactDirectory);
        var sqlFile = Path.Combine(Path.GetTempPath(), "Northwind sample.sql");
        await File.WriteAllTextAsync(sqlFile, TourSql);

        var shots = new List<string>();
        string? themeBefore = null;
        try
        {
            using var app = SsmsApp.Launch(sqlFile, server: "(local)", database: "Northwind");
            var window = app.MainWindow(timeoutSeconds: 240);
            window.WaitUntilReady(w => w.IsConnected(), timeoutSeconds: 120, description: "the query window to attach to the server");
            window.BringToFront();
            foreach (var panel in new[] { "GitHub Copilot Chat", "Copilot" })
                if (window.CloseToolWindow(panel)) break;
            var tour = new Tour040(window, app.ProcessId);
            tour.Prepare();

            // The AKML SQL menu, then Active Style ▸.
            shots.Add(tour.MenuShot());

            // Options in light and dark.
            themeBefore = tour.Theme();
            foreach (var theme in new[] { "Light", "Dark" })
                shots.AddRange(tour.OptionsShots(theme));

            // SQL History with Advanced search open.
            shots.Add(tour.HistoryShot());

            // Format Styles at its default size, on Lists.
            shots.Add(tour.StylesShot());

            foreach (var shot in shots)
            {
                output.WriteLine($"Captured {shot}");
                Assert.False(Shot.LooksBlank(shot), $"{shot} captured blank.");
            }

            // Whatever is on screen must be sample data.
            var text = window.EditorText();
            foreach (var forbidden in new[] { "aqmar", "martyrs", "Toledo" })
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);

            if (themeBefore != null) tour.SetTheme(themeBefore);
            themeBefore = null;
        }
        finally
        {
            if (themeBefore != null) output.WriteLine($"Put the AKML SQL theme back to '{themeBefore}' by hand (Options › General).");
            try { File.Delete(sqlFile); } catch { /* scratch file */ }
        }
    }
}

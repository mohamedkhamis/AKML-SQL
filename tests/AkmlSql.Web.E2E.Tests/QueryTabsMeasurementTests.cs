using System.Diagnostics;
using System.Text;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 043 (T102) — the numbers baseline.md records: a tab switch until the status bar and database selector show the
/// tab's connection (SC-003, 20 switches); showing a tab that holds a 10 MB document; a reload with 20 restored tabs.
/// Opt-in (AKML_MEASURE=1): timings depend on the machine, so the bounds are loose and the numbers are written to the output.
/// </summary>
[Trait("Category", "Measurement")]
public sealed class QueryTabsMeasurementTests
{
    private readonly ITestOutputHelper _output;

    public QueryTabsMeasurementTests(ITestOutputHelper output) => _output = output;

    private static void SkipUnlessMeasuring() =>
        Skip.IfNot(Environment.GetEnvironmentVariable("AKML_MEASURE") == "1", "Opt-in measurement: AKML_MEASURE=1.");

    private static ILocator Tabs(IPage page) => page.GetByTestId("doc-tab");

    private void Report(string what, List<long> samples)
    {
        samples.Sort();
        _output.WriteLine($"{what}: n={samples.Count} median={samples[samples.Count / 2]} ms p90={samples[(int)(samples.Count * 0.9)]} ms max={samples[^1]} ms");
    }

    [SkippableFact]
    public async Task SC003_tab_switch_shows_the_tabs_connection_within_a_second()
    {
        SkipUnlessMeasuring();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await page.GetByTestId("doc-new").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);
        await WebAppFixture.ConnectAsync(page, "master");

        var samples = new List<long>();
        for (int i = 0; i < 20; i++)
        {
            int target = i % 2;   // tab 1 (tempdb), tab 2 (master), ...
            string database = target == 0 ? "tempdb" : "master";
            var watch = Stopwatch.StartNew();
            await Tabs(page).Nth(target).ClickAsync();
            await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync($"localhost/{database}", new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync(database, new() { Timeout = 10_000 });
            samples.Add(watch.ElapsedMilliseconds);
        }
        Report("SC-003 tab switch (click to status bar and database selector)", samples);
        Assert.True(samples.Max() < 2_000, "A switch took more than 2 s.");
    }

    [SkippableFact]
    public async Task Showing_a_tab_that_holds_a_10_MB_document()
    {
        SkipUnlessMeasuring();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        // Just under the 10 MB limit, in characters and in UTF-8 bytes.
        var line = "SELECT 'measure' AS c1, 12345 AS c2 FROM sys.objects WHERE object_id > 0; -- line\n";
        var text = new StringBuilder();
        while (text.Length + line.Length < 10 * 1024 * 1024 - 64) text.Append(line);
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        await page.GetByTestId("doc-open-input").SetInputFilesAsync(new FilePayload { Name = "big.sql", MimeType = "text/plain", Buffer = bytes });
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("big", new() { Timeout = 60_000 });
        await page.GetByTestId("doc-new").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);

        var samples = new List<long>();
        for (int i = 0; i < 6; i++)
        {
            int target = i % 2 == 0 ? 0 : 1;
            var watch = Stopwatch.StartNew();
            await Tabs(page).Nth(target).ClickAsync();
            await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync(target == 0 ? "big" : "SQLQuery2", new() { Timeout = 30_000 });
            if (target == 0) await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content")).ToContainTextAsync("measure", new() { Timeout = 30_000 });
            samples.Add(watch.ElapsedMilliseconds);
        }
        Report($"Switch between a {bytes.Length / 1024 / 1024} MB tab and an empty one", samples);
    }

    [SkippableFact]
    public async Task A_reload_with_twenty_restored_tabs()
    {
        SkipUnlessMeasuring();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        for (int i = 2; i <= 20; i++)
        {
            await page.GetByTestId("doc-new").ClickAsync();
            await Assertions.Expect(Tabs(page)).ToHaveCountAsync(i);
            await s.SetEditorTextAsync($"SELECT {i} AS tab;");
        }
        await Assertions.Expect(page.GetByTestId("doc-new")).ToBeDisabledAsync();   // FR-013: the limit
        await page.WaitForTimeoutAsync(1_200);   // the tab records' debounce

        var watch = Stopwatch.StartNew();
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(20, new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content")).ToContainTextAsync("SELECT 20 AS tab;", new() { Timeout = 60_000 });
        long shown = watch.ElapsedMilliseconds;
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/tempdb", new() { Timeout = 60_000 });
        long connected = watch.ElapsedMilliseconds;
        _output.WriteLine($"Reload with 20 tabs: tabs and the active editor shown in {shown} ms; the active tab connected again in {connected} ms.");
    }
}

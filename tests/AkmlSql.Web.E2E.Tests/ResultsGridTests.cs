using System.Globalization;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T054, research R70) — quickstart §1, "Result columns", in a real browser against the local SQL
/// Server: content-fitted widths, drag and double-click resizing, spreadsheet selection and copy, the menus,
/// sort, click-to-select editing with NULL and invalid cells, the pending-edits confirmation, and the
/// keyboard-only path. Pointer work (column drag, drag-select, the context-menu event) and clipboard text
/// are exactly what a component test cannot reach.
///
/// <para><c>BridgeE2E</c>, <see cref="SkippableFactAttribute"/>: skipped without a local SQL Server, a DEBUG
/// build of AkmlSql.Web and AkmlSql.Engine, or Playwright Chromium. Run the suite serially
/// (<c>-- xUnit.ParallelizeTestCollections=false</c>): every test starts the web app on port 5000.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class ResultsGridTests
{
    private readonly ITestOutputHelper _output;

    [SkippableFact]
    public async Task Large_results_fit_without_clipping_and_pointer_work_stays_responsive()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await page.EvaluateAsync("""
            () => {
                window.__gridTiming = { complete: null, fitted: null, tasks: [] };
                const timing = window.__gridTiming;
                const observer = new MutationObserver(() => {
                    const marker = document.querySelector('[data-testid="execute-complete"]');
                    if (marker && Number(marker.dataset.version) > 0 && timing.complete === null)
                        timing.complete = performance.now();
                    const grid = document.querySelector('[data-testid="results-grid"]');
                    if (timing.complete !== null && grid?.style.getPropertyValue('--akml-grid-cols').trim().split(/\s+/).length === 31) {
                        requestAnimationFrame(() => { timing.fitted = performance.now(); });
                        observer.disconnect();
                    }
                });
                observer.observe(document.body, { subtree: true, childList: true, attributes: true });
                window.__gridLongTasks = new PerformanceObserver(list => timing.tasks.push(...list.getEntries().map(e => e.duration)));
                window.__gridLongTasks.observe({ type: 'longtask' });
            }
            """);
        static string Query(int columns) => "SELECT TOP (1000) " + string.Join(", ", Enumerable.Range(0, columns)
            .Select(c => c == 5 ? "CAST('2025-10-01T23:57:23.1234567' AS datetime2(7)) AS precise_date"
                : $"CONCAT('column{c}-', ROW_NUMBER() OVER (ORDER BY a.object_id, b.object_id)) AS col{c}"))
            + " FROM sys.all_objects a CROSS JOIN sys.all_objects b;";
        await s.ExecuteAsync(Query(30));
        await page.WaitForFunctionAsync("() => window.__gridTiming.fitted !== null");
        var elapsed = await page.EvaluateAsync<double>("() => window.__gridTiming.fitted - window.__gridTiming.complete");
        _output.WriteLine($"SC-009 1000 x 30 tracks: {elapsed:F1} ms");
        Assert.InRange(elapsed, 0, 1000);
        foreach (var fraction in new[] { 0, 0.5, 1 })
        {
            await Grid(page).EvaluateAsync("(el, fraction) => { el.scrollTop = (el.scrollHeight - el.clientHeight) * fraction; }", fraction);
            await page.WaitForTimeoutAsync(250); // allow Virtualize's intersection observer to fill the viewport
            var clipped = await Grid(page).EvaluateAsync<int>("""
                el => [...el.querySelectorAll('[data-r][data-c]')].filter(c => c.dataset.c !== '-1'
                    && c.clientWidth < 479 && c.scrollWidth > c.clientWidth).length
                """);
            Assert.Equal(0, clipped);
        }
        await s.ExecuteAsync(Query(50));
        await Grid(page).EvaluateAsync("el => { el.scrollTop = 0; el.scrollLeft = 0; }");
        await Assertions.Expect(Cell(page, 0, 0)).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(250);
        _output.WriteLine($"Rendered rows before pointer work: {await page.Locator("[data-testid^='results-row-']").CountAsync()}");
        await page.EvaluateAsync("() => { window.__gridLongTasks.takeRecords(); window.__gridTiming.tasks = []; }");
        await DragAsync(page, page.GetByTestId("results-col-resizer-0"), 80, 0);
        await DragAsync(page, Cell(page, 0, 0), 180, 50);
        await page.WaitForTimeoutAsync(250);
        var tasks = await page.EvaluateAsync<double[]>("() => window.__gridTiming.tasks");
        _output.WriteLine($"SC-002/009 50-column drag maximum long task: {(tasks.Length == 0 ? 0 : tasks.Max()):F1} ms");
        await s.ExecuteAsync(Query(200));
        await EventuallyAsync(async () => (await TracksAsync(page)).Length == 201, "200 columns fitted");
        await page.GetByTestId("results-col-0").ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByTestId("results-menu-autofit-all").ClickAsync();
        await EventuallyAsync(async () => (await TracksAsync(page)).All(w => w > 0), "200 columns auto-fitted");
        _output.WriteLine("200-column auto-fit completed.");
        Assert.All(tasks, duration => Assert.True(duration <= 100, $"Pointer long task: {duration:F1} ms"));
    }

    public ResultsGridTests(ITestOutputHelper output) => _output = output;

    /// <summary>Ten rows: n, a label, and a column that is NULL on every third row.</summary>
    private const string Numbers =
        "SELECT v.n, CONCAT('row ', v.n) AS label, CASE WHEN v.n % 3 = 0 THEN NULL ELSE v.n * 10 END AS tens " +
        "FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS v(n) ORDER BY v.n;";

    private static readonly BrowserNewContextOptions ClipboardContext = new()
    {
        ViewportSize = new() { Width = 1440, Height = 900 },
        Permissions = new[] { "clipboard-read", "clipboard-write" },
    };

    private static ILocator Grid(IPage page) => page.Locator("[data-testid='results-grid']");

    private static ILocator Cell(IPage page, int r, int c) => page.Locator($"[data-testid='results-cell-{r}-{c}']");

    private static async Task<int[]> TracksAsync(IPage page)
    {
        var tracks = await Grid(page).EvaluateAsync<string>("el => el.style.getPropertyValue('--akml-grid-cols')");
        return tracks.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => (int)Math.Round(double.Parse(t.Replace("px", ""), CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static Task<string> ClipboardAsync(IPage page) => page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

    private static async Task<int[]> DisplayOrderAsync(IPage page)
    {
        var ids = await page.Locator("[data-testid^='results-row-']").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.testid)");
        return ids.Select(id => int.Parse(id.Substring("results-row-".Length), CultureInfo.InvariantCulture)).ToArray();
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail($"Timed out waiting for: {what}");
    }

    /// <summary>Clean-up: drop pending edits (a failed assertion can leave some) so the next run does not stop
    /// at the discard confirmation, and run <paramref name="sql"/>.</summary>
    private static async Task CleanUpAsync(SqlSession s, string sql)
    {
        try
        {
            if (await s.Page.Locator("[data-testid='dialog-cancel']").CountAsync() > 0) await s.Page.ClickAsync("[data-testid='dialog-cancel']");
            if (await s.Page.Locator("[data-testid='results-discard']").CountAsync() > 0)
            {
                await s.Page.ClickAsync("[data-testid='results-discard']");
                await s.Page.ClickAsync("[data-testid='dialog-confirm']");
            }
            await s.ExecuteAsync(sql, 20_000);
        }
        catch (Exception ex)
        {
            SqlSession.Log(null, ex.Message);
        }
    }

    private static async Task DragAsync(IPage page, ILocator from, float dx, float dy)
    {
        var box = (await from.BoundingBoxAsync())!;
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + dx, y + dy, new() { Steps = 12 });
        await page.Mouse.UpAsync();
    }

    [SkippableFact]
    public async Task Columns_fit_their_content_and_no_visible_value_is_cut()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT name, database_id, create_date FROM sys.databases;");

        var tracks = await TracksAsync(s.Page);
        Assert.Equal(4, tracks.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$", (await Cell(s.Page, 0, 2).InnerTextAsync()).Trim());

        var cut = await s.Page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('[data-testid=results-grid] [data-r][data-c], [data-testid=results-grid] .akml-col-name')]" +
            ".filter(c => c.dataset.c !== '-1' && c.scrollWidth > c.clientWidth).length");
        Assert.Equal(0, cut);
    }

    [SkippableFact]
    public async Task A_wide_value_stops_at_the_maximum_and_View_value_shows_it_all()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT REPLICATE('x', 2000) AS wide, 1 AS n;");

        Assert.Equal(480, (await TracksAsync(s.Page))[1]);
        var cell = Cell(s.Page, 0, 0);
        Assert.StartsWith("xxxxxxxx", await cell.GetAttributeAsync("title"));

        await cell.ClickAsync(new() { Button = MouseButton.Right });
        await s.Page.ClickAsync("[data-testid='results-menu-view-value']");
        var text = s.Page.Locator("[data-testid='results-view-value-text']");
        await Assertions.Expect(text).ToBeVisibleAsync();
        Assert.Equal(2000, (await text.InputValueAsync()).Length);
        await Assertions.Expect(s.Page.Locator("[data-testid='results-view-value-size']")).ToHaveTextAsync("2,000 characters");

        await s.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-view-value']")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Dragging_an_edge_follows_the_pointer_and_a_double_click_fits_it_again()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync(Numbers);
        var before = await TracksAsync(s.Page);

        var handle = s.Page.Locator("[data-testid='results-col-resizer-1']");
        await DragAsync(s.Page, handle, 120, 0);

        await EventuallyAsync(async () => (await TracksAsync(s.Page))[2] - before[2] is >= 118 and <= 122, "the label column 120 px wider");
        var after = await TracksAsync(s.Page);
        Assert.Equal(before[1], after[1]);
        Assert.Equal(before[3], after[3]);
        var header = (await s.Page.Locator("[data-testid='results-col-1']").BoundingBoxAsync())!;
        var body = (await Cell(s.Page, 0, 1).BoundingBoxAsync())!;
        Assert.InRange(Math.Abs(header.X - body.X), 0, 1);
        Assert.InRange(Math.Abs(header.Width - body.Width), 0, 1);

        await handle.DblClickAsync();
        await EventuallyAsync(async () => (await TracksAsync(s.Page))[2] == before[2], "the column back at its content width");
    }

    [SkippableFact]
    public async Task Rows_and_columns_copy_as_tab_separated_text()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output, contextOptions: ClipboardContext);
        await s.ExecuteAsync(Numbers);

        await s.Page.ClickAsync("[data-testid='results-rownum-2']");
        await s.Page.ClickAsync("[data-testid='results-rownum-5']", new() { Modifiers = new[] { KeyboardModifier.Shift } });
        await s.Page.Keyboard.PressAsync("Control+C");
        await EventuallyAsync(async () => (await ClipboardAsync(s.Page)).StartsWith("3\t", StringComparison.Ordinal), "rows 3–6 on the clipboard");
        var lines = (await ClipboardAsync(s.Page)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "3\trow 3\tNULL", "4\trow 4\t40", "5\trow 5\t50", "6\trow 6\tNULL" }, lines);

        await s.Page.ClickAsync("[data-testid='results-col-1'] .akml-col-name");
        await s.Page.Keyboard.PressAsync("Control+Shift+C");
        await EventuallyAsync(async () => (await ClipboardAsync(s.Page)).StartsWith("label\r\nrow 1\r\n", StringComparison.Ordinal), "the column with its header");

        var from = (await Cell(s.Page, 0, 0).BoundingBoxAsync())!;
        var to = (await Cell(s.Page, 2, 1).BoundingBoxAsync())!;
        await s.Page.Mouse.MoveAsync(from.X + 5, from.Y + 5);
        await s.Page.Mouse.DownAsync();
        await s.Page.Mouse.MoveAsync(to.X + 5, to.Y + 5, new() { Steps = 10 });
        await s.Page.Mouse.UpAsync();
        await Assertions.Expect(Cell(s.Page, 2, 1)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(Cell(s.Page, 1, 0)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(Cell(s.Page, 2, 2)).ToHaveAttributeAsync("aria-selected", "false");

        await s.Page.Keyboard.PressAsync("Shift+ArrowDown");
        await Assertions.Expect(Cell(s.Page, 3, 1)).ToHaveAttributeAsync("aria-selected", "true");
    }

    [SkippableFact]
    public async Task The_cell_menu_lists_its_commands_and_opens_from_the_keyboard()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output, contextOptions: ClipboardContext);
        await s.ExecuteAsync(Numbers);

        await Cell(s.Page, 1, 1).ClickAsync(new() { Button = MouseButton.Right });
        var menu = s.Page.Locator("[data-testid='results-context-menu']");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        Assert.Equal(new[] { "Copy", "Copy with headers", "Copy as", "Select all", "View value", "Save results as CSV…", "Save results as tab-delimited…" },
            (await menu.Locator(":scope > li > .akml-menu-item .akml-menu-label").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());

        await s.Page.ClickAsync("[data-testid='results-menu-copy-as']");
        await s.Page.ClickAsync("[data-testid='results-menu-copy-json']");
        await EventuallyAsync(async () => (await ClipboardAsync(s.Page)).TrimStart().StartsWith("[", StringComparison.Ordinal), "JSON on the clipboard");

        await s.Page.Keyboard.PressAsync("Shift+F10");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await s.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0);
        Assert.Equal("results-grid", await s.Page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.dataset.testid"));
    }

    [SkippableFact]
    public async Task The_sort_control_cycles_and_a_new_run_clears_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync(Numbers);
        var sort = s.Page.Locator("[data-testid='results-sort-2']");
        var header = s.Page.Locator("[data-testid='results-col-2']");

        await sort.ClickAsync();
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "ascending");
        Assert.Equal(new[] { 2, 5, 8, 0, 1 }, (await DisplayOrderAsync(s.Page)).Take(5).ToArray());   // NULL first, then 10, 20

        await sort.ClickAsync();
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "descending");
        Assert.Equal(9, (await DisplayOrderAsync(s.Page))[0]);   // 100

        await sort.ClickAsync();
        Assert.Null(await header.GetAttributeAsync("aria-sort"));
        Assert.Equal(Enumerable.Range(0, 10).ToArray(), await DisplayOrderAsync(s.Page));

        await sort.ClickAsync();
        await s.ExecuteAsync(Numbers);
        Assert.Null(await header.GetAttributeAsync("aria-sort"));
    }

    [SkippableFact]
    public async Task Editing_selects_first_marks_invalid_cells_and_Apply_persists_date_and_money()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_grid_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync(
            $"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NULL, qty int NOT NULL, d date NULL, m money NULL); " +
            $"INSERT dbo.{table} (name, qty, d, m) VALUES (N'alpha', 1, '2025-01-01', 1.5), (N'bravo', 2, NULL, NULL);");
        try
        {
            await s.ExecuteAsync($"SELECT id, name, qty, d, m FROM dbo.{table} ORDER BY id;");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-legend']")).ToBeVisibleAsync();
            var input = s.Page.Locator("[data-testid='results-cell-input']");

            // One click selects; Enter edits; Escape cancels.
            await Cell(s.Page, 0, 1).ClickAsync();
            await Assertions.Expect(input).ToHaveCountAsync(0);
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(input).ToHaveValueAsync("alpha");
            await s.Page.Keyboard.TypeAsync("zzz");
            await s.Page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(input).ToHaveCountAsync(0);
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("alpha");

            // Ctrl+0 → NULL on a nullable column.
            await s.Page.Keyboard.PressAsync("Control+0");
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("NULL");

            // Tab moves to qty; emptied, it is invalid and Apply is blocked; a value fixes it.
            await s.Page.Keyboard.PressAsync("F2");
            await s.Page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(input).ToHaveValueAsync("1");
            await s.Page.Keyboard.PressAsync("Control+A");
            await s.Page.Keyboard.PressAsync("Backspace");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-invalid-0-2']")).ToBeVisibleAsync();
            await Assertions.Expect(s.Page.Locator("[data-testid='results-apply']")).ToBeDisabledAsync();
            await s.Page.Keyboard.PressAsync("Control+0");   // NOT NULL: refused
            await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-invalid-0-2']")).ToBeVisibleAsync();
            await s.Page.Keyboard.TypeAsync("5");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-apply']")).ToBeEnabledAsync();

            // PR #255: dates accept the grid's displayed text and round-trip through Apply.
            await Cell(s.Page, 0, 3).ClickAsync();
            await s.Page.Keyboard.PressAsync("F2");
            await Assertions.Expect(input).ToHaveValueAsync("2025-01-01");
            await s.Page.Keyboard.PressAsync("Control+A");
            await s.Page.Keyboard.TypeAsync("2025-10-01");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(Cell(s.Page, 0, 3)).ToHaveTextAsync("2025-10-01");
            await Cell(s.Page, 0, 4).ClickAsync();
            await s.Page.Keyboard.TypeAsync("12.5");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(Cell(s.Page, 0, 4)).ToHaveTextAsync("12.5000");

            await s.Page.ClickAsync("[data-testid='results-apply']");
            var applyBar = s.Page.Locator("[data-testid='results-applybar']");
            var applyMessage = s.Page.Locator("[data-testid='results-apply-message']");
            await EventuallyAsync(async () => await applyBar.CountAsync() == 0 || await applyMessage.CountAsync() > 0, "the Apply to finish", 30_000);
            var message = await applyMessage.CountAsync() > 0 ? await applyMessage.InnerTextAsync() : "(no message)";
            Assert.True(await applyBar.CountAsync() == 0, "Apply failed: " + message);

            await s.ExecuteAsync($"SELECT name, qty, CONVERT(char(10), d, 120) AS d, CONVERT(varchar(20), m) AS m FROM dbo.{table} ORDER BY id;");
            await Assertions.Expect(Cell(s.Page, 0, 0)).ToHaveTextAsync("NULL");
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("5");
            await Assertions.Expect(Cell(s.Page, 0, 2)).ToHaveTextAsync("2025-10-01");
            await Assertions.Expect(Cell(s.Page, 0, 3)).ToHaveTextAsync("12.50");
        }
        finally
        {
            await CleanUpAsync(s, $"DROP TABLE IF EXISTS dbo.{table};");
        }
    }

    [SkippableFact]
    public async Task Running_a_query_over_pending_edits_asks_first()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_pending_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync($"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NULL); INSERT dbo.{table} (name) VALUES (N'alpha');");
        try
        {
            await s.ExecuteAsync($"SELECT id, name FROM dbo.{table};");
            await Cell(s.Page, 0, 1).ClickAsync();
            await s.Page.Keyboard.TypeAsync("changed");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-applybar']")).ToBeVisibleAsync();

            // F5 (editor:execute) asks first; so does the Execute button.
            await s.Page.Keyboard.PressAsync("F5");
            var dialog = s.Page.Locator("[data-testid='dialog']");
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(s.Page.Locator("[data-testid='dialog-title']")).ToContainTextAsync("Discard 1 pending edit");
            await s.Page.ClickAsync("[data-testid='dialog-cancel']");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-applybar']")).ToBeVisibleAsync();

            await s.Page.ClickAsync("[data-testid='execute-button']");
            await s.Page.ClickAsync("[data-testid='dialog-confirm']");
            await Assertions.Expect(s.Page.Locator("[data-testid='results-applybar']")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("alpha");
        }
        finally
        {
            await CleanUpAsync(s, $"DROP TABLE IF EXISTS dbo.{table};");
        }
    }

    [SkippableFact]
    public async Task The_grid_works_from_the_keyboard_alone()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT REPLICATE('x', 2000) AS wide, 1 AS n, 'short' AS s;");
        var grid = Grid(s.Page);

        // One tab stop: Tab leaves the grid, Shift+Tab comes back to it.
        await grid.FocusAsync();
        await s.Page.Keyboard.PressAsync("Tab");
        Assert.False(await grid.EvaluateAsync<bool>("el => el.contains(document.activeElement)"));
        await s.Page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await grid.EvaluateAsync<bool>("el => el === document.activeElement"));

        // Arrow to the cut cell: its tooltip shows; Escape dismisses it.
        await s.Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(grid).ToHaveAttributeAsync("aria-activedescendant", "akml-grid-0-0-0");
        var tooltip = s.Page.Locator("[data-testid='results-cell-tooltip']");
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await s.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tooltip).ToHaveCountAsync(0);

        // Shift+Alt+Right widens the focused column.
        await s.Page.Keyboard.PressAsync("ArrowRight");
        var before = (await TracksAsync(s.Page))[2];
        await s.Page.Keyboard.PressAsync("Shift+Alt+ArrowRight");
        await EventuallyAsync(async () => (await TracksAsync(s.Page))[2] == before + 16, "the n column 16 px wider");

        // Shift+F10 opens the menu, Escape closes it and the grid keeps the focus.
        await s.Page.Keyboard.PressAsync("Shift+F10");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-context-menu']")).ToBeVisibleAsync();
        await s.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-context-menu']")).ToHaveCountAsync(0);
        Assert.True(await grid.EvaluateAsync<bool>("el => el === document.activeElement"));
    }
}

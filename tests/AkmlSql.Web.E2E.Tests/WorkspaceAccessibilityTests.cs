using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

[Trait("Category", "BridgeE2E")]
public sealed class WorkspaceAccessibilityTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Themes_have_readable_token_pairs_focus_help_and_reduced_motion()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        var page = s.Page;
        foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
        {
            await ShellPolishTests.NavigateAsync(page, "settings/general");
            await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
            await ShellPolishTests.NavigateAsync(page, "editor");
            await s.ExecuteAsync("SELECT 1 AS n, REPLICATE('x', 2000) AS wide;");
            await page.Keyboard.PressAsync("Tab");
            await page.GetByTestId("format-button").FocusAsync();
            await Assertions.Expect(page.Locator("#akml-keyboard-help")).ToContainTextAsync("Format");
            Assert.Contains("akml-keyboard-help", await page.GetByTestId("format-button").GetAttributeAsync("aria-describedby"));
            Assert.NotEqual("none", await page.GetByTestId("format-button").EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle"));
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(page.Locator("#akml-keyboard-help")).ToHaveCountAsync(0);

            var ratiosJson = await page.EvaluateAsync<string>("""
                () => {
                    const probe = document.createElement('span'); document.body.appendChild(probe);
                    const rgb = token => { probe.style.color = `var(--akml-${token})`; return getComputedStyle(probe).color.match(/[\d.]+/g).slice(0,3).map(Number); };
                    const luminance = values => values.map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; })
                        .reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
                    const pairs = [['editor', 'text-primary', 'surface-editor'], ['badge', 'badge-neutraltext', 'badge-neutral'],
                        ['dialog', 'text-primary', 'surface-panel'], ['menu-selection', 'text-onaccent', 'surface-selectionstrong'],
                        ['secondary', 'text-secondary', 'surface-panel'], ['tooltip', 'text-primary', 'surface-panel'],
                        ['error-badge-and-confirm', 'text-ondanger', 'badge-error'], ['warning-badge', 'text-onaccent', 'badge-warning'],
                        ['success-badge', 'text-onaccent', 'badge-success'],
                        ['primary-button', 'text-onaccent', 'accent-primary'], ['primary-button-hover', 'text-onaccent', 'accent-primarypressed'],
                        ...['success', 'warning', 'danger'].flatMap(kind => ['panel', 'canvas'].map(surface =>
                            [`${kind}-on-${surface}`, `statustext-${kind}`, `surface-${surface}`]))];
                    const ratios = Object.fromEntries(pairs.map(([name, fg, bg]) => {
                        const a = luminance(rgb(fg)), b = luminance(rgb(bg)); return [name, (Math.max(a,b)+.05)/(Math.min(a,b)+.05)];
                    }));
                    const pill = getComputedStyle(document.querySelector('[data-testid="status-pill"]'));
                    const fg = luminance(pill.color.match(/[\d.]+/g).slice(0,3).map(Number));
                    const bg = luminance(pill.backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number));
                    ratios['live-status-pill'] = (Math.max(fg,bg)+.05)/(Math.min(fg,bg)+.05);
                    probe.remove(); return JSON.stringify(ratios);
                }
                """);
            var ratios = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(ratiosJson)!;
            Assert.Equal(18, ratios.Count);
            foreach (var pair in ratios)
            {
                output.WriteLine($"{theme} {pair.Key}: {pair.Value:F2}:1");
                Assert.True(pair.Value >= 4.5, $"{theme} {pair.Key}: {pair.Value:F2}:1");
            }
            var animation = await page.EvaluateAsync<string>("""
                () => { const el = document.createElement('span'); el.className = 'akml-spin'; document.body.appendChild(el);
                    const name = getComputedStyle(el).animationName; el.remove(); return name; }
                """);
            Assert.Equal("none", animation);
            if (theme == "HighContrast")
            {
                await page.GetByTestId("format-button").HoverAsync();
                Assert.True(await page.GetByTestId("format-button").EvaluateAsync<bool>(
                    "el => { const s = getComputedStyle(el); return s.color !== s.backgroundColor; }"));
                await page.GetByTestId("results-cell-0-0").HoverAsync();
                output.WriteLine(await page.GetByTestId("results-cell-0-0").EvaluateAsync<string>("el => JSON.stringify({hover:el.matches(':hover'), rowHover:el.parentElement.matches(':hover'), theme:document.documentElement.dataset.akmlTheme, color:getComputedStyle(el).color, rowBackground:getComputedStyle(el.parentElement).backgroundColor, parent:el.parentElement.className, forced:matchMedia('(forced-colors: active)').matches})"));
                var text = await page.GetByTestId("results-cell-0-0").EvaluateAsync<string>("el => getComputedStyle(el).color");
                Assert.Equal(await page.EvaluateAsync<string>("""
                    () => { const el = document.createElement('span'); el.style.color = 'var(--akml-text-onaccent)'; document.body.appendChild(el);
                        const color = getComputedStyle(el).color; el.remove(); return color; }
                    """), text);
            }
        }
    }

    /// <summary>
    /// Spec 043 (T064, FR-018, FR-019, contracts/theme-and-settings.md §5): every syntax colour reads at 4.5:1 on the
    /// editor and on the active line, body text reads at 4.5:1 on the selection and on a find match, and the matching
    /// bracket's outline stands out from the editor at 3:1. Light and dark only: high contrast maps these tokens to
    /// system colours, checked for visibility by the screenshot walk rather than by ratio.
    /// </summary>
    [SkippableFact]
    public async Task Editor_colours_meet_their_contrast_targets_in_light_and_dark()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        var page = s.Page;
        foreach (var theme in new[] { "Light", "Dark" })
        {
            await ShellPolishTests.NavigateAsync(page, "settings/general");
            await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
            await ShellPolishTests.NavigateAsync(page, "editor");

            var json = await page.EvaluateAsync<string>("""
                () => {
                    const probe = document.createElement('span'); document.body.appendChild(probe);
                    const rgb = token => { probe.style.color = `var(--akml-${token})`; return getComputedStyle(probe).color.match(/[\d.]+/g).slice(0,3).map(Number); };
                    const luminance = values => values.map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; })
                        .reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
                    const ratio = (fg, bg) => { const a = luminance(rgb(fg)), b = luminance(rgb(bg)); return (Math.max(a,b)+.05)/(Math.min(a,b)+.05); };
                    const syntax = ['keyword', 'string', 'number', 'comment', 'function', 'tablecolumn'];
                    const out = {};
                    for (const t of syntax) {
                        out[`${t}-on-editor`] = [ratio(`syntax-${t}`, 'surface-editor'), 4.5];
                        out[`${t}-on-active-line`] = [ratio(`syntax-${t}`, 'editor-activeline'), 4.5];
                    }
                    out['text-on-active-line'] = [ratio('text-primary', 'editor-activeline'), 4.5];
                    out['text-on-selection'] = [ratio('text-primary', 'editor-selection'), 4.5];
                    out['text-on-find-match'] = [ratio('text-primary', 'editor-searchmatch'), 4.5];
                    out['bracket-outline-on-editor'] = [ratio('editor-matchingbracket', 'surface-editor'), 3.0];
                    probe.remove(); return JSON.stringify(out);
                }
                """);
            var pairs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(json)!;
            Assert.Equal(16, pairs.Count);
            foreach (var (name, values) in pairs)
            {
                output.WriteLine($"{theme} {name}: {values[0]:F2}:1 (target {values[1]})");
                Assert.True(values[0] >= values[1], $"{theme} {name}: {values[0]:F2}:1, target {values[1]}:1");
            }
        }
    }

    /// <summary>
    /// Spec 043 (T086, R17, FR-031): the page declares its colour scheme, so the browser draws its own controls
    /// (checkboxes, a select's list, scrollbars) dark in the dark theme. Before, the connection dialog's checkbox was
    /// white and its scrollbar light.
    /// </summary>
    [SkippableFact]
    public async Task Native_controls_follow_the_theme()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        foreach (var (theme, scheme) in new[] { ("Dark", "dark"), ("Light", "light") })
        {
            await ShellPolishTests.NavigateAsync(page, "settings/general");
            await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
            await ShellPolishTests.NavigateAsync(page, "editor");
            Assert.Equal(scheme, await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).colorScheme"));

            await page.ClickAsync("[data-testid='status-connection']");
            var box = page.GetByTestId("conn-trust-cert");
            await Assertions.Expect(box).ToBeVisibleAsync();
            Assert.Equal(scheme, await box.EvaluateAsync<string>("el => getComputedStyle(el).colorScheme"));
            await page.Keyboard.PressAsync("Escape");
        }
    }

    [SkippableFact]
    public async Task Touch_resizes_splitters_and_columns_at_both_scales()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ViewportSize = new() { Width = 1800, Height = 1200 }, HasTouch = true });
        var page = s.Page;
        var cdp = await s.Context.NewCDPSessionAsync(page);
        foreach (var zoom in new[] { 1.0, 1.5 })
        {
            await page.EvaluateAsync("zoom => { document.body.style.zoom = zoom; }", zoom);
            await s.ExecuteAsync("SELECT 1 AS n, 'touch resize' AS label;");
            foreach (var (id, minimum, vertical) in new[] { ("workspace-splitter-bottom", 12, true), ("results-col-resizer-0", 8, false) })
            {
                var target = page.GetByTestId(id);
                await target.ScrollIntoViewIfNeededAsync();
                var box = (await target.BoundingBoxAsync())!;
                Assert.True((vertical ? box.Height : box.Width) >= minimum * zoom - 1);
                float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
                var measured = vertical ? page.GetByTestId("workspace-results") : page.GetByTestId("results-col-0");
                var before = (await measured.BoundingBoxAsync())!;
                await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y } } });
                for (var step = 1; step <= 8; step++)
                {
                    // Pace the gesture as a deliberate resize. A burst of CDP moves can leave
                    // Chromium suppressing the next tap after a fast drag, even on a plain page.
                    await page.WaitForTimeoutAsync(100);
                    await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = x + (vertical ? 0 : step * 8), y = y - (vertical ? step * 8 : 0) } } });
                }
                await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
                await page.WaitForTimeoutAsync(250);
                var after = (await measured.BoundingBoxAsync())!;
                var delta = vertical ? after.Height - before.Height : after.Width - before.Width;
                output.WriteLine($"{zoom * 100}% {id}: hit area {(vertical ? box.Height : box.Width):F1}px; drag delta {delta:F1}px");
                Assert.True(delta > 10);
            }
            // Zoom can already have folded Schema; blindly toggling would open it instead.
            var schema = page.GetByTestId("workspace-schema");
            if (await schema.IsVisibleAsync()) await page.Keyboard.PressAsync("F8");
            var strip = page.GetByTestId("workspace-edge-left");
            await Assertions.Expect(strip).ToBeVisibleAsync();
            Assert.True((await strip.BoundingBoxAsync())!.Width >= 24 * zoom - 1);
            await strip.TapAsync();
            await Assertions.Expect(schema).ToBeVisibleAsync();
        }
    }
}

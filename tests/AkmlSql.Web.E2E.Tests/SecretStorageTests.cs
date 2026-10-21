using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Review findings WEB-04 and WEB-15 with the browser's real Web Crypto and IndexedDB (a mock cannot show either), in a fresh
/// Chromium context against the Debug web app. Synthetic values only; no engine, SQL Server or provider is involved.
/// <list type="bullet">
///   <item>WEB-04 — concurrent first wraps, in one tab and in two, all decrypt after a reload: one wrapping key, used by all.</item>
///   <item>WEB-15 — after secrets are stored, another tab's version upgrade is not blocked by connections the crypto module
///   left open, and the secrets still decrypt afterwards.</item>
/// </list>
/// </summary>
public sealed class SecretStorageTests(ITestOutputHelper output) : IAsyncLifetime
{
    private Harness.WebAppFixture? _app;

    public async Task InitializeAsync() => _app = await Harness.WebAppFixture.StartAsync();

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    private const string WrapMany = """
        async (prefix) => {
            const crypto = await import('./js/akml-crypto.js');
            const values = Array.from({ length: 12 }, (_, i) => `${prefix}-synthetic-${i}`);
            const wrapped = await Promise.all(values.map(v => crypto.wrap(v, 'aad-e2e')));
            return JSON.stringify(wrapped.map((w, i) => ({ value: values[i], ct: Array.from(w.ciphertext), iv: Array.from(w.iv), aad: Array.from(w.aad) })));
        }
        """;

    private const string UnwrapAll = """
        async (json) => {
            const records = JSON.parse(json);
            const crypto = await import('./js/akml-crypto.js');
            const out = [];
            for (const r of records) {
                try { out.push(await crypto.unwrap(new Uint8Array(r.ct), new Uint8Array(r.iv), new Uint8Array(r.aad)) === r.value); }
                catch (e) { out.push(false); }
            }
            return out;
        }
        """;

    private async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        page.Console += (_, m) => { if (m.Type == "error") output.WriteLine("console: " + m.Text.Split('\n')[0]); };
        await page.GotoAsync(_app!.Url);
        // The app opens (and upgrades) its database at start-up; the crypto module only uses it.
        await page.WaitForSelectorAsync("nav.akml-nav", new() { Timeout = 60_000 });
        return page;
    }

    [SkippableFact]
    public async Task Concurrent_first_wraps_in_one_tab_and_in_two_all_decrypt_after_a_reload()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync();
        var a = await OpenAsync(context);
        var b = await OpenAsync(context);

        // Two tabs, each with twelve wraps in flight, against a store that has no key yet.
        var both = await Task.WhenAll(
            a.EvaluateAsync<string>(WrapMany, "tab-a"),
            b.EvaluateAsync<string>(WrapMany, "tab-b"));

        await a.ReloadAsync();
        await a.WaitForSelectorAsync("nav.akml-nav", new() { Timeout = 60_000 });
        foreach (var records in both)
        {
            var ok = await a.EvaluateAsync<bool[]>(UnwrapAll, records);
            Assert.Equal(12, ok.Length);
            Assert.All(ok, Assert.True);
        }
    }

    [SkippableFact]
    public async Task Stored_secrets_do_not_block_another_tabs_upgrade_and_still_decrypt_after_it()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync();
        var a = await OpenAsync(context);
        var records = await a.EvaluateAsync<string>(WrapMany, "upgrade");

        var b = await OpenAsync(context);
        var outcome = await b.EvaluateAsync<string>("""
            () => new Promise(resolve => {
                const probe = indexedDB.open('AkmlSqlWeb');
                probe.onsuccess = () => {
                    const next = probe.result.version + 1;
                    probe.result.close();
                    const req = indexedDB.open('AkmlSqlWeb', next);
                    let blocked = false;
                    req.onblocked = () => { blocked = true; };
                    req.onsuccess = () => { req.result.close(); resolve(blocked ? 'blocked-then-upgraded' : 'upgraded'); };
                    req.onerror = () => resolve('error: ' + req.error);
                    setTimeout(() => resolve(blocked ? 'blocked' : 'timeout'), 10000);
                };
            })
            """);
        output.WriteLine("upgrade: " + outcome);

        // The main adapter closes on versionchange as it always did; nothing the crypto module opened may hold the upgrade.
        // (After it, the app's own start-up asks for its version, 4, below the probe's 5 and logs a VersionError: an
        // artefact of upgrading past what this app knows. The crypto module opens without a version, so it is unaffected.)
        Assert.Equal("upgraded", outcome);

        await a.ReloadAsync();
        await a.WaitForSelectorAsync("nav.akml-nav", new() { Timeout = 60_000 });
        var ok = await a.EvaluateAsync<bool[]>(UnwrapAll, records);
        Assert.All(ok, Assert.True);
    }
}

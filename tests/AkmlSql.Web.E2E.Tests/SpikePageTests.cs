using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 023 (M1 ScriptDom-in-WASM spike) -- T022. The repeatable, automated form of
/// the in-browser corpus run (quickstart.md §8). Drives /spike in a real Chromium
/// browser, clicks "Run all corpus", and asserts the corpus completes and no uncaught
/// runtime exception reaches the page.
///
/// This test runs against an ALREADY-SERVED instance. Set AKML_SPIKE_BASE_URL to the
/// root of a served Release publish, then:
///   dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "SpikePageTests"
///
/// When AKML_SPIKE_BASE_URL is unset the test is a documented no-op: the existing E2E
/// project ships no self-hosting harness (see PostKeywordTriggerTests), and the spike
/// deliberately does not add one. Requires the Playwright browser binaries -- install
/// once with the generated `playwright.ps1 install chromium` script in the build output.
/// </summary>
public sealed class SpikePageTests
{
    private readonly ITestOutputHelper _output;

    public SpikePageTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Spike_page_runs_the_corpus_with_no_uncaught_exception()
    {
        var baseUrl = Environment.GetEnvironmentVariable("AKML_SPIKE_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _output.WriteLine(
                "SKIPPED: set AKML_SPIKE_BASE_URL (e.g. http://localhost:5000) to a served "
                + "AkmlSql.Web instance to run this test. See quickstart.md §8.");
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();

        // Capture any uncaught JS/WASM error. The spike catches and renders handled
        // exceptions itself, so a non-empty list here means a hard page crash.
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);

        await page.GotoAsync(
            $"{baseUrl.TrimEnd('/')}/spike",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The "Run all corpus" button renders once the WASM app is interactive.
        await page.Locator("#spike-run-corpus").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 60_000 });

        // ClickAsync auto-waits for the button to become enabled (it is disabled
        // until corpus.json has loaded).
        await page.Locator("#spike-run-corpus").ClickAsync();

        // Every corpus item must resolve into a row -- seven items in the corpus.
        await Assertions.Expect(page.Locator("#spike-corpus-table tbody tr"))
            .ToHaveCountAsync(7, new() { Timeout = 120_000 });

        Assert.Empty(pageErrors);
    }
}

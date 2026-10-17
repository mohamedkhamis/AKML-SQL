using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// The installed web edition as a person meets it: the browser checks the engine's certificate.
///
/// <para>
/// Every other test that talks to the installed engine passes <c>--ignore-certificate-errors</c>, so
/// they all passed while a real browser could not connect at all. The report that prompted this: a
/// re-install replaced the bridge certificate; the new one was not trusted and did not name
/// <c>localhost</c>, and on the engine's own computer the page said "Nothing answered" and offered no
/// PIN field, because a network-mode engine wants a PIN even from loopback.
/// </para>
///
/// <para>
/// Machine set-up, not product state, decides whether these can run: they skip until the installed
/// <c>bridge.cer</c> is trusted on this computer (Local Machine › Trusted Root). From then on a
/// certificate that does not name an address this computer uses is a failure, not a skip.
/// </para>
/// </summary>
public sealed class InstalledEngineCertificateTests(ITestOutputHelper output)
{
    private static int BridgePort => InstalledEngine.BridgePort;

    /// <summary>The names a browser on the engine's computer dials, and the one INSTALL-SUMMARY.txt links to.</summary>
    public static TheoryData<string> LocalNames => new()
    {
        "localhost",
        "127.0.0.1",
        Environment.MachineName, // the NetBIOS name, which the summary's URL uses
        Dns.GetHostName(),
    };

    [SkippableTheory]
    [MemberData(nameof(LocalNames))]
    public async Task The_engine_certificate_is_valid_for_every_name_this_computer_uses(string host)
    {
        var errors = await TlsErrorsAsync(host, BridgePort);
        output.WriteLine($"{host}:{BridgePort} -> {errors}");

        // Only the chain is the machine's business; the names are the installer's (web-tls-setup.ps1).
        Assert.False(errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch),
            $"The engine's certificate does not name {host}. Re-run web-tls-setup.ps1 so it includes every local name.");
        Assert.Equal(SslPolicyErrors.None, errors);
    }

    [SkippableFact]
    public async Task A_browser_on_the_engine_computer_pairs_at_localhost_with_the_certificate_checked()
    {
        // Skips when the engine is absent or its certificate untrusted; a name mismatch fails here, plainly.
        Assert.Equal(SslPolicyErrors.None, await TlsErrorsAsync("localhost", BridgePort));

        await using var app = await WebAppFixture.StartAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchForTestsAsync(new() { Headless = true });
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } }); // no IgnoreHTTPSErrors
        var page = await context.NewPageAsync();
        page.WebSocket += (_, ws) => ws.SocketError += (_, e) => output.WriteLine($"[ws error] {ws.Url}: {e}");

        await page.GotoAsync(app.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });
        await page.ClickAsync("nav >> text=Settings");
        await page.ClickAsync("[data-testid='settings-nav-connections']");
        await page.Locator("button", new() { HasTextString = "Add" }).First.ClickAsync();
        await page.FillAsync("[data-testid='engine-add-host']", "localhost");
        await page.FillAsync("[data-testid='engine-add-port']", BridgePort.ToString());
        await Assertions.Expect(page.Locator("[data-testid='engine-add-pin']")).ToHaveCountAsync(0);
        await page.ClickAsync("[data-testid='engine-add-pair']");

        if (InstalledEngine.IsNetworkMode)
        {
            // A network-mode engine asks for its PIN even from this computer; the form must offer the field.
            await Assertions.Expect(page.Locator("[data-testid='engine-add-pin']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator("[data-testid='engine-connect-diagnosis']")).ToHaveCountAsync(0);
            var pin = InstalledEngine.PairingPin;
            Skip.If(pin is null, "No pairing PIN published. Restart AkmlSqlWebEngine to mint one.");
            await page.FillAsync("[data-testid='engine-add-pin']", pin!);
            await page.ClickAsync("[data-testid='engine-add-pair']");
        }

        await Assertions.Expect(page.Locator("[data-testid='status-pill']")).ToContainTextAsync("Live", new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator(".akml-connections-badge").First).ToHaveTextAsync("Local");
    }

    [SkippableFact]
    public async Task A_deep_link_on_the_installed_site_loads_the_app_instead_of_IIS_404()
    {
        var site = InstalledEngine.SiteUrl;
        Skip.If(site is null, "AKML SQL Web is not installed with IIS hosting on this computer (no INSTALL-SUMMARY.txt URL).");

        using (var http = new HttpClient())
        {
            var root = await http.GetStringAsync(site);
            Assert.Contains("_framework/blazor.webassembly.js", root, StringComparison.Ordinal);
            var body = await (await http.GetAsync(site + "settings/connections")).Content.ReadAsStringAsync();
            Assert.Contains("_framework/blazor.webassembly.js", body, StringComparison.Ordinal);
        }

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchForTestsAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(site + "settings/connections", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90_000 });
        await Assertions.Expect(page.Locator("[data-testid='settings-nav-connections']")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>
    /// Opens TLS to the bridge and returns what default validation found for <paramref name="host"/>. Skips the
    /// test when nothing listens there, the bridge is not TLS (a localhost-mode install), or this computer does
    /// not trust the certificate yet.
    /// </summary>
    private static async Task<SslPolicyErrors> TlsErrorsAsync(string host, int port)
    {
        using var tcp = new TcpClient();
        try { await tcp.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            throw new SkipException($"No engine bridge listening at {host}:{port}: {ex.Message}");
        }

        var errors = SslPolicyErrors.None;
        string? thumbprint = null;
        using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, e) =>
        {
            errors = e;
            thumbprint = cert?.GetCertHashString();
            return true; // decide below, with the reason in hand
        });
        try { await ssl.AuthenticateAsClientAsync(host).WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or TimeoutException)
        {
            throw new SkipException($"The bridge at {host}:{port} does not speak TLS (a localhost-mode install): {ex.Message}");
        }

        Skip.If(errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors),
            $"This computer does not trust the engine's certificate {thumbprint}. " +
            @"Install C:\ProgramData\AKML SQL Web\certs\bridge.cer under Local Machine › Trusted Root Certification Authorities.");
        return errors;
    }
}

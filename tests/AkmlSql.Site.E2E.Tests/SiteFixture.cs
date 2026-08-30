using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Shared Playwright browser against the LOCALLY DEPLOYED site (scripts/deploy-site-iis.ps1 →
/// IIS, host header akml.khamis.work).
/// <para>
/// The public DNS name resolves to a different address, so the browser is started with
/// <c>--host-resolver-rules</c> mapping the host to 127.0.0.1. That keeps the real host header,
/// cookie domain and TLS SNI — which matters, because the admin cookie is <c>Secure</c> and would
/// not be sent over a plain-http localhost URL.
/// </para>
/// <para>
/// The certificate is self-signed / locally issued, so certificate errors are ignored: this
/// fixture exercises the deployed application, not the CA chain.
/// </para>
/// </summary>
public sealed class SiteFixture : IAsyncLifetime
{
    /// <summary>Host the site is bound to in IIS.</summary>
    public const string Host = "akml.khamis.work";

    /// <summary>Base URL every test navigates from.</summary>
    public const string BaseUrl = "https://" + Host;

    /// <summary>Set when the deployed site could not be reached; every test skips.</summary>
    public string? SkipReason { get; private set; }

    public IBrowser Browser { get; private set; } = null!;

    private IPlaywright? _playwright;

    /// <summary>Admin password, supplied by the test runner via AKML_SITE_ADMIN_PASSWORD.</summary>
    public static string? AdminPassword => Environment.GetEnvironmentVariable("AKML_SITE_ADMIN_PASSWORD");

    public async Task InitializeAsync()
    {
        try
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // Point the public host name at the local IIS binding.
                Args = [$"--host-resolver-rules=MAP {Host} 127.0.0.1"],
            });

            await using var probe = await NewContextAsync();
            var page = await probe.NewPageAsync();
            var response = await page.GotoAsync(BaseUrl + "/health", new PageGotoOptions { Timeout = 15_000 });
            if (response is null || !response.Ok)
            {
                SkipReason = $"Deployed site not reachable at {BaseUrl} (status {response?.Status.ToString() ?? "none"}). " +
                             "Run scripts/deploy-site-iis.ps1 first.";
            }
        }
        catch (Exception ex)
        {
            SkipReason = $"Playwright/browser unavailable or site unreachable: {ex.Message}";
        }
    }

    /// <summary>A fresh browser context (own cookies/storage) at the default desktop viewport.</summary>
    public Task<IBrowserContext> NewContextAsync(int width = 1440, int height = 900) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}

/// <summary>Collection so every test class shares one browser process.</summary>
[CollectionDefinition(Name)]
public sealed class SiteCollection : ICollectionFixture<SiteFixture>
{
    public const string Name = "deployed-site";
}

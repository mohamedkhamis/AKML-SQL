global using AkmlSql.Web.E2E.Tests.Harness;

using Microsoft.Playwright;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>Shared visible-browser option for the local Playwright runner.</summary>
internal static class BrowserLaunch
{
    private static bool Headed => Environment.GetEnvironmentVariable("AKML_PLAYWRIGHT_HEADED") == "1";

    public static Task<IBrowser> LaunchForTestsAsync(this IBrowserType browser, BrowserTypeLaunchOptions? options = null)
    {
        options ??= new();
        if (Headed) options.Headless = false;
        return browser.LaunchAsync(options);
    }

    public static Task<IBrowserContext> LaunchPersistentForTestsAsync(this IBrowserType browser,
        string profile, BrowserTypeLaunchPersistentContextOptions? options = null)
    {
        options ??= new();
        if (Headed) options.Headless = false;
        return browser.LaunchPersistentContextAsync(profile, options);
    }
}

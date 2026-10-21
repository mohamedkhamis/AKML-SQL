using System.Net.Sockets;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 042 (task T051) - a browser on the Debug web app, paired with a <see cref="SandboxEngine"/> and connected to the
/// throwaway PostgreSQL server named by <c>AKML_PG_TEST_CONNECTION</c> (see <c>scripts/pg-test-server.ps1</c>): the starting
/// point of the PostgreSQL workspace tests. It skips the test when the variable is unset, the server does not answer, the
/// Debug engine or web app is missing, or Chromium cannot launch. The engine started is this working tree's, so it speaks
/// PostgreSQL; nothing here touches the installed engine service.
/// </summary>
public static class PgWebSession
{
    public const string EnvVar = "AKML_PG_TEST_CONNECTION";

    /// <summary>Host, port, user and password from the environment variable, or null when it is not set.</summary>
    public static (string Host, int Port, string User, string Password)? Configured(string? envVar = null)
    {
        var text = Environment.GetEnvironmentVariable(envVar ?? EnvVar);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) values[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
        }
        var host = values.GetValueOrDefault("Host") ?? "localhost";
        var port = int.TryParse(values.GetValueOrDefault("Port"), out var p) ? p : 5432;
        var user = values.GetValueOrDefault("Username") ?? values.GetValueOrDefault("User ID") ?? "postgres";
        return (host, port, user, values.GetValueOrDefault("Password") ?? string.Empty);
    }

    public static async Task<bool> ServerAnswersAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(2_000));
            return done == connect && client.Connected;
        }
        catch (SocketException) { return false; }
    }

    /// <summary>
    /// Starts everything and, when <paramref name="connect"/> is true, connects the page to the PostgreSQL server through
    /// the connection manager; the editor is open when this returns.
    /// </summary>
    /// <param name="postgreSqlEnabled">False starts an engine with PostgreSQL switched off in its config (spec 042, FR-093).</param>
    public static async Task<SqlSession> StartOrSkipAsync(
        ITestOutputHelper? output = null, bool connect = true, string? envVar = null, bool postgreSqlEnabled = true)
    {
        var configured = Configured(envVar);
        Skip.If(configured is null, $"No PostgreSQL test server: set {envVar ?? EnvVar} (see scripts/pg-test-server.ps1).");
        var (host, port, user, password) = configured!.Value;
        Skip.IfNot(await ServerAnswersAsync(host, port), $"No PostgreSQL server answering on {host}:{port}.");

        var engine = await SandboxEngine.StartAsync(postgreSqlEnabled);
        WebAppFixture? web = null;
        IPlaywright? pw = null;
        IBrowser? browser = null;
        try
        {
            try { web = await WebAppFixture.StartAsync(); }
            catch (Exception ex) { throw new SkipException($"Web app could not start (build it Debug first): {ex.Message}"); }

            pw = await Playwright.CreateAsync();
            try { browser = await pw.Chromium.LaunchForTestsAsync(new() { Headless = true }); }
            catch (PlaywrightException ex) { throw new SkipException($"Could not launch chromium: {ex.Message}"); }

            var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new() { Width = 1440, Height = 900 } });
            await Keys.InstallAsync(context);
            var page = await context.NewPageAsync();
            page.Console += (_, m) => { if (m.Type is "error") SqlSession.Log(output, $"[browser {m.Type}] {m.Text}"); };

            await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });

            await WebAppFixture.PairAsync(page, engine.Port);
            if (connect) await ConnectAsync(page, host, port, "postgres", user, password);

            await page.ClickAsync("nav >> text=Editor");
            await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
            return new SqlSession(engine, web, pw, browser, context, page);
        }
        catch
        {
            if (browser != null) await browser.DisposeAsync();
            pw?.Dispose();
            if (web != null) await web.DisposeAsync();
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>Opens the connection manager on the PostgreSQL kind and fills the form.</summary>
    public static async Task FillFormAsync(IPage page, string host, int port, string database, string user, string password)
    {
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']");
        await page.ClickAsync("[data-testid='conn-kind-postgresql']");
        await page.FillAsync("[data-testid='conn-pg-host']", host);
        await page.FillAsync("[data-testid='conn-pg-port']", port.ToString());
        await page.FillAsync("[data-testid='conn-pg-database']", database);
        await page.FillAsync("[data-testid='conn-pg-user']", user);
        await page.FillAsync("[data-testid='conn-pg-password']", password);
    }

    /// <summary>Connects the page to a PostgreSQL server through the connection manager.</summary>
    public static async Task ConnectAsync(IPage page, string host, int port, string database, string user, string password)
    {
        await FillFormAsync(page, host, port, database, user, password);
        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await Assertions.Expect(page.Locator("[data-testid='status-kind']"))
            .ToContainTextAsync("PostgreSQL", new() { Timeout = 30_000 });
    }
}

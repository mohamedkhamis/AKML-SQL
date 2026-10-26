using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 043 (T078, US3, FR-023 to FR-030, SC-007, SC-008) — one "Connect to database" command and dialog in a real
/// browser: the single palette entry, one title for both kinds, the four groups, a SQL Server form that fits a
/// 1280x720 window without scrolling, the trust choice saved and restored, and no "SQL Server" where the product means
/// "the server". Connecting through a self-signed remote server needs one and stays a manual check (quickstart M8).
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class ConnectDialogTests(ITestOutputHelper output)
{
    private static readonly BrowserNewContextOptions Small = new() { ViewportSize = new() { Width = 1280, Height = 720 } };

    private static async Task OpenDialogAsync(IPage page)
    {
        await page.ClickAsync("[data-testid='status-connection']");
        await Assertions.Expect(page.GetByTestId("connection-manager")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task The_palette_has_one_connect_command_and_the_dialog_one_title()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: Small);
        var page = s.Page;

        await page.Keyboard.PressAsync("Control+Shift+P");
        await page.GetByTestId("command-palette-input").FillAsync("connect");
        var rows = page.GetByTestId("command-palette-row").Filter(new() { HasTextString = "Connect to" });
        await Assertions.Expect(rows).ToHaveCountAsync(1);
        await Assertions.Expect(rows.First).ToContainTextAsync("Connect to database…");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByTestId("connection-manager")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("conn-title")).ToHaveTextAsync("Connect to database");
        if (await page.GetByTestId("conn-kind-postgresql").IsEnabledAsync())
        {
            await page.GetByTestId("conn-kind-postgresql").ClickAsync();
            await Assertions.Expect(page.GetByTestId("conn-title")).ToHaveTextAsync("Connect to database");
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_SQL_Server_form_fits_a_1280_by_720_window_without_scrolling(bool windowsAuth)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: Small);
        var page = s.Page;
        await OpenDialogAsync(page);
        await page.GetByTestId("conn-kind-sqlserver").ClickAsync();
        await page.CheckAsync(windowsAuth ? "[data-testid='conn-auth-windows']" : "[data-testid='conn-auth-sql']");

        var sizes = await page.Locator(".akml-connmgr-form-pane").EvaluateAsync<int[]>("el => [el.scrollHeight, el.clientHeight]");
        output.WriteLine($"windowsAuth={windowsAuth}: form {sizes[0]} in {sizes[1]}");
        Assert.True(sizes[0] <= sizes[1] + 1, $"the form scrolls: {sizes[0]} > {sizes[1]}");
        foreach (var group in new[] { "conn-group-server", "conn-group-signin", "conn-group-database", "conn-group-security" })
        {
            await Assertions.Expect(page.GetByTestId(group)).ToBeInViewportAsync(new() { Ratio = 1 });
        }
    }

    [SkippableFact]
    public async Task The_trust_choice_is_saved_and_restored_after_a_reload_and_a_local_server_cannot_take_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: Small);
        var page = s.Page;
        await OpenDialogAsync(page);
        await page.GetByTestId("conn-new-btn").ClickAsync();
        await page.GetByTestId("conn-kind-sqlserver").ClickAsync();

        // The engine's own computer: never validated, so the box is off-limits and says why.
        await page.FillAsync("[data-testid='conn-server-input']", "localhost");
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await Assertions.Expect(page.GetByTestId("conn-trust-cert")).ToBeDisabledAsync();
        await Assertions.Expect(page.GetByTestId("conn-trust-cert-help")).ToContainTextAsync("engine's own computer");

        // A remote server with a self-signed certificate: ticked, saved (no connect needed to save).
        await page.FillAsync("[data-testid='conn-name-input']", "Remote self-signed (043)");
        await page.FillAsync("[data-testid='conn-server-input']", "db043.example.com");
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await page.CheckAsync("[data-testid='conn-auth-sql']");
        await page.FillAsync("[data-testid='conn-login-input']", "reporting");
        await Assertions.Expect(page.GetByTestId("conn-trust-cert")).ToBeEnabledAsync();
        await page.CheckAsync("[data-testid='conn-trust-cert']");
        await page.GetByTestId("conn-save-btn").ClickAsync();
        await Assertions.Expect(page.GetByTestId("conn-message")).ToContainTextAsync("Saved");   // the write is done before the reload
        await Assertions.Expect(page.GetByTestId("conn-row").Filter(new() { HasTextString = "Remote self-signed (043)" })).ToHaveCountAsync(1);

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 60_000 });
        await OpenDialogAsync(page);
        var row = page.GetByTestId("conn-row").Filter(new() { HasTextString = "Remote self-signed (043)" });
        await Assertions.Expect(row.GetByTestId("conn-row-kind")).ToHaveTextAsync("SQL");
        await row.ClickAsync();

        await Assertions.Expect(page.GetByTestId("conn-server-input")).ToHaveValueAsync("db043.example.com");
        await Assertions.Expect(page.GetByTestId("conn-trust-cert")).ToBeCheckedAsync();

        await page.GetByTestId("conn-delete-btn").ClickAsync();   // leave the browser profile as it was
        var confirm = page.Locator(".akml-dialog [data-testid='dialog-confirm']");
        if (await confirm.CountAsync() > 0) await confirm.ClickAsync();
    }

    [SkippableFact]
    public async Task No_visible_text_names_SQL_Server_where_it_means_the_server()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: Small);
        var page = s.Page;

        // The disconnected states are where the old wording lived: the status bar, the schema panel, Settings.
        await page.ClickAsync("[data-testid='status-connection']");
        await page.GetByTestId("conn-close-btn").ClickAsync();
        var texts = new List<string> { await page.Locator("body").InnerTextAsync() };
        foreach (var title in await page.Locator("[title]").EvaluateAllAsync<string[]>("els => els.map(e => e.title)")) texts.Add(title);
        await ShellPolishTests.NavigateAsync(page, "settings/connections");
        texts.Add(await page.Locator("body").InnerTextAsync());

        foreach (var phrase in new[] { "Connect to SQL Server", "connect to SQL Server", "Not connected to SQL Server", "needs a SQL Server connection", "Connect to a SQL Server" })
        {
            Assert.DoesNotContain(texts, t => t.Contains(phrase, StringComparison.Ordinal));
        }
    }

    [SkippableFact]
    public async Task A_PostgreSQL_connection_keeps_its_TLS_mode_and_CA_path()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: Small);
        var page = s.Page;
        await OpenDialogAsync(page);
        Skip.IfNot(await page.GetByTestId("conn-kind-postgresql").IsEnabledAsync(), "The sandbox engine does not advertise PostgreSQL support.");

        await page.GetByTestId("conn-new-btn").ClickAsync();
        await page.GetByTestId("conn-kind-postgresql").ClickAsync();
        await page.FillAsync("[data-testid='conn-name-input']", "PG verify-ca (043)");
        await page.FillAsync("[data-testid='conn-pg-host']", "pg043.example.com");
        await page.FillAsync("[data-testid='conn-pg-user']", "reporting");
        await page.SelectOptionAsync("[data-testid='conn-pg-sslmode']", "verify-ca");
        await page.FillAsync("[data-testid='conn-pg-rootcert']", @"C:\certs\root043.crt");
        await page.GetByTestId("conn-save-btn").ClickAsync();

        await Assertions.Expect(page.GetByTestId("conn-message")).ToContainTextAsync("Saved");
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 60_000 });
        await OpenDialogAsync(page);
        await page.GetByTestId("conn-row").Filter(new() { HasTextString = "PG verify-ca (043)" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("conn-pg-sslmode")).ToHaveValueAsync("verify-ca");
        await Assertions.Expect(page.GetByTestId("conn-pg-rootcert")).ToHaveValueAsync(@"C:\certs\root043.crt");

        await page.GetByTestId("conn-delete-btn").ClickAsync();
        var confirm = page.Locator(".akml-dialog [data-testid='dialog-confirm']");
        if (await confirm.CountAsync() > 0) await confirm.ClickAsync();
    }
}

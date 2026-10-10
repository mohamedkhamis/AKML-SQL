using Microsoft.Playwright;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 041 (R59, R69, SC-012) — the web edition asks through its own dialog (<c>Shared/DialogHost.razor</c>), never
/// the browser's confirm/prompt. Suites that used to accept every browser dialog register this instead: whenever the
/// in-app dialog appears before a Playwright action or auto-waiting assertion, a prompt is answered with
/// <paramref name="promptAnswer"/> and the dialog is confirmed.
/// </summary>
internal static class InAppDialogs
{
    public static Task AcceptAllAsync(IPage page, string promptAnswer) =>
        page.AddLocatorHandlerAsync(page.Locator("[data-testid=dialog]"), async dialog =>
        {
            var input = dialog.Locator("[data-testid=dialog-input]");
            if (await input.CountAsync() > 0) await input.FillAsync(promptAnswer);
            await dialog.Locator("[data-testid=dialog-confirm]").ClickAsync();
        });

    /// <summary>Fails the test if the browser's own confirm, prompt or alert ever opens.</summary>
    public static void FailOnBrowserDialogs(IPage page, List<string> seen) =>
        page.Dialog += async (_, dialog) =>
        {
            lock (seen) seen.Add($"{dialog.Type}: {dialog.Message}");
            await dialog.DismissAsync();
        };
}

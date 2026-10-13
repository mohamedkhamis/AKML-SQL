using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 041 (T005, research R70, N16) — proves that the page claims a key. Playwright's synthetic key
/// presses never trigger browser accelerators (F5 does not reload even when nobody cancels it), so
/// "the page did not reload" proves nothing. Instead an init script records every keydown whose
/// <c>preventDefault</c> was called, and the assertions check that record. (A bubble-phase listener would
/// never see the event: the editor page's capture listener stops its propagation.)
/// </summary>
public static class Keys
{
    private const string RecorderScript = """
        (() => {
            if (window.__akmlKeyLog) return;
            window.__akmlKeyLog = [];
            const original = Event.prototype.preventDefault;
            Event.prototype.preventDefault = function () {
                if (this.type === 'keydown') {
                    window.__akmlKeyLog.push({ code: this.code, ctrl: !!(this.ctrlKey || this.metaKey), shift: !!this.shiftKey, alt: !!this.altKey });
                }
                return original.call(this);
            };
        })();
        """;

    /// <summary>Install the recorder on every page the context opens. Call before the first navigation.</summary>
    public static Task InstallAsync(IBrowserContext context) => context.AddInitScriptAsync(RecorderScript);

    /// <summary>Press <paramref name="key"/> (Playwright syntax, e.g. <c>Control+F5</c>) and report whether
    /// anything on the page called preventDefault on that keydown.</summary>
    public static async Task<bool> PressAndCheckClaimedAsync(IPage page, string key)
    {
        await page.EvaluateAsync("() => { if (window.__akmlKeyLog) window.__akmlKeyLog.length = 0; }");
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(50);
        var (code, ctrl, shift, alt) = Parse(key);
        return await page.EvaluateAsync<bool>(
            "([code, ctrl, shift, alt]) => (window.__akmlKeyLog || []).some(k => k.code === code && k.ctrl === ctrl && k.shift === shift && k.alt === alt)",
            new object[] { code, ctrl, shift, alt });
    }

    public static async Task AssertClaimedAsync(IPage page, string key) =>
        Assert.True(await PressAndCheckClaimedAsync(page, key), $"{key} was not claimed: the browser would act on it.");

    public static async Task AssertNotClaimedAsync(IPage page, string key) =>
        Assert.False(await PressAndCheckClaimedAsync(page, key), $"{key} was claimed where the browser should keep it.");

    /// <summary>Playwright key syntax → KeyboardEvent.code and modifiers.</summary>
    internal static (string Code, bool Ctrl, bool Shift, bool Alt) Parse(string key)
    {
        var parts = key.Split('+');
        var name = parts[^1];
        bool ctrl = parts.Contains("Control") || parts.Contains("Meta") || parts.Contains("ControlOrMeta");
        bool shift = parts.Contains("Shift");
        bool alt = parts.Contains("Alt");
        string code = name.Length == 1 && char.IsLetter(name[0]) ? "Key" + char.ToUpperInvariant(name[0])
            : name.Length == 1 && char.IsDigit(name[0]) ? "Digit" + name
            : name;
        return (code, ctrl, shift, alt);
    }
}

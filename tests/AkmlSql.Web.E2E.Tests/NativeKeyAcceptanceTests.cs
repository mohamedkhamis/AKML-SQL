using System.Runtime.InteropServices;
using System.Text;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

// Explicit opt-in: sends real Windows input to a headed browser on the interactive desktop.
// Run serially with AKML_NATIVE_KEY_ACCEPTANCE=1, never in an unattended routine suite.
[Trait("Category", "NativeAcceptance")]
public sealed class NativeKeyAcceptanceTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData("chromium")]
    [InlineData("edge")]
    [InlineData("firefox")]
    public async Task Windows_input_reaches_workspace_without_browser_accelerators(string browser)
    {
        Skip.IfNot(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("AKML_NATIVE_KEY_ACCEPTANCE") == "1",
            "Opt-in interactive Windows acceptance: AKML_NATIVE_KEY_ACCEPTANCE=1.");
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, browserName: browser, headless: false);
        var page = s.Page;
        page.FileChooser += async (_, chooser) => await chooser.SetFilesAsync(Array.Empty<string>());
        await s.SetEditorTextAsync("SELECT 1 AS n;");
        var marker = "AKML native acceptance " + Guid.NewGuid().ToString("N");
        await page.EvaluateAsync("title => { document.title = title; window.__nativeTitleTimer = setInterval(() => document.title = title, 50); }", marker);
        await page.WaitForTimeoutAsync(500);
        await page.BringToFrontAsync();
        using var input = new WindowsInput(marker);
        await page.EvaluateAsync("clearInterval(window.__nativeTitleTimer)");
        input.SetLayout("00000409");
        var failures = new List<string>();
        foreach (var key in new[] { "F5", "Control+F5", "Control+r", "F8", "F6", "Shift+F6", "Control+s", "Control+Shift+s", "Control+Alt+n", "Control+Alt+o" })
        {
            await page.Locator(".cm-content").First.ClickAsync();
            var before = await s.ExecuteVersionAsync();
            if (!await CheckAsync(page, input, key))
            {
                if (browser == "edge" && key == "Control+Shift+s") output.WriteLine("Edge keeps Ctrl+Shift+S for Screenshot; verify the Save as chord below.");
                else failures.Add(key);
            }
            if (key is "F5" or "Control+F5" && await page.GetByTestId("execute-complete").CountAsync() > 0)
                await s.WaitForExecuteAfterAsync(before);
            if (await page.GetByTestId("dialog-cancel").CountAsync() > 0) await page.GetByTestId("dialog-cancel").ClickAsync();
            input.Press("Escape");
        }
        foreach (var key in new[] { "Control+f", "Control+l", "Control+s", "Control+a" })
        {
            await page.Locator(".cm-content").First.ClickAsync();
            if (!await CheckAsync(page, input, "Control+k")) failures.Add("Control+k prefix");
            if (!await CheckAsync(page, input, key)) failures.Add("Control+k," + key);
            if (key == "Control+a")
            {
                await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
                await page.GetByTestId("dialog-cancel").ClickAsync();
                await Assertions.Expect(page.GetByTestId("doc-saveas")).ToHaveAttributeAsync("title", "Save as (Ctrl+Shift+S or Ctrl+K, Ctrl+A)");
                await page.GetByTestId("view-menu").ClickAsync();
                await Assertions.Expect(page.GetByTestId("view-saveas")).ToContainTextAsync("Ctrl+K, Ctrl+A");
                await page.GetByTestId("view-saveas").ClickAsync();
                await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
                await page.GetByTestId("dialog-cancel").ClickAsync();
                await page.Keyboard.PressAsync("Control+p");
                await page.Locator(".akml-cmd-input").FillAsync("Save as");
                var saveAs = page.GetByTestId("command-palette-row").Filter(new() { HasTextString = "Save as" });
                await Assertions.Expect(saveAs.Locator(".akml-cmd-shortcut")).ToContainTextAsync("Ctrl+K, Ctrl+A");
                await saveAs.ClickAsync();
                await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
                await page.GetByTestId("dialog-cancel").ClickAsync();
            }
            if (await page.GetByTestId("surround-picker").CountAsync() > 0)
                await page.GetByTestId("surround-picker").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            input.Press("Escape");
        }
        await s.ExecuteAsync("SELECT 1 AS n;");
        await page.GetByTestId("results-cell-0-0").ClickAsync();
        foreach (var key in new[] { "Control+Shift+c", "Control+0", "Shift+Alt+ArrowLeft", "Shift+Alt+ArrowRight" })
        {
            await page.GetByTestId("results-cell-0-0").ClickAsync();
            var layout = input.Layout;
            if (!await CheckAsync(page, input, key)) failures.Add(key);
            output.WriteLine($"{browser} {key}: layout {layout:X} -> {input.Layout:X}");
            if (layout != input.Layout) failures.Add(key + " switched input language");
        }
        await page.Locator(".cm-content").First.ClickAsync();
        input.Press("Control+a");
        input.Press("Delete");
        input.SetLayout("00000415"); // Polish (Programmers); only this test window receives the request.
        input.Press("AltGraph+o");
        input.Press("AltGraph+n");
        await Assertions.Expect(page.Locator(".cm-content").First).ToHaveTextAsync("óń");
        output.WriteLine($"{browser} native Polish AltGr: óń inserted");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private async Task<bool> CheckAsync(IPage page, WindowsInput input, string key)
    {
        var nonce = Guid.NewGuid().ToString("N");
        await page.EvaluateAsync("nonce => { window.__nativeNonce = nonce; window.__akmlKeyLog.length = 0; window.__nativeEvents = []; if (!window.__nativeEventProbe) { window.__nativeEventProbe = true; document.addEventListener('keydown', e => { window.__nativeEvents.push({ code:e.code,key:e.key,ctrl:e.ctrlKey,alt:e.altKey,shift:e.shiftKey,trusted:e.isTrusted }); },true); } }", nonce);
        input.Press(key);
        await page.WaitForTimeoutAsync(200);
        var (code, ctrl, shift, alt) = Keys.Parse(key);
        var claimed = await page.EvaluateAsync<bool>("([code,ctrl,shift,alt,nonce]) => window.__nativeNonce === nonce && (window.__akmlKeyLog || []).some(k => k.code === code && k.ctrl === ctrl && k.shift === shift && k.alt === alt)", new object[] { code, ctrl, shift, alt, nonce });
        output.WriteLine($"{key}: native input {(claimed ? "claimed, document retained" : "NOT claimed or document reloaded")}");
        if (!claimed) output.WriteLine(await page.EvaluateAsync<string>("JSON.stringify({focus:document.hasFocus(),element:document.activeElement?.tagName,events:window.__nativeEvents})"));
        return claimed;
    }

    private sealed class WindowsInput : IDisposable
    {
        private readonly IntPtr window;
        private readonly IntPtr previous = GetForegroundWindow();
        private readonly IntPtr originalLayout;
        public IntPtr Layout => GetKeyboardLayout(GetWindowThreadProcessId(window, out _));
        public WindowsInput(string marker)
        {
            var found = IntPtr.Zero;
            EnumWindows((handle, _) => { var title = new StringBuilder(1024); GetWindowText(handle, title, title.Capacity); if (title.ToString().Contains(marker)) found = handle; return true; }, IntPtr.Zero);
            window = found;
            if (window == IntPtr.Zero) throw new SkipException("Native-input prerequisite unavailable: no headed browser window found.");
            originalLayout = Layout;
            var foregroundThread = GetWindowThreadProcessId(previous, out _);
            var currentThread = GetCurrentThreadId();
            bool attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try
            {
                ShowWindow(window, 9);
                SetForegroundWindow(window);
                Thread.Sleep(150);
            }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            if (GetForegroundWindow() != window) throw new SkipException("Native-input prerequisite unavailable: Windows refused browser foreground focus; no native input sent.");
        }
        public void SetLayout(string id)
        {
            var layout = LoadKeyboardLayout(id, 0);
            if (layout == IntPtr.Zero) throw new InvalidOperationException("Keyboard layout unavailable: " + id);
            SendMessage(window, 0x50, IntPtr.Zero, layout);
            if ((Layout.ToInt64() & 0xffff) != (layout.ToInt64() & 0xffff)) throw new InvalidOperationException("Browser did not activate " + id);
        }
        public void Press(string key)
        {
            if (GetForegroundWindow() != window) throw new InvalidOperationException("Browser lost foreground; refusing to send keys to another application.");
            var keys = key.Split('+').Select(part => part switch
            {
                "Control" => (ushort)0x11, "Shift" => (ushort)0x10, "Alt" => (ushort)0x12,
                "AltGraph" => (ushort)0xA5, "F5" => (ushort)0x74, "F6" => (ushort)0x75, "F8" => (ushort)0x77,
                "Escape" => (ushort)0x1B, "Delete" => (ushort)0x2E, "ArrowLeft" => (ushort)0x25, "ArrowRight" => (ushort)0x27,
                _ when part.Length == 1 => (ushort)char.ToUpperInvariant(part[0]),
                _ => throw new ArgumentException("Unknown key: " + part)
            }).ToArray();
            var events = keys.Select(k => Event(k, false)).Concat(keys.Reverse().Select(k => Event(k, true))).ToArray();
            if (SendInput((uint)events.Length, events, Marshal.SizeOf<Input>()) != events.Length)
                throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        }
        private static Input Event(ushort key, bool up) => new() { Type = 1, Keyboard = new() { Scan = (ushort)MapVirtualKey(key, 0), Flags = 8u | (up ? 2u : 0u) | (key is 0xA5 or 0x25 or 0x27 or 0x2E ? 1u : 0u) } };
        public void Dispose()
        {
            SendMessage(window, 0x50, IntPtr.Zero, originalLayout);
            if (GetForegroundWindow() == window) SetForegroundWindow(previous);
        }
        [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
        private delegate bool EnumCallback(IntPtr handle, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint source, uint target, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string id, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    }
}

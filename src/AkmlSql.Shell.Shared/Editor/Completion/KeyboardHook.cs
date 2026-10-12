using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Thread-local keyboard hook using WH_KEYBOARD (not WH_KEYBOARD_LL) to intercept
    /// Ctrl+Space in the SSMS/VS editor. SSMS 22's SQL editor is a Win32 control where
    /// WPF PreviewKeyDown, ComponentDispatcher.ThreadPreprocessMessage, and IOleCommandTarget
    /// all fail to intercept Ctrl+Space before SSMS consumes it. A SetWindowsHookEx thread hook
    /// sits earlier in the message chain and can swallow the keystroke.
    ///
    /// For chord shortcuts like Ctrl+K,Y (Format Document) and Ctrl+D, a WH_KEYBOARD_LL
    /// (low-level) hook is used because SSMS's accelerator table captures Ctrl+K before
    /// WH_KEYBOARD can see it. The LL hook runs before TranslateAccelerator.
    ///
    /// A low-level hook sees the keyboard of the WHOLE desktop. It is therefore installed only while
    /// SSMS's window is in front (<see cref="ForegroundWatcher"/>: taken off when another program comes
    /// to the front, put back when SSMS does), and every key it sees is handed on untouched unless SSMS's own window is in front and an
    /// AKML SQL editor has the focus (<see cref="ShortcutRouter"/>). Before this, Visual Studio's
    /// Ctrl+K chords and Ctrl+D stopped working for as long as SSMS had a query open. In the SQL editor
    /// a kept Ctrl+K that is not followed by Y is typed again with its second key (SendInput, marked with
    /// <see cref="Win32Hooks.ReplayMarker"/>), so SSMS's own Ctrl+K chords still reach it.
    ///
    /// Known .NET issue: SetWindowsHookEx(WH_KEYBOARD_LL, ...) requires a valid native
    /// module handle. Using Process.MainModule.BaseAddress fails in managed processes.
    /// The workaround is LoadLibrary("user32.dll") — see:
    /// https://learn.microsoft.com/en-us/archive/blogs/toub/low-level-keyboard-hook-in-c
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WM_KEYDOWN = 0x0100;
        private const int LLKHF_EXTENDED = 0x01;

        private static readonly int[] NoModifiers = new int[0];

        private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

        private readonly IWindowsHooks _hooks;
        private readonly Func<bool> _thisProcessInFront;
        private readonly IAppActivation _activation;
        private readonly ShortcutRouter _router = new ShortcutRouter();

        private IntPtr _hookHandle = IntPtr.Zero;
        private HookProc _hookProc;  // prevent GC collection of the delegate
        private Action _onCtrlSpace;
        private Action _onFormat;
        private Action _onRefresh;
        private Func<bool> _sqlEditorFocused;
        private int _disposed;
        private bool _installed;
        private bool _followsActivation;
        private IntPtr _llHookHandle = IntPtr.Zero;
        private HookProc _llHookProc;

        public KeyboardHook() : this(Win32Hooks.Instance, ForegroundIsThisProcess, new ForegroundWatcher()) { }

        /// <summary>Test seam: the native hook calls, the foreground test and the activation source.</summary>
        internal KeyboardHook(IWindowsHooks hooks, Func<bool> thisProcessInFront, IAppActivation activation)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _thisProcessInFront = thisProcessInFront ?? throw new ArgumentNullException(nameof(thisProcessInFront));
            _activation = activation;
        }

        /// <summary>True while the desktop-wide hook is in place (only while SSMS is the active app).</summary>
        internal bool IsLowLevelHookInstalled => _llHookHandle != IntPtr.Zero;

        /// <summary>True while the UI-thread hook is in place.</summary>
        internal bool IsThreadHookInstalled => _hookHandle != IntPtr.Zero;

        internal ShortcutRouter Router => _router;

        /// <summary>
        /// Installs a WH_KEYBOARD hook on the current (UI) thread, and the low-level hook while SSMS is
        /// the active application. Must be called from the UI thread.
        /// </summary>
        /// <param name="onCtrlSpace">Callback invoked when Ctrl+Space is pressed. Called on the UI thread.</param>
        /// <param name="onFormat">Callback invoked when Ctrl+K,Y or Ctrl+D is pressed. Called on the UI thread.</param>
        /// <param name="onRefresh">Callback invoked when Ctrl+Shift+D is pressed. Called on the UI thread.</param>
        /// <param name="sqlEditorFocused">True while an AKML SQL editor has the focus; the shortcuts act only then.</param>
        public void Install(Action onCtrlSpace, Action onFormat = null, Action onRefresh = null, Func<bool> sqlEditorFocused = null)
        {
            if (onCtrlSpace == null)
                throw new ArgumentNullException(nameof(onCtrlSpace));

            if (_installed)
                return; // Already installed
            _installed = true;

            _onCtrlSpace = onCtrlSpace;
            _onFormat = onFormat;
            _onRefresh = onRefresh;
            _sqlEditorFocused = sqlEditorFocused ?? (() => true);

            // Must hold a reference to prevent the delegate from being garbage-collected
            // while user32.dll still holds a native pointer to it.
            _hookProc = HookCallback;
            _llHookProc = LowLevelHookCallback;

            uint threadId = GetCurrentThreadId();
            _hookHandle = _hooks.SetThreadHook(_hookProc, threadId);
            if (_hookHandle == IntPtr.Zero)
                Log.Warning("KeyboardHook: WH_KEYBOARD failed with error {Error}", Marshal.GetLastWin32Error());

            // The low-level hook follows the foreground: watched first, then checked, so a change in
            // between is not missed. Without the notification it stays in place for the session; the
            // foreground test in the callback still keeps other programs' keys untouched.
            _followsActivation = _activation != null && _activation.Attach(SyncToForeground);
            if (_followsActivation)
                SyncToForeground();
            else
                InstallLowLevel();

            Log.Debug("KeyboardHook: installed (WH_KEYBOARD={K}, WH_KEYBOARD_LL={LL}, followsActivation={F}) on thread {ThreadId}",
                _hookHandle != IntPtr.Zero, _llHookHandle != IntPtr.Zero, _followsActivation, threadId);
        }

        /// <summary>
        /// Puts the low-level hook on while SSMS's window is in front and takes it off otherwise, so another
        /// program's keys never pass through it. Called on every hint that the foreground may have changed
        /// — Windows' foreground notification, a SQL editor getting the focus, a key reaching SSMS's UI
        /// thread while the hook is off — and decided by asking who is in front now, not by the hint:
        /// Windows does not report every switch (one made through attached input queues raises neither the
        /// foreground notification nor WM_ACTIVATEAPP). Must be called on the UI thread.
        /// </summary>
        internal void SyncToForeground()
        {
            if (!_installed || !_followsActivation || Volatile.Read(ref _disposed) != 0)
                return;

            bool inFront;
            try { inFront = _thisProcessInFront(); }
            catch { inFront = false; }

            if (inFront)
            {
                InstallLowLevel();
            }
            else
            {
                _router.Reset();
                UninstallLowLevel();
            }
        }

        private void InstallLowLevel()
        {
            if (_llHookHandle != IntPtr.Zero)
                return;

            _llHookHandle = _hooks.SetLowLevelHook(_llHookProc);
            if (_llHookHandle == IntPtr.Zero)
                Log.Warning("KeyboardHook: WH_KEYBOARD_LL failed with error {Error}", Marshal.GetLastWin32Error());
        }

        private void UninstallLowLevel()
        {
            var llHandle = Interlocked.Exchange(ref _llHookHandle, IntPtr.Zero);
            if (llHandle != IntPtr.Zero)
                _hooks.Remove(llHandle);
        }

        /// <summary>
        /// Removes the keyboard hooks. Safe to call multiple times.
        /// </summary>
        public void Uninstall()
        {
            if (_followsActivation)
            {
                _activation.Detach();
                _followsActivation = false;
            }

            var handle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
                _hooks.Remove(handle);

            UninstallLowLevel();
            _router.Reset();

            _installed = false;
            _onCtrlSpace = null;
            _onFormat = null;
            _onRefresh = null;
            Log.Debug("KeyboardHook: uninstalled");
        }

        private bool SqlEditorFocused()
        {
            try { return _sqlEditorFocused?.Invoke() ?? false; }
            catch { return false; }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    // Use long to avoid OverflowException on 64-bit
                    long vkCode = wParam.ToInt64();
                    long flags = lParam.ToInt64();

                    // bit 31: transition state (0 = key going down, 1 = key going up)
                    bool keyDown = (flags & (1L << 31)) == 0;

                    if (keyDown && HandleThreadKeyDown((int)vkCode, (GetKeyState(ShortcutRouter.VK_CONTROL) & 0x8000) != 0))
                        return (IntPtr)1;

                    // NOTE: Ctrl+K,Y and Ctrl+D are handled in LowLevelHookCallback
                    // because SSMS's accelerator table captures them before WH_KEYBOARD.
                }
            }
            catch { }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>Ctrl+Space on the UI thread; true when the key is kept from its window.</summary>
        internal bool HandleThreadKeyDown(int vk, bool ctrl)
        {
            // A key on SSMS's UI thread means SSMS is in front: if the switch went unreported and the
            // low-level hook is off, put it on now (Ctrl+K,Y then works from the next key).
            if (_llHookHandle == IntPtr.Zero)
                SyncToForeground();

            if (ShortcutRouter.OnThreadKeyDown(vk, ctrl, SqlEditorFocused()) != ShortcutAction.Complete)
                return false;

            try { _onCtrlSpace?.Invoke(); } catch { }
            return true;
        }

        /// <summary>
        /// WH_KEYBOARD_LL callback for Ctrl+K,Y chord and Ctrl+D.
        /// Low-level hooks run before TranslateAccelerator, so they see
        /// keystrokes that SSMS's accelerator table would otherwise consume.
        /// wParam = WM_KEYDOWN/WM_KEYUP, lParam = pointer to KBDLLHOOKSTRUCT.
        /// Runs for every key typed on the desktop — keep it short.
        /// </summary>
        private IntPtr LowLevelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // KBDLLHOOKSTRUCT: vkCode (0), scanCode (4), flags (8), time (12), dwExtraInfo (16).
                // Keys this hook typed itself (a replayed chord) go straight to SSMS.
                if (nCode >= 0 && wParam.ToInt64() == WM_KEYDOWN
                    && Marshal.ReadIntPtr(lParam, 16) != Win32Hooks.ReplayMarker)
                {
                    // GetAsyncKeyState: the low-level hook runs before any thread's key state is updated.
                    int vkCode = Marshal.ReadInt32(lParam);
                    bool ctrlHeld  = Held(ShortcutRouter.VK_CONTROL);
                    bool shiftHeld = Held(ShortcutRouter.VK_SHIFT);
                    bool altHeld   = Held(ShortcutRouter.VK_MENU);

                    if (HandleLowLevelKeyDown(vkCode, ctrlHeld, shiftHeld, altHeld,
                            scan: Marshal.ReadInt32(lParam, 4),
                            extended: (Marshal.ReadInt32(lParam, 8) & LLKHF_EXTENDED) != 0,
                            heldShiftAndAlt: shiftHeld || altHeld ? HeldShiftAndAlt() : null))
                        return (IntPtr)1;
                }
            }
            catch { }

            return CallNextHookEx(_llHookHandle, nCode, wParam, lParam);
        }

        private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private static int[] HeldShiftAndAlt()
        {
            var held = new System.Collections.Generic.List<int>(2);
            foreach (int vk in SideModifiers)
                if (Held(vk))
                    held.Add(vk);
            return held.ToArray();
        }

        private static readonly int[] SideModifiers =
        {
            ShortcutRouter.VK_LSHIFT, ShortcutRouter.VK_RSHIFT, ShortcutRouter.VK_LMENU, ShortcutRouter.VK_RMENU,
        };

        /// <summary>One desktop key-down; true when the key is kept from its window.</summary>
        internal bool HandleLowLevelKeyDown(int vk, bool ctrl, bool shift, bool alt = false,
            int scan = 0, bool extended = false, int[] heldShiftAndAlt = null)
        {
            // Only a Ctrl chord or the key after Ctrl+K can be ours: everything else (most typing, in
            // every program) passes without asking which window is in front.
            if (!ctrl && !_router.WaitingForChord)
                return false;

            bool inFront;
            try { inFront = _thisProcessInFront(); }
            catch { inFront = false; }

            switch (_router.OnLowLevelKeyDown(vk, ctrl, shift, alt, inFront, inFront && SqlEditorFocused()))
            {
                case ShortcutAction.Replay:
                    // Sent from inside the callback: SendInput queues the keys as one block behind this
                    // one, so nothing typed later can come between Ctrl+K and its second key.
                    try { _hooks.Inject(ShortcutRouter.ReplayChord(vk, scan, extended, ctrl, heldShiftAndAlt ?? NoModifiers)); }
                    catch (Exception ex) { Log.Debug(ex, "KeyboardHook LL: chord replay failed"); }
                    return true;
                case ShortcutAction.Swallow:
                    Log.Debug("KeyboardHook LL: Ctrl+K detected, waiting for Y");
                    return true;
                case ShortcutAction.Format:
                    Log.Debug("KeyboardHook LL: format shortcut");
                    try { _onFormat?.Invoke(); } catch { }
                    return true;
                case ShortcutAction.Refresh:
                    Log.Debug("KeyboardHook LL: Ctrl+Shift+D detected, invoking refresh");
                    try { _onRefresh?.Invoke(); } catch { }
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the foreground window belongs to this process (SSMS).</summary>
        internal static bool ForegroundIsThisProcess()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(window, out uint processId);
            return processId == CurrentProcessId;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                Uninstall();
            }
        }
    }

    /// <summary>The SetWindowsHookEx calls <see cref="KeyboardHook"/> makes (a seam for tests).</summary>
    internal interface IWindowsHooks
    {
        IntPtr SetThreadHook(KeyboardHook.HookProc proc, uint threadId);
        IntPtr SetLowLevelHook(KeyboardHook.HookProc proc);
        void Remove(IntPtr handle);

        /// <summary>Types the keys as one block, marked so the hook hands them straight on.</summary>
        void Inject(KeyStroke[] keys);
    }

    /// <summary>Reports that the foreground window may have changed (which one is asked separately).</summary>
    internal interface IAppActivation
    {
        /// <summary>Starts reporting; false when the notification is unavailable.</summary>
        bool Attach(Action foregroundChanged);
        void Detach();
    }

    internal sealed class Win32Hooks : IWindowsHooks
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardHook.HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_KEYBOARD = 2;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;

        /// <summary>dwExtraInfo of the keys AKML types itself ("AKML"), so its own hook lets them through.</summary>
        internal static readonly IntPtr ReplayMarker = new IntPtr(0x414B4D4C);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // The union must be as large as its largest member (MOUSEINPUT) or SendInput rejects cbSize.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static readonly Win32Hooks Instance = new Win32Hooks();

        private Win32Hooks() { }

        public IntPtr SetThreadHook(KeyboardHook.HookProc proc, uint threadId)
            => SetWindowsHookEx(WH_KEYBOARD, proc, IntPtr.Zero, threadId);

        public IntPtr SetLowLevelHook(KeyboardHook.HookProc proc)
        {
            // IMPORTANT: Use LoadLibrary("user32.dll") as the module handle.
            // Process.MainModule.BaseAddress does NOT work for managed .NET processes
            // because SetWindowsHookEx needs a real HMODULE, not just a base address.
            IntPtr moduleHandle = LoadLibrary("user32.dll");
            if (moduleHandle == IntPtr.Zero)
                Log.Warning("KeyboardHook: LoadLibrary(user32.dll) failed with error {Error}", Marshal.GetLastWin32Error());
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
        }

        public void Remove(IntPtr handle) => UnhookWindowsHookEx(handle);

        public void Inject(KeyStroke[] keys)
        {
            var inputs = new INPUT[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                uint scan = key.Scan != 0 ? (uint)key.Scan : MapVirtualKey((uint)key.Vk, MAPVK_VK_TO_VSC);
                inputs[i].type = INPUT_KEYBOARD;
                inputs[i].u.ki = new KEYBDINPUT
                {
                    wVk = (ushort)key.Vk,
                    wScan = (ushort)scan,
                    dwFlags = (key.Up ? KEYEVENTF_KEYUP : 0) | (key.Extended ? KEYEVENTF_EXTENDEDKEY : 0),
                    dwExtraInfo = ReplayMarker,
                };
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
                Log.Debug("KeyboardHook: SendInput sent {Sent} of {Count} keys, error {Error}", sent, inputs.Length, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Windows' foreground-change notification (EVENT_SYSTEM_FOREGROUND, out of context). It intercepts
    /// nothing, so it costs other programs nothing. WPF's Application.Activated is not used: it can be raised before Windows
    /// actually gives SSMS the foreground (a window shown while another program holds it), and then no
    /// later event comes when it does — the hook would stay off while SSMS is in front.
    /// Callbacks arrive through the message loop of the thread that attached (SSMS's UI thread).
    /// </summary>
    internal sealed class ForegroundWatcher : IAppActivation
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint eventThread, uint eventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private IntPtr _hook;
        private WinEventProc _proc;   // held: user32 keeps a native pointer to it
        private Action _changed;

        public bool Attach(Action foregroundChanged)
        {
            if (_hook != IntPtr.Zero)
                return true;

            _changed = foregroundChanged;
            _proc = OnForegroundChanged;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (_hook == IntPtr.Zero)
                Log.Warning("KeyboardHook: foreground notification unavailable; the low-level hook stays in place");
            return _hook != IntPtr.Zero;
        }

        public void Detach()
        {
            var hook = _hook;
            _hook = IntPtr.Zero;
            if (hook != IntPtr.Zero)
                UnhookWinEvent(hook);
        }

        private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint eventThread, uint eventTime)
        {
            try
            {
                if (_hook != IntPtr.Zero)
                    _changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "KeyboardHook: foreground notification failed");
            }
        }
    }
}

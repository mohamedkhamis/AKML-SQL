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
    /// SSMS is the active application (taken off when another program is activated, put back when SSMS
    /// is), and every key it sees is handed on untouched unless SSMS's own window is in front and an
    /// AKML SQL editor has the focus (<see cref="ShortcutRouter"/>). Before this, Visual Studio's
    /// Ctrl+K chords and Ctrl+D stopped working for as long as SSMS had a query open.
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

        public KeyboardHook() : this(Win32Hooks.Instance, ForegroundIsThisProcess, new WpfAppActivation()) { }

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

            // The low-level hook follows SSMS's activation. Without an activation source it stays in
            // place for the session; the foreground test in the callback still keeps other programs'
            // keys untouched.
            _followsActivation = _activation != null && _activation.Attach(OnAppActivated, OnAppDeactivated);
            if (!_followsActivation || _thisProcessInFront())
                InstallLowLevel();

            Log.Debug("KeyboardHook: installed (WH_KEYBOARD={K}, WH_KEYBOARD_LL={LL}, followsActivation={F}) on thread {ThreadId}",
                _hookHandle != IntPtr.Zero, _llHookHandle != IntPtr.Zero, _followsActivation, threadId);
        }

        /// <summary>SSMS became the active application: put the low-level hook back.</summary>
        internal void OnAppActivated()
        {
            if (_installed && Volatile.Read(ref _disposed) == 0)
                InstallLowLevel();
        }

        /// <summary>Another program became active: take the low-level hook off so it never sees its keys.</summary>
        internal void OnAppDeactivated()
        {
            _router.Reset();
            UninstallLowLevel();
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
                if (nCode >= 0 && wParam.ToInt64() == WM_KEYDOWN)
                {
                    // KBDLLHOOKSTRUCT: first DWORD is vkCode. GetAsyncKeyState: the low-level hook runs
                    // before any thread's key state is updated.
                    int vkCode = Marshal.ReadInt32(lParam);
                    bool ctrlHeld  = (GetAsyncKeyState(ShortcutRouter.VK_CONTROL) & 0x8000) != 0;
                    bool shiftHeld = (GetAsyncKeyState(ShortcutRouter.VK_SHIFT) & 0x8000) != 0;

                    if (HandleLowLevelKeyDown(vkCode, ctrlHeld, shiftHeld))
                        return (IntPtr)1;
                }
            }
            catch { }

            return CallNextHookEx(_llHookHandle, nCode, wParam, lParam);
        }

        /// <summary>One desktop key-down; true when the key is kept from its window.</summary>
        internal bool HandleLowLevelKeyDown(int vk, bool ctrl, bool shift)
        {
            // Only a Ctrl chord or the key after Ctrl+K can be ours: everything else (most typing, in
            // every program) passes without asking which window is in front.
            if (!ctrl && !_router.WaitingForChord)
                return false;

            bool inFront;
            try { inFront = _thisProcessInFront(); }
            catch { inFront = false; }

            switch (_router.OnLowLevelKeyDown(vk, ctrl, shift, inFront, inFront && SqlEditorFocused()))
            {
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
    }

    /// <summary>When SSMS becomes, and stops being, the active application.</summary>
    internal interface IAppActivation
    {
        /// <summary>Starts reporting; false when there is nothing to follow (no WPF application).</summary>
        bool Attach(Action activated, Action deactivated);
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

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_KEYBOARD = 2;

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
    }

    /// <summary>
    /// WPF's Application.Activated / Deactivated: raised when SSMS as a whole gains or loses the
    /// foreground (WM_ACTIVATEAPP), not when focus moves between SSMS's own windows.
    /// </summary>
    internal sealed class WpfAppActivation : IAppActivation
    {
        private System.Windows.Application _app;
        private EventHandler _activated;
        private EventHandler _deactivated;

        public bool Attach(Action activated, Action deactivated)
        {
            var app = System.Windows.Application.Current;
            if (app == null || !app.Dispatcher.CheckAccess())
                return false;

            _app = app;
            _activated = (s, e) => activated();
            _deactivated = (s, e) => deactivated();
            app.Activated += _activated;
            app.Deactivated += _deactivated;
            return true;
        }

        public void Detach()
        {
            var app = _app;
            if (app == null)
                return;
            _app = null;
            app.Activated -= _activated;
            app.Deactivated -= _deactivated;
        }
    }
}

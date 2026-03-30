using System;
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
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        // P/Invoke declarations
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_KEYBOARD = 2;
        private const int HC_ACTION = 0;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_SPACE = 0x20;
        private const int VK_CONTROL = 0x11;
        private const int VK_K = 0x4B;
        private const int VK_Y = 0x59;

        private IntPtr _hookHandle = IntPtr.Zero;
        private HookProc _hookProc;  // prevent GC collection of the delegate
        private Action _onCtrlSpace;
        private Action _onFormat;
        private int _disposed;
        private bool _waitingForY;
        private IntPtr _llHookHandle = IntPtr.Zero;
        private HookProc _llHookProc;

        /// <summary>
        /// Installs a WH_KEYBOARD hook on the current (UI) thread.
        /// Must be called from the UI thread.
        /// </summary>
        /// <param name="onCtrlSpace">Callback invoked when Ctrl+Space is pressed. Called on the UI thread.</param>
        public void Install(Action onCtrlSpace, Action onFormat = null)
        {
            if (onCtrlSpace == null)
                throw new ArgumentNullException(nameof(onCtrlSpace));

            if (_hookHandle != IntPtr.Zero)
                return; // Already installed

            _onCtrlSpace = onCtrlSpace;
            _onFormat = onFormat;

            // Must hold a reference to prevent the delegate from being garbage-collected
            // while user32.dll still holds a native pointer to it.
            _hookProc = HookCallback;

            uint threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD, _hookProc, IntPtr.Zero, threadId);

            // WH_KEYBOARD_LL for Ctrl+K,Y chord. Uses MAIN PROCESS module handle
            // (not our assembly's handle). LL hooks run before TranslateAccelerator.
            _llHookProc = LowLevelHookCallback;
            IntPtr moduleHandle;
            try { moduleHandle = System.Diagnostics.Process.GetCurrentProcess().MainModule.BaseAddress; }
            catch { moduleHandle = IntPtr.Zero; }
            _llHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _llHookProc, moduleHandle, 0);
            Log.Debug("KeyboardHook: WH_KEYBOARD_LL installed={Installed} handle={Handle} module={Module}",
                _llHookHandle != IntPtr.Zero, _llHookHandle, moduleHandle);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("KeyboardHook: WH_KEYBOARD failed with error {Error}", error);
            }

            if (_llHookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("KeyboardHook: WH_KEYBOARD_LL failed with error {Error}", error);
            }

            if (_hookHandle != IntPtr.Zero || _llHookHandle != IntPtr.Zero)
            {
                Log.Debug("KeyboardHook: installed (WH_KEYBOARD={K}, WH_KEYBOARD_LL={LL}) on thread {ThreadId}",
                    _hookHandle != IntPtr.Zero, _llHookHandle != IntPtr.Zero, threadId);
            }
        }

        /// <summary>
        /// Removes the keyboard hook. Safe to call multiple times.
        /// </summary>
        public void Uninstall()
        {
            var handle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
                UnhookWindowsHookEx(handle);

            var llHandle = Interlocked.Exchange(ref _llHookHandle, IntPtr.Zero);
            if (llHandle != IntPtr.Zero)
                UnhookWindowsHookEx(llHandle);

            _onCtrlSpace = null;
            _onFormat = null;
            Log.Debug("KeyboardHook: uninstalled");
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

                    if (keyDown)
                    {
                        short ctrlState = GetKeyState(VK_CONTROL);
                        bool ctrlHeld = (ctrlState & 0x8000) != 0;

                        // Log ALL Ctrl+key combos to see what the hook receives
                        if (ctrlHeld)
                        {
                            Log.Debug("KeyboardHook: Ctrl+{VK} (0x{VKHex:X2})", (char)vkCode, vkCode);
                        }

                        // Ctrl+Space → trigger completion
                        if (ctrlHeld && vkCode == VK_SPACE)
                        {
                            try { _onCtrlSpace?.Invoke(); } catch { }
                            return (IntPtr)1;
                        }

                        // Ctrl+D → format document (fallback for Ctrl+K,Y)
                        // VK_D = 0x44
                        if (ctrlHeld && vkCode == 0x44)
                        {
                            try { _onFormat?.Invoke(); } catch { }
                            return (IntPtr)1;
                        }

                    }
                }
            }
            catch { }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// WH_KEYBOARD_LL callback for Ctrl+K,Y chord.
        /// Low-level hooks run before TranslateAccelerator, so they see
        /// keystrokes that SSMS's accelerator table would otherwise consume.
        /// wParam = WM_KEYDOWN/WM_KEYUP, lParam = pointer to KBDLLHOOKSTRUCT.
        /// </summary>
        private IntPtr LowLevelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt64() == WM_KEYDOWN)
                {
                    // KBDLLHOOKSTRUCT: first DWORD is vkCode
                    int vkCode = Marshal.ReadInt32(lParam);
                    short ctrlState = GetKeyState(VK_CONTROL);
                    bool ctrlHeld = (ctrlState & 0x8000) != 0;

                    if (ctrlHeld)
                        Log.Debug("KeyboardHook LL: Ctrl+VK=0x{VK:X2}", vkCode);

                    // Ctrl+K → start chord
                    if (ctrlHeld && vkCode == VK_K)
                    {
                        _waitingForY = true;
                        return (IntPtr)1; // Swallow
                    }

                    // Y after Ctrl+K → format
                    if (_waitingForY && vkCode == VK_Y)
                    {
                        _waitingForY = false;
                        try { _onFormat?.Invoke(); } catch { }
                        return (IntPtr)1; // Swallow
                    }

                    // Reset chord on non-modifier
                    if (_waitingForY && vkCode != VK_CONTROL)
                    {
                        _waitingForY = false;
                    }
                }
            }
            catch { }

            return CallNextHookEx(_llHookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                Uninstall();
            }
        }
    }
}

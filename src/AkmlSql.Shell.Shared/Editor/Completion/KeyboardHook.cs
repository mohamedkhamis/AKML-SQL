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

        private const int WH_KEYBOARD = 2;
        private const int HC_ACTION = 0;
        private const int VK_SPACE = 0x20;
        private const int VK_CONTROL = 0x11;

        private IntPtr _hookHandle = IntPtr.Zero;
        private HookProc _hookProc;  // prevent GC collection of the delegate
        private Action _onCtrlSpace;
        private int _disposed;

        /// <summary>
        /// Installs a WH_KEYBOARD hook on the current (UI) thread.
        /// Must be called from the UI thread.
        /// </summary>
        /// <param name="onCtrlSpace">Callback invoked when Ctrl+Space is pressed. Called on the UI thread.</param>
        public void Install(Action onCtrlSpace)
        {
            if (onCtrlSpace == null)
                throw new ArgumentNullException(nameof(onCtrlSpace));

            if (_hookHandle != IntPtr.Zero)
                return; // Already installed

            _onCtrlSpace = onCtrlSpace;

            // Must hold a reference to prevent the delegate from being garbage-collected
            // while user32.dll still holds a native pointer to it.
            _hookProc = HookCallback;

            uint threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD, _hookProc, IntPtr.Zero, threadId);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("KeyboardHook: SetWindowsHookEx failed with error {Error}", error);
            }
            else
            {
                Log.Debug("KeyboardHook: installed on thread {ThreadId}", threadId);
            }
        }

        /// <summary>
        /// Removes the keyboard hook. Safe to call multiple times.
        /// </summary>
        public void Uninstall()
        {
            var handle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(handle);
                Log.Debug("KeyboardHook: uninstalled");
            }

            _onCtrlSpace = null;
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

                    if (keyDown && vkCode == VK_SPACE)
                    {
                        short ctrlState = GetKeyState(VK_CONTROL);
                        if ((ctrlState & 0x8000) != 0)
                        {
                            try
                            {
                                _onCtrlSpace?.Invoke();
                            }
                            catch { /* never crash the hook */ }

                            return (IntPtr)1; // Swallow Ctrl+Space
                        }
                    }
                }
            }
            catch
            {
                // NEVER let an exception escape a native callback — it crashes the process
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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

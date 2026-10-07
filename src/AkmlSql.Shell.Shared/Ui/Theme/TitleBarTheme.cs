#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Makes a window's Windows-drawn title bar dark or light to match the AKML theme. WPF draws
    /// everything below the title bar, so a dark window kept a white title bar over it.
    /// </summary>
    internal static class TitleBarTheme
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1 and later, 19 before that.
        private const int UseImmersiveDarkMode = 20;
        private const int UseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Applies once the window has a handle (from <see cref="Window.SourceInitialized"/> on).
        /// Does nothing where Windows has no dark title bar.</summary>
        internal static void Apply(Window window, bool dark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                var value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                // Older Windows: the title bar keeps the system colours.
            }
        }
    }
}

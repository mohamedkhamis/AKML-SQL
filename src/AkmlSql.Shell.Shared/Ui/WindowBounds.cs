#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// Keeps a window inside the work area of the monitor it is on. <see cref="WindowStartupLocation.CenterOwner"/>
    /// centres a dialog on its owner, and a maximised SSMS reports a rectangle larger than the screen,
    /// so a tall dialog could open with its buttons under the taskbar.
    /// </summary>
    internal static class WindowBounds
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        private const uint DefaultToNearest = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        /// <summary>Moves (and if need be shrinks) a shown window so it fits its monitor's work area.</summary>
        internal static void KeepInWorkArea(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var source = PresentationSource.FromVisual(window);
                if (hwnd == IntPtr.Zero || source?.CompositionTarget == null || window.WindowState != WindowState.Normal) return;
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, DefaultToNearest), ref info)) return;

                // The work area in device pixels → the window's DIPs.
                var toDip = source.CompositionTarget.TransformFromDevice;
                var topLeft = toDip.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = toDip.Transform(new Point(info.Work.Right, info.Work.Bottom));
                var work = new System.Windows.Rect(topLeft, bottomRight);

                if (window.ActualWidth > work.Width) window.Width = work.Width;
                if (window.ActualHeight > work.Height) window.Height = work.Height;
                var width = Math.Min(window.ActualWidth, work.Width);
                var height = Math.Min(window.ActualHeight, work.Height);
                if (window.Left + width > work.Right) window.Left = work.Right - width;
                if (window.Top + height > work.Bottom) window.Top = work.Bottom - height;
                if (window.Left < work.Left) window.Left = work.Left;
                if (window.Top < work.Top) window.Top = work.Top;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException)
            {
                // Best effort: the window stays where Windows put it.
            }
        }
    }
}

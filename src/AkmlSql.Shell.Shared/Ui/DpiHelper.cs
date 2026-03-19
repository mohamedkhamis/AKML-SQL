using System;
using System.Runtime.InteropServices;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T083: DPI helper for scaling UI elements on high-DPI monitors.
    /// Shell code: .NET Framework 4.7.2, C# 7.3 compatible.
    /// </summary>
    public static class DpiHelper
    {
        private static double _cachedScaleFactor;
        private static volatile bool _cached;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Gets the DPI scale factor for the current monitor.
        /// Returns 1.0 at 96 DPI, 1.25 at 120 DPI, 1.5 at 144 DPI, 2.0 at 192 DPI, etc.
        /// </summary>
        public static double GetScaleFactor()
        {
            if (_cached)
                return _cachedScaleFactor;

            lock (_cacheLock)
            {
                if (_cached)
                    return _cachedScaleFactor;

                try
                {
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        try
                        {
                            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                            _cachedScaleFactor = dpiX / 96.0;
                        }
                        finally
                        {
                            ReleaseDC(IntPtr.Zero, hdc);
                        }
                    }
                    else
                    {
                        _cachedScaleFactor = 1.0;
                    }
                }
                catch
                {
                    _cachedScaleFactor = 1.0;
                }

                _cached = true;
                return _cachedScaleFactor;
            }
        }

        /// <summary>
        /// Scales a pixel value by the current DPI factor.
        /// </summary>
        public static int Scale(int pixels)
        {
            return (int)Math.Round(pixels * GetScaleFactor());
        }

        /// <summary>
        /// Scales a pixel value by the current DPI factor (double precision).
        /// </summary>
        public static double Scale(double pixels)
        {
            return pixels * GetScaleFactor();
        }

        /// <summary>
        /// Resets the cached DPI value. Call when the window moves to a different monitor.
        /// </summary>
        public static void InvalidateCache()
        {
            _cached = false;
        }

        private const int LOGPIXELSX = 88;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
    }
}

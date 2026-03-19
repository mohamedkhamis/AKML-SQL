using System;
using System.Drawing;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T082: Detects the current Visual Studio / SSMS theme (Dark/Light/Blue) and provides
    /// appropriate colors for the completion popup UI.
    /// Shell code: .NET Framework 4.7.2, C# 7.3 compatible.
    /// </summary>
    public enum VsThemeKind
    {
        Light,
        Dark,
        Blue
    }

    public sealed class ThemeManager
    {
        private static ThemeManager _instance;
        private static readonly object _lock = new object();

        private VsThemeKind _cachedTheme;
        private bool _themeCached;

        private ThemeManager() { }

        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ThemeManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Detects the current theme by examining the environment background color.
        /// Caches the result to avoid repeated SystemColors lookups.
        /// </summary>
        public VsThemeKind DetectTheme()
        {
            if (_themeCached)
                return _cachedTheme;

            try
            {
                var bgColor = SystemColors.Window;
                var luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255.0;

                if (luminance < 0.3)
                    _cachedTheme = VsThemeKind.Dark;
                else if (luminance < 0.7)
                    _cachedTheme = VsThemeKind.Blue;
                else
                    _cachedTheme = VsThemeKind.Light;
            }
            catch
            {
                _cachedTheme = VsThemeKind.Light;
            }

            _themeCached = true;
            return _cachedTheme;
        }

        /// <summary>
        /// Invalidates the cached theme. Call when the VS theme changes.
        /// </summary>
        public void InvalidateTheme()
        {
            _themeCached = false;
        }

        public System.Windows.Media.Color Background
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(30, 30, 30);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(214, 219, 233);
                    default:
                        return System.Windows.Media.Color.FromRgb(246, 246, 246);
                }
            }
        }

        public System.Windows.Media.Color Foreground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(220, 220, 220);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(27, 27, 28);
                    default:
                        return System.Windows.Media.Color.FromRgb(30, 30, 30);
                }
            }
        }

        public System.Windows.Media.Color Border
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(63, 63, 70);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(155, 167, 183);
                    default:
                        return System.Windows.Media.Color.FromRgb(204, 206, 219);
                }
            }
        }

        public System.Windows.Media.Color HighlightBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(51, 51, 52);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(255, 240, 208);
                    default:
                        return System.Windows.Media.Color.FromRgb(198, 198, 198);
                }
            }
        }

        public System.Windows.Media.Color HighlightForeground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(255, 255, 255);
                    default:
                        return System.Windows.Media.Color.FromRgb(0, 0, 0);
                }
            }
        }
    }
}

using System.Drawing;

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
        private static readonly object s_lock = new object();

        private VsThemeKind _cachedTheme;
        private bool _themeCached;

        private ThemeManager() { }

        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (s_lock)
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
            {
                return _cachedTheme;
            }

            try
            {
                var bgColor = SystemColors.Window;
                var luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255.0;

                if (luminance < 0.3)
                {
                    _cachedTheme = VsThemeKind.Dark;
                }
                else if (luminance < 0.7)
                {
                    _cachedTheme = VsThemeKind.Blue;
                }
                else
                {
                    _cachedTheme = VsThemeKind.Light;
                }
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

        // T104: Profile Editor environment color resource keys

        /// <summary>
        /// Background color for the SQL preview pane in the profile editor.
        /// </summary>
        public System.Windows.Media.Color PreviewBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(30, 30, 30);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(255, 255, 255);
                    default:
                        return System.Windows.Media.Color.FromRgb(255, 255, 255);
                }
            }
        }

        /// <summary>
        /// Background for the options panel area in the profile editor.
        /// </summary>
        public System.Windows.Media.Color EditorPanelBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(37, 37, 38);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(238, 242, 250);
                    default:
                        return System.Windows.Media.Color.FromRgb(251, 251, 251);
                }
            }
        }

        /// <summary>
        /// Accent color used for category headers and selected tree items.
        /// </summary>
        public System.Windows.Media.Color AccentColor
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(0, 122, 204);
                    case VsThemeKind.Blue:
                        return System.Windows.Media.Color.FromRgb(0, 114, 198);
                    default:
                        return System.Windows.Media.Color.FromRgb(0, 122, 204);
                }
            }
        }

        /// <summary>
        /// Color for the splitter/divider in the profile editor.
        /// </summary>
        public System.Windows.Media.Color SplitterColor
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

        /// <summary>
        /// Disabled / placeholder text color in the profile editor.
        /// </summary>
        public System.Windows.Media.Color PlaceholderText
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(110, 110, 110);
                    default:
                        return System.Windows.Media.Color.FromRgb(160, 160, 160);
                }
            }
        }
    }
}

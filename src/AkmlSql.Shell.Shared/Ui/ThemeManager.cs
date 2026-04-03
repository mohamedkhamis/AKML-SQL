using System.Drawing;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// Detects or applies the current theme (Dark/Light/Blue) and provides
    /// appropriate colors for AKML SQL UI components.
    /// Supports three modes:
    ///   "light"  — always light (SQL Prompt default)
    ///   "dark"   — always dark
    ///   "system" — auto-detect from VS/SSMS environment
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
        private static readonly object SLock = new();

        private VsThemeKind _cachedTheme;
        private bool _themeCached;
        private string _userTheme; // null = not loaded yet; "light"/"dark"/"system"
        private bool _userThemeLoaded;

        private ThemeManager() { }

        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (SLock)
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
        /// Sets the user theme preference. Call during package init after reading settings.
        /// Valid values: "light", "dark", "system".
        /// </summary>
        public void SetUserTheme(string theme)
        {
            var normalized = (theme ?? "light").ToLowerInvariant();
            _userThemeLoaded = true;
            if (_userTheme != normalized)
            {
                _userTheme = normalized;
                InvalidateTheme();
            }
        }

        /// <summary>
        /// Detects the current theme based on user preference or environment.
        /// Lazy-loads user theme from config.json on first call.
        /// </summary>
        public VsThemeKind DetectTheme()
        {
            if (_themeCached)
            {
                return _cachedTheme;
            }

            if (!_userThemeLoaded)
            {
                _userThemeLoaded = true;
                try
                {
                    var settings = ConfigManager.Load();
                    _userTheme = (settings.Theme ?? "light").ToLowerInvariant();
                }
                catch
                {
                    _userTheme = "light";
                }
            }

            switch (_userTheme)
            {
                case "dark":
                    _cachedTheme = VsThemeKind.Dark;
                    break;
                case "light":
                    _cachedTheme = VsThemeKind.Light;
                    break;
                default: // "system" — auto-detect from environment
                    _cachedTheme = DetectFromEnvironment();
                    break;
            }

            _themeCached = true;
            return _cachedTheme;
        }

        /// <summary>
        /// Auto-detects theme from the VS/SSMS environment background color.
        /// </summary>
        public static VsThemeKind DetectFromEnvironment()
        {
            try
            {
                var bgColor = SystemColors.Window;
                var luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255.0;

                if (luminance < 0.3)
                    return VsThemeKind.Dark;
                if (luminance < 0.7)
                    return VsThemeKind.Blue;
                return VsThemeKind.Light;
            }
            catch
            {
                return VsThemeKind.Light;
            }
        }

        /// <summary>
        /// Invalidates the cached theme. Call when the VS theme changes or user preference changes.
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

        // Profile Editor environment color resource keys

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

        // SQL Prompt History color properties

        public System.Windows.Media.Color HistoryWindowBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(30, 30, 46);
                    default:
                        return System.Windows.Media.Color.FromRgb(255, 255, 255);
                }
            }
        }

        public System.Windows.Media.Color HistoryPanelBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(20, 24, 32);
                    default:
                        return System.Windows.Media.Color.FromRgb(245, 245, 245);
                }
            }
        }

        public System.Windows.Media.Color HistorySearchBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(37, 40, 54);
                    default:
                        return System.Windows.Media.Color.FromRgb(240, 240, 240);
                }
            }
        }

        public System.Windows.Media.Color HistorySearchBorder
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(58, 63, 78);
                    default:
                        return System.Windows.Media.Color.FromRgb(204, 204, 204);
                }
            }
        }

        public System.Windows.Media.Color HistoryCodePreviewBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(12, 15, 20);
                    default:
                        return System.Windows.Media.Color.FromRgb(255, 255, 255);
                }
            }
        }

        public System.Windows.Media.Color HistorySelectedBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromArgb(25, 79, 140, 255);
                    default:
                        return System.Windows.Media.Color.FromArgb(20, 0, 120, 212);
                }
            }
        }

        public System.Windows.Media.Color HistorySelectedBorder
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromArgb(77, 79, 140, 255);
                    default:
                        return System.Windows.Media.Color.FromArgb(77, 0, 120, 212);
                }
            }
        }

        public System.Windows.Media.Color HistoryOpenIcon
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(61, 214, 140);
                    default:
                        return System.Windows.Media.Color.FromRgb(46, 204, 113);
                }
            }
        }

        public System.Windows.Media.Color HistoryClosedIcon
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(255, 92, 92);
                    default:
                        return System.Windows.Media.Color.FromRgb(231, 76, 60);
                }
            }
        }

        public System.Windows.Media.Color HistoryStarActive
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(251, 191, 36);
                    default:
                        return System.Windows.Media.Color.FromRgb(243, 156, 18);
                }
            }
        }

        public System.Windows.Media.Color HistoryStarInactive
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(58, 63, 78);
                    default:
                        return System.Windows.Media.Color.FromRgb(204, 204, 204);
                }
            }
        }

        public System.Windows.Media.Color HistoryActiveFilterBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromArgb(38, 79, 140, 255);
                    default:
                        return System.Windows.Media.Color.FromArgb(31, 0, 120, 212);
                }
            }
        }

        public System.Windows.Media.Color HistoryActiveFilterBorder
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromArgb(102, 79, 140, 255);
                    default:
                        return System.Windows.Media.Color.FromArgb(102, 0, 120, 212);
                }
            }
        }

        public System.Windows.Media.Color HistoryInactiveFilterBackground
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(37, 40, 54);
                    default:
                        return System.Windows.Media.Color.FromRgb(240, 240, 240);
                }
            }
        }

        public System.Windows.Media.Color HistoryInactiveFilterBorder
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(58, 63, 78);
                    default:
                        return System.Windows.Media.Color.FromRgb(204, 204, 204);
                }
            }
        }

        public System.Windows.Media.Color HistoryQueryName
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(212, 212, 212);
                    default:
                        return System.Windows.Media.Color.FromRgb(51, 51, 51);
                }
            }
        }

        public System.Windows.Media.Color HistoryMetadata
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(92, 99, 112);
                    default:
                        return System.Windows.Media.Color.FromRgb(153, 153, 153);
                }
            }
        }

        public System.Windows.Media.Color HistoryVersionCurrent
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(79, 140, 255);
                    default:
                        return System.Windows.Media.Color.FromRgb(0, 120, 212);
                }
            }
        }

        public System.Windows.Media.Color HistorySearchHighlight
        {
            get
            {
                switch (DetectTheme())
                {
                    case VsThemeKind.Dark:
                        return System.Windows.Media.Color.FromRgb(218, 165, 32);
                    default:
                        return System.Windows.Media.Color.FromRgb(255, 248, 220);
                }
            }
        }
    }
}

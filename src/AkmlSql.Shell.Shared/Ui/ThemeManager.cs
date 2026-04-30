using System;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// LEGACY FACADE over <see cref="ThemeRegistry"/>. New code MUST use
    /// <see cref="ThemeTokens"/> with <c>SetResourceReference</c> for theme-aware chrome.
    /// Existing callers continue to compile; their values are resolved through the new palette
    /// so themes apply correctly. Properties are <see cref="ObsoleteAttribute"/>-marked and will
    /// be removed once all call sites migrate (see spec 016 task T044).
    ///
    /// Pruned 2026-04-30 (T044): the History-specific chrome properties (HistoryWindowBackground,
    /// HistoryPanelBackground, HistorySearchBackground, HistorySearchBorder,
    /// HistoryCodePreviewBackground, HistorySelectedBackground, HistorySelectedBorder,
    /// HistoryOpenIcon, HistoryClosedIcon, HistoryStarActive, HistoryStarInactive,
    /// HistoryActiveFilterBackground, HistoryActiveFilterBorder,
    /// HistoryInactiveFilterBackground, HistoryInactiveFilterBorder, HistoryQueryName,
    /// HistoryMetadata, HistoryVersionCurrent), HighlightForeground, PreviewBackground, and
    /// the InvalidateTheme() no-op were removed after T034 collapsed all their callers onto
    /// general semantic tokens. <see cref="HistorySearchHighlight"/> remains as an FR-003
    /// semantic constant.
    /// </summary>
    [Obsolete("Use AkmlSql.Shell.Shared.Ui.Theme.ThemeTokens with SetResourceReference. Will be removed after migration (spec 016 T044).")]
    public enum VsThemeKind
    {
        Light,
        Dark,
    }

    /// <summary>
    /// LEGACY FACADE over <see cref="ThemeRegistry"/>. Each property routes to the corresponding
    /// semantic token in the new palette. New code MUST consume <see cref="ThemeTokens"/> via
    /// <c>SetResourceReference</c> instead.
    ///
    /// Properties are <see cref="ObsoleteAttribute"/>-marked but not error-level, so the codebase
    /// continues to build during incremental migration. Each property's obsolete message names the
    /// replacement token. When a property's call-site count reaches zero, it is deleted (T044).
    /// </summary>
    public sealed class ThemeManager
    {
        private static ThemeManager _instance;
        private static readonly object SLock = new object();

        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (SLock)
                    {
                        if (_instance == null) _instance = new ThemeManager();
                    }
                }
                return _instance;
            }
        }

        private ThemeManager() { }

        // -------------------------------------------------------------------
        // Legacy theme detection — forwards to ThemeRegistry.
        // -------------------------------------------------------------------

        /// <summary>
        /// Sets the user theme preference. Forwards to <see cref="ThemeRegistry.SetPreference"/>.
        /// Valid values: "light", "dark", "system".
        /// </summary>
        [Obsolete("Use ThemeRegistry.Instance.SetPreference(...).")]
        public void SetUserTheme(string theme) => ThemeRegistry.Instance.SetPreference(theme);

        /// <summary>
        /// Returns the active variant mapped to the legacy <see cref="VsThemeKind"/> enum.
        /// HighContrast is reported as <see cref="VsThemeKind.Light"/> (legacy callers don't recognize HighContrast).
        /// </summary>
        [Obsolete("Use ThemeRegistry.Instance.Current.")]
        public VsThemeKind DetectTheme()
        {
            switch (ThemeRegistry.Instance.Current)
            {
                case ThemeVariant.Dark: return VsThemeKind.Dark;
                case ThemeVariant.HighContrast: return VsThemeKind.Light;
                default: return VsThemeKind.Light;
            }
        }

        /// <summary>
        /// Auto-detects theme from the environment. Forwards to <see cref="HostThemeWatcher"/>.
        /// </summary>
        [Obsolete("Use HostThemeWatcher.Instance.LastDetectedHostVariant.")]
        public static VsThemeKind DetectFromEnvironment()
        {
            switch (HostThemeWatcher.Instance.LastDetectedHostVariant)
            {
                case ThemeVariant.Dark: return VsThemeKind.Dark;
                default: return VsThemeKind.Light;
            }
        }

        // -------------------------------------------------------------------
        // Legacy color properties — every property resolves to a token color.
        // -------------------------------------------------------------------

        private static Color FromToken(string key)
        {
            return ThemeRegistry.Instance.Resources[key] is SolidColorBrush b
                ? b.Color
                : Colors.Magenta; // sentinel — visible bug if a token is unresolved
        }

        // ---- Generic chrome (still in use by ProfileEditorDialog, SnippetManagerDialog,
        //      SchemaProgressMargin, OptionCategoryTreeBuilder until those are migrated) ----

        [Obsolete("Use ThemeTokens.SurfaceCanvas with SetResourceReference.")]
        public Color Background => FromToken(ThemeTokens.SurfaceCanvas);

        [Obsolete("Use ThemeTokens.TextPrimary with SetResourceReference.")]
        public Color Foreground => FromToken(ThemeTokens.TextPrimary);

        [Obsolete("Use ThemeTokens.BorderDefault with SetResourceReference.")]
        public Color Border => FromToken(ThemeTokens.BorderDefault);

        [Obsolete("Use ThemeTokens.SurfaceHover with SetResourceReference.")]
        public Color HighlightBackground => FromToken(ThemeTokens.SurfaceHover);

        [Obsolete("Use ThemeTokens.SurfaceElevated with SetResourceReference.")]
        public Color EditorPanelBackground => FromToken(ThemeTokens.SurfaceElevated);

        [Obsolete("Use ThemeTokens.AccentPrimary with SetResourceReference.")]
        public Color AccentColor => FromToken(ThemeTokens.AccentPrimary);

        [Obsolete("Use ThemeTokens.BorderSplitter with SetResourceReference.")]
        public Color SplitterColor => FromToken(ThemeTokens.BorderSplitter);

        [Obsolete("Use ThemeTokens.TextPlaceholder with SetResourceReference.")]
        public Color PlaceholderText => FromToken(ThemeTokens.TextPlaceholder);

        // ---- FR-003 semantic constant (intentionally theme-independent) ----

        /// <summary>
        /// Yellow Ochre #F9A825 at ~30% opacity — semantic constant, theme-independent. Used as a
        /// search-highlight overlay in the history code preview. Returned directly (not via a token)
        /// because the alpha-channel role is intentionally identical in both themes.
        /// </summary>
        [Obsolete("Use ThemeTokens.StatusWarning with appropriate alpha overlay, or hold this constant locally.")]
        public Color HistorySearchHighlight => Color.FromArgb(0x4D, 0xF9, 0xA8, 0x25);
    }
}

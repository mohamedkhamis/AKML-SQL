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
    /// </summary>
    [Obsolete("Use AkmlSql.Shell.Shared.Ui.Theme.ThemeTokens with SetResourceReference. Will be removed after migration (spec 016 T044).")]
    public enum VsThemeKind
    {
        Light,
        Dark,
        /// <summary>Deprecated; kept for source compatibility during migration. Treated as <see cref="Light"/>.</summary>
        [Obsolete("VsThemeKind.Blue is dropped. Use ThemeVariant via ThemeRegistry.Instance.Current.")]
        Blue,
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

        /// <summary>No-op in the new system; the registry is always in sync.</summary>
        [Obsolete("No-op — the theme registry is always in sync.")]
        public void InvalidateTheme() { /* intentionally no-op */ }

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

        // ---- Generic chrome ----

        [Obsolete("Use ThemeTokens.SurfaceCanvas with SetResourceReference.")]
        public Color Background => FromToken(ThemeTokens.SurfaceCanvas);

        [Obsolete("Use ThemeTokens.TextPrimary with SetResourceReference.")]
        public Color Foreground => FromToken(ThemeTokens.TextPrimary);

        [Obsolete("Use ThemeTokens.BorderDefault with SetResourceReference.")]
        public Color Border => FromToken(ThemeTokens.BorderDefault);

        [Obsolete("Use ThemeTokens.SurfaceHover with SetResourceReference.")]
        public Color HighlightBackground => FromToken(ThemeTokens.SurfaceHover);

        [Obsolete("Use ThemeTokens.TextPrimary with SetResourceReference.")]
        public Color HighlightForeground => FromToken(ThemeTokens.TextPrimary);

        [Obsolete("Use ThemeTokens.SurfacePanel with SetResourceReference.")]
        public Color PreviewBackground => FromToken(ThemeTokens.SurfacePanel);

        [Obsolete("Use ThemeTokens.SurfaceElevated with SetResourceReference.")]
        public Color EditorPanelBackground => FromToken(ThemeTokens.SurfaceElevated);

        [Obsolete("Use ThemeTokens.AccentPrimary with SetResourceReference.")]
        public Color AccentColor => FromToken(ThemeTokens.AccentPrimary);

        [Obsolete("Use ThemeTokens.BorderSplitter with SetResourceReference.")]
        public Color SplitterColor => FromToken(ThemeTokens.BorderSplitter);

        [Obsolete("Use ThemeTokens.TextPlaceholder with SetResourceReference.")]
        public Color PlaceholderText => FromToken(ThemeTokens.TextPlaceholder);

        // ---- History tool window (legacy History-specific tokens) ----

        [Obsolete("Use ThemeTokens.SurfaceCanvas.")]
        public Color HistoryWindowBackground => FromToken(ThemeTokens.SurfaceCanvas);

        [Obsolete("Use ThemeTokens.SurfacePanel.")]
        public Color HistoryPanelBackground => FromToken(ThemeTokens.SurfacePanel);

        [Obsolete("Use ThemeTokens.SurfaceInput.")]
        public Color HistorySearchBackground => FromToken(ThemeTokens.SurfaceInput);

        [Obsolete("Use ThemeTokens.BorderDefault.")]
        public Color HistorySearchBorder => FromToken(ThemeTokens.BorderDefault);

        [Obsolete("Use ThemeTokens.EditorPopupBackground.")]
        public Color HistoryCodePreviewBackground => FromToken(ThemeTokens.EditorPopupBackground);

        [Obsolete("Use ThemeTokens.SurfaceSelection.")]
        public Color HistorySelectedBackground => FromToken(ThemeTokens.SurfaceSelection);

        [Obsolete("Use ThemeTokens.BorderFocus.")]
        public Color HistorySelectedBorder => FromToken(ThemeTokens.BorderFocus);

        [Obsolete("Use ThemeTokens.StatusSuccess.")]
        public Color HistoryOpenIcon => FromToken(ThemeTokens.StatusSuccess);

        [Obsolete("Use ThemeTokens.StatusDanger.")]
        public Color HistoryClosedIcon => FromToken(ThemeTokens.StatusDanger);

        [Obsolete("Use ThemeTokens.StatusWarning.")]
        public Color HistoryStarActive => FromToken(ThemeTokens.StatusWarning);

        [Obsolete("Use ThemeTokens.BorderDefault.")]
        public Color HistoryStarInactive => FromToken(ThemeTokens.BorderDefault);

        [Obsolete("Use ThemeTokens.SurfaceSelection.")]
        public Color HistoryActiveFilterBackground => FromToken(ThemeTokens.SurfaceSelection);

        [Obsolete("Use ThemeTokens.AccentPrimary.")]
        public Color HistoryActiveFilterBorder => FromToken(ThemeTokens.AccentPrimary);

        [Obsolete("Use ThemeTokens.SurfaceInput.")]
        public Color HistoryInactiveFilterBackground => FromToken(ThemeTokens.SurfaceInput);

        [Obsolete("Use ThemeTokens.BorderDefault.")]
        public Color HistoryInactiveFilterBorder => FromToken(ThemeTokens.BorderDefault);

        [Obsolete("Use ThemeTokens.TextPrimary.")]
        public Color HistoryQueryName => FromToken(ThemeTokens.TextPrimary);

        [Obsolete("Use ThemeTokens.TextSecondary.")]
        public Color HistoryMetadata => FromToken(ThemeTokens.TextSecondary);

        [Obsolete("Use ThemeTokens.TextLink.")]
        public Color HistoryVersionCurrent => FromToken(ThemeTokens.TextLink);

        /// <summary>
        /// Yellow Ochre #F9A825 at ~30% opacity — semantic constant, theme-independent. Used as a
        /// search-highlight overlay in the history code preview. Returned directly (not via a token)
        /// because the alpha-channel role is intentionally identical in both themes.
        /// </summary>
        [Obsolete("Use ThemeTokens.StatusWarning with appropriate alpha overlay, or hold this constant locally.")]
        public Color HistorySearchHighlight => Color.FromArgb(0x4D, 0xF9, 0xA8, 0x25);
    }
}

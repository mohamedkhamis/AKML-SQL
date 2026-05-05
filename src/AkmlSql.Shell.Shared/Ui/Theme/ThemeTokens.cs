namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// String constants for every semantic theme token.
    /// Surfaces consume these via <c>FrameworkElement.SetResourceReference(&lt;property&gt;, ThemeTokens.&lt;Token&gt;)</c>.
    /// Authoritative catalog with Light/Dark/HighContrast values lives in
    /// <c>specs/016-wpf-theme-refresh/contracts/theme-tokens.md</c>.
    /// </summary>
    public static class ThemeTokens
    {
        // --- Surface group ---
        public const string SurfaceCanvas         = "Akml.Brush.Surface.Canvas";
        public const string SurfacePanel          = "Akml.Brush.Surface.Panel";
        public const string SurfaceElevated       = "Akml.Brush.Surface.Elevated";
        public const string SurfaceSidebar        = "Akml.Brush.Surface.Sidebar";
        public const string SurfaceInput          = "Akml.Brush.Surface.Input";
        public const string SurfaceInputReadOnly  = "Akml.Brush.Surface.InputReadOnly";
        public const string SurfaceHover          = "Akml.Brush.Surface.Hover";
        public const string SurfaceSelection      = "Akml.Brush.Surface.Selection";
        public const string SurfaceSelectionStrong = "Akml.Brush.Surface.SelectionStrong";

        // --- Text group ---
        public const string TextPrimary     = "Akml.Brush.Text.Primary";
        public const string TextSecondary   = "Akml.Brush.Text.Secondary";
        public const string TextDisabled    = "Akml.Brush.Text.Disabled";
        public const string TextPlaceholder = "Akml.Brush.Text.Placeholder";
        public const string TextLink        = "Akml.Brush.Text.Link";
        public const string TextOnAccent    = "Akml.Brush.Text.OnAccent";
        public const string TextOnDanger    = "Akml.Brush.Text.OnDanger";

        // --- Border group ---
        public const string BorderDefault  = "Akml.Brush.Border.Default";
        public const string BorderStrong   = "Akml.Brush.Border.Strong";
        public const string BorderSubtle   = "Akml.Brush.Border.Subtle";
        public const string BorderFocus    = "Akml.Brush.Border.Focus";
        public const string BorderSplitter = "Akml.Brush.Border.Splitter";

        // --- Accent group ---
        public const string AccentPrimary        = "Akml.Brush.Accent.Primary";
        public const string AccentPrimaryHover   = "Akml.Brush.Accent.PrimaryHover";
        public const string AccentPrimaryPressed = "Akml.Brush.Accent.PrimaryPressed";

        // --- Status group ---
        public const string StatusSuccess = "Akml.Brush.Status.Success";
        public const string StatusWarning = "Akml.Brush.Status.Warning";
        public const string StatusDanger  = "Akml.Brush.Status.Danger";
        public const string StatusInfo    = "Akml.Brush.Status.Info";

        // --- Editor group (in-editor adornments / popups / margins) ---
        public const string EditorMarginBackground = "Akml.Brush.Editor.MarginBackground";
        public const string EditorSpinnerStroke    = "Akml.Brush.Editor.SpinnerStroke";
        public const string EditorPopupBackground  = "Akml.Brush.Editor.PopupBackground";
        public const string EditorPopupBorder      = "Akml.Brush.Editor.PopupBorder";

        // --- Chat group (AI Chat tool window message bubbles) ---
        public const string ChatUserBubble      = "Akml.Brush.Chat.UserBubble";
        public const string ChatAssistantBubble = "Akml.Brush.Chat.AssistantBubble";
        public const string ChatSystemBubble    = "Akml.Brush.Chat.SystemBubble";

        /// <summary>
        /// All token keys. Used by <see cref="ThemePalette"/> to validate every variant
        /// has a brush for every key, and by tests/audits.
        /// </summary>
        public static readonly string[] All =
        {
            SurfaceCanvas, SurfacePanel, SurfaceElevated, SurfaceSidebar,
            SurfaceInput, SurfaceInputReadOnly, SurfaceHover,
            SurfaceSelection, SurfaceSelectionStrong,
            TextPrimary, TextSecondary, TextDisabled, TextPlaceholder,
            TextLink, TextOnAccent, TextOnDanger,
            BorderDefault, BorderStrong, BorderSubtle, BorderFocus, BorderSplitter,
            AccentPrimary, AccentPrimaryHover, AccentPrimaryPressed,
            StatusSuccess, StatusWarning, StatusDanger, StatusInfo,
            EditorMarginBackground, EditorSpinnerStroke,
            EditorPopupBackground, EditorPopupBorder,
            ChatUserBubble, ChatAssistantBubble, ChatSystemBubble,
        };
    }
}

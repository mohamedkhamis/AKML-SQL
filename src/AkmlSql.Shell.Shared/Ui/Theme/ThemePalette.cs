using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Per-variant <c>Token.Key → SolidColorBrush</c> map. One palette per <see cref="ThemeVariant"/>.
    /// Brushes are <c>Freeze()</c>-d before insertion. Construction validates that every key in
    /// <see cref="ThemeTokens.All"/> has a brush in the palette — fail fast.
    /// Authoritative color values live in <c>contracts/theme-tokens.md</c>.
    /// </summary>
    internal sealed class ThemePalette
    {
        public ThemeVariant Variant { get; }
        public IReadOnlyDictionary<string, SolidColorBrush> Brushes { get; }

        private ThemePalette(ThemeVariant variant, IDictionary<string, SolidColorBrush> brushes)
        {
            Variant = variant;
            // Validate completeness — every token MUST have a brush.
            foreach (var key in ThemeTokens.All)
            {
                if (!brushes.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"ThemePalette[{variant}] missing brush for token '{key}'.");
                }
            }
            Brushes = (IReadOnlyDictionary<string, SolidColorBrush>)brushes;
        }

        // -------------------------------------------------------------------
        // Variant builders
        // -------------------------------------------------------------------

        public static readonly ThemePalette Light = BuildLight();
        public static readonly ThemePalette Dark  = BuildDark();
        public static readonly ThemePalette HighContrast = BuildHighContrast();

        public static ThemePalette ForVariant(ThemeVariant v)
        {
            switch (v)
            {
                case ThemeVariant.Dark: return Dark;
                case ThemeVariant.HighContrast: return HighContrast;
                default: return Light;
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        // -------------------------------------------------------------------
        // Light palette — values from contracts/theme-tokens.md
        // -------------------------------------------------------------------
        private static ThemePalette BuildLight()
        {
            var d = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal)
            {
                // Surface (v2 AKML-Blue / slate)
                [ThemeTokens.SurfaceCanvas]          = Solid(0xF1, 0xF5, 0xF9),
                [ThemeTokens.SurfacePanel]           = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceElevated]        = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceSidebar]         = Solid(0xF8, 0xFA, 0xFC),
                [ThemeTokens.SurfaceInput]           = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceInputReadOnly]   = Solid(0xF8, 0xFA, 0xFC),
                [ThemeTokens.SurfaceHover]           = Solid(0xF1, 0xF5, 0xF9),
                [ThemeTokens.SurfaceSelection]       = Solid(0x1F, 0x25, 0x63, 0xEB), // ~12% AKML Blue
                [ThemeTokens.SurfaceSelectionStrong] = Solid(0x25, 0x63, 0xEB),

                // Text
                [ThemeTokens.TextPrimary]     = Solid(0x0F, 0x17, 0x2A),
                [ThemeTokens.TextSecondary]   = Solid(0x47, 0x55, 0x69),
                [ThemeTokens.TextDisabled]    = Solid(0x94, 0xA3, 0xB8),
                [ThemeTokens.TextPlaceholder] = Solid(0x94, 0xA3, 0xB8),
                [ThemeTokens.TextLink]        = Solid(0x25, 0x63, 0xEB),
                [ThemeTokens.TextOnAccent]    = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.TextOnDanger]    = Solid(0xFF, 0xFF, 0xFF),

                // Border
                [ThemeTokens.BorderDefault]  = Solid(0xE2, 0xE8, 0xF0),
                [ThemeTokens.BorderStrong]   = Solid(0xCB, 0xD5, 0xE1),
                [ThemeTokens.BorderSubtle]   = Solid(0xE2, 0xE8, 0xF0),
                [ThemeTokens.BorderFocus]    = Solid(0x25, 0x63, 0xEB),
                [ThemeTokens.BorderSplitter] = Solid(0xE2, 0xE8, 0xF0),

                // Accent (AKML Blue)
                [ThemeTokens.AccentPrimary]        = Solid(0x25, 0x63, 0xEB),
                [ThemeTokens.AccentPrimaryHover]   = Solid(0x1E, 0x40, 0xAF),
                [ThemeTokens.AccentPrimaryPressed] = Solid(0x1E, 0x40, 0xAF),

                // Status
                [ThemeTokens.StatusSuccess] = Solid(0x16, 0xA3, 0x4A),
                [ThemeTokens.StatusWarning] = Solid(0xEA, 0x58, 0x0C),
                [ThemeTokens.StatusDanger]  = Solid(0xDC, 0x26, 0x26),
                [ThemeTokens.StatusInfo]    = Solid(0x25, 0x63, 0xEB),

                // Editor
                [ThemeTokens.EditorMarginBackground] = Solid(0xF8, 0xFA, 0xFC),
                [ThemeTokens.EditorSpinnerStroke]    = Solid(0x25, 0x63, 0xEB),
                [ThemeTokens.EditorPopupBackground]  = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.EditorPopupBorder]      = Solid(0xE2, 0xE8, 0xF0),

                // Chat
                [ThemeTokens.ChatUserBubble]      = Solid(0xDB, 0xEA, 0xFE),
                [ThemeTokens.ChatAssistantBubble] = Solid(0xF1, 0xF5, 0xF9),
                [ThemeTokens.ChatSystemBubble]    = Solid(0xFE, 0xF3, 0xC7),

                // Spec 020 — IconBadge (solid foreground colour per object type).
                // Same hex values as Dark — these are saturated badge colours intended to
                // read on both Light (#FFFFFF) and Dark (#252836) popup backgrounds.
                // Source: doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §1.2.
                [ThemeTokens.IconBadgeTable]      = Solid(0xE5, 0xC0, 0x4B),  // Yellow
                [ThemeTokens.IconBadgeView]       = Solid(0x56, 0xB6, 0xC2),  // Teal
                [ThemeTokens.IconBadgeColumn]     = Solid(0x61, 0xAF, 0xEF),  // Blue
                [ThemeTokens.IconBadgeStoredProc] = Solid(0xC6, 0x78, 0xDD),  // Purple
                [ThemeTokens.IconBadgeFunction]   = Solid(0xD1, 0x9A, 0x66),  // Orange
                [ThemeTokens.IconBadgeSnippet]    = Solid(0x3D, 0xD6, 0x8C),  // Green
                [ThemeTokens.IconBadgeKeyword]    = Solid(0xAB, 0xB2, 0xBF),  // Gray
                [ThemeTokens.IconBadgeDatabase]   = Solid(0xE0, 0x6C, 0x75),  // Red
                [ThemeTokens.IconBadgeSchema]     = Solid(0x98, 0xC3, 0x79),  // Green2
                [ThemeTokens.IconBadgeTrigger]    = Solid(0xBE, 0x50, 0x46),  // DarkRed
                [ThemeTokens.IconBadgeIndex]      = Solid(0x7F, 0x84, 0x8E),  // DimGray
                [ThemeTokens.IconBadgeSynonym]    = Solid(0x56, 0xB6, 0xC2),  // Teal2

                // Spec 020 — TabColor swatches (environment-colour palette; same in both themes).
                [ThemeTokens.TabColorRed]    = Solid(0xFF, 0x44, 0x44),
                [ThemeTokens.TabColorAmber]  = Solid(0xFF, 0xB8, 0x00),
                [ThemeTokens.TabColorGreen]  = Solid(0x44, 0xBB, 0x44),
                [ThemeTokens.TabColorBlue]   = Solid(0x44, 0x88, 0xFF),
                [ThemeTokens.TabColorTeal]   = Solid(0x00, 0xBC, 0xD4),
                [ThemeTokens.TabColorPurple] = Solid(0x9B, 0x59, 0xB6),
                [ThemeTokens.TabColorPink]   = Solid(0xE9, 0x1E, 0x63),
                [ThemeTokens.TabColorGray]   = Solid(0x80, 0x8A, 0x99),

                // Spec 020 — History (Light; v2 aligned to new status colours).
                [ThemeTokens.HistoryOpenIcon]       = Solid(0x16, 0xA3, 0x4A),
                [ThemeTokens.HistoryClosedIcon]     = Solid(0xDC, 0x26, 0x26),
                [ThemeTokens.HistoryStarActive]     = Solid(0xEA, 0x58, 0x0C),
                [ThemeTokens.HistoryStarInactive]   = Solid(0xE2, 0xE8, 0xF0),
                // Spec 020 PR-235 review fix: legacy ThemeManager.HistorySearchHighlight
                // returned Color.FromArgb(0x4D, ...) — 30 % alpha so editor text behind the
                // match remains readable. Preserving that alpha here against the
                // doc-specified light-theme hex (#FFF8DC cream) to avoid obscuring text.
                [ThemeTokens.HistoryMatchHighlight] = Solid(0x4D, 0xFF, 0xF8, 0xDC),
            };
            return new ThemePalette(ThemeVariant.Light, d);
        }

        // -------------------------------------------------------------------
        // Dark palette — values from contracts/theme-tokens.md
        // -------------------------------------------------------------------
        private static ThemePalette BuildDark()
        {
            var d = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal)
            {
                // Surface (v2 AKML-Blue / slate-dark)
                [ThemeTokens.SurfaceCanvas]          = Solid(0x0F, 0x17, 0x2A),
                [ThemeTokens.SurfacePanel]           = Solid(0x1E, 0x29, 0x3B),
                [ThemeTokens.SurfaceElevated]        = Solid(0x33, 0x41, 0x55),
                [ThemeTokens.SurfaceSidebar]         = Solid(0x1E, 0x29, 0x3B),
                [ThemeTokens.SurfaceInput]           = Solid(0x1E, 0x29, 0x3B),
                [ThemeTokens.SurfaceInputReadOnly]   = Solid(0x33, 0x41, 0x55),
                [ThemeTokens.SurfaceHover]           = Solid(0x33, 0x41, 0x55),
                [ThemeTokens.SurfaceSelection]       = Solid(0x26, 0x25, 0x63, 0xEB), // ~15% AKML Blue
                [ThemeTokens.SurfaceSelectionStrong] = Solid(0x25, 0x63, 0xEB),

                // Text
                [ThemeTokens.TextPrimary]     = Solid(0xE2, 0xE8, 0xF0),
                [ThemeTokens.TextSecondary]   = Solid(0x94, 0xA3, 0xB8),
                [ThemeTokens.TextDisabled]    = Solid(0x64, 0x74, 0x8B),
                [ThemeTokens.TextPlaceholder] = Solid(0x64, 0x74, 0x8B),
                [ThemeTokens.TextLink]        = Solid(0x60, 0xA5, 0xFA),
                [ThemeTokens.TextOnAccent]    = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.TextOnDanger]    = Solid(0xFF, 0xFF, 0xFF),

                // Border (low-alpha white-on-slate)
                [ThemeTokens.BorderDefault]  = Solid(0x14, 0xFF, 0xFF, 0xFF),
                [ThemeTokens.BorderStrong]   = Solid(0x28, 0xFF, 0xFF, 0xFF),
                [ThemeTokens.BorderSubtle]   = Solid(0x0D, 0xFF, 0xFF, 0xFF),
                [ThemeTokens.BorderFocus]    = Solid(0x60, 0xA5, 0xFA),
                [ThemeTokens.BorderSplitter] = Solid(0x14, 0xFF, 0xFF, 0xFF),

                // Accent (AKML Blue)
                [ThemeTokens.AccentPrimary]        = Solid(0x25, 0x63, 0xEB),
                [ThemeTokens.AccentPrimaryHover]   = Solid(0x3B, 0x82, 0xF6),
                [ThemeTokens.AccentPrimaryPressed] = Solid(0x1E, 0x40, 0xAF),

                // Status
                [ThemeTokens.StatusSuccess] = Solid(0x4A, 0xDE, 0x80),
                [ThemeTokens.StatusWarning] = Solid(0xFB, 0x92, 0x3C),
                [ThemeTokens.StatusDanger]  = Solid(0xF8, 0x71, 0x71),
                [ThemeTokens.StatusInfo]    = Solid(0x60, 0xA5, 0xFA),

                // Editor
                [ThemeTokens.EditorMarginBackground] = Solid(0x1E, 0x29, 0x3B),
                [ThemeTokens.EditorSpinnerStroke]    = Solid(0x60, 0xA5, 0xFA),
                [ThemeTokens.EditorPopupBackground]  = Solid(0x1E, 0x29, 0x3B),
                [ThemeTokens.EditorPopupBorder]      = Solid(0x14, 0xFF, 0xFF, 0xFF),

                // Chat
                [ThemeTokens.ChatUserBubble]      = Solid(0x1E, 0x3A, 0x5F),
                [ThemeTokens.ChatAssistantBubble] = Solid(0x33, 0x41, 0x55),
                [ThemeTokens.ChatSystemBubble]    = Solid(0x3A, 0x30, 0x00),

                // Spec 020 — IconBadge (same hex as Light; documented in SQL_Prompt_Features_Core.md §1.2
                // as Dark-theme values, but saturated enough for both. Badge background uses 20%-alpha
                // overlay computed at render time so contrast adapts to host popup background.)
                [ThemeTokens.IconBadgeTable]      = Solid(0xE5, 0xC0, 0x4B),
                [ThemeTokens.IconBadgeView]       = Solid(0x56, 0xB6, 0xC2),
                [ThemeTokens.IconBadgeColumn]     = Solid(0x61, 0xAF, 0xEF),
                [ThemeTokens.IconBadgeStoredProc] = Solid(0xC6, 0x78, 0xDD),
                [ThemeTokens.IconBadgeFunction]   = Solid(0xD1, 0x9A, 0x66),
                [ThemeTokens.IconBadgeSnippet]    = Solid(0x3D, 0xD6, 0x8C),
                [ThemeTokens.IconBadgeKeyword]    = Solid(0xAB, 0xB2, 0xBF),
                [ThemeTokens.IconBadgeDatabase]   = Solid(0xE0, 0x6C, 0x75),
                [ThemeTokens.IconBadgeSchema]     = Solid(0x98, 0xC3, 0x79),
                [ThemeTokens.IconBadgeTrigger]    = Solid(0xBE, 0x50, 0x46),
                [ThemeTokens.IconBadgeIndex]      = Solid(0x7F, 0x84, 0x8E),
                [ThemeTokens.IconBadgeSynonym]    = Solid(0x56, 0xB6, 0xC2),

                // Spec 020 — TabColor swatches (same in both themes).
                [ThemeTokens.TabColorRed]    = Solid(0xFF, 0x44, 0x44),
                [ThemeTokens.TabColorAmber]  = Solid(0xFF, 0xB8, 0x00),
                [ThemeTokens.TabColorGreen]  = Solid(0x44, 0xBB, 0x44),
                [ThemeTokens.TabColorBlue]   = Solid(0x44, 0x88, 0xFF),
                [ThemeTokens.TabColorTeal]   = Solid(0x00, 0xBC, 0xD4),
                [ThemeTokens.TabColorPurple] = Solid(0x9B, 0x59, 0xB6),
                [ThemeTokens.TabColorPink]   = Solid(0xE9, 0x1E, 0x63),
                [ThemeTokens.TabColorGray]   = Solid(0x80, 0x8A, 0x99),

                // Spec 020 — History (Dark; v2 aligned to new status colours).
                [ThemeTokens.HistoryOpenIcon]       = Solid(0x4A, 0xDE, 0x80),
                [ThemeTokens.HistoryClosedIcon]     = Solid(0xF8, 0x71, 0x71),
                [ThemeTokens.HistoryStarActive]     = Solid(0xFB, 0x92, 0x3C),
                [ThemeTokens.HistoryStarInactive]   = Solid(0x14, 0xFF, 0xFF, 0xFF),
                // Spec 020 PR-235 review fix: 30 % alpha (0x4D) preserves the legacy
                // ThemeManager.HistorySearchHighlight read-through behaviour. Dark-theme
                // hex (#DAA520 gold) per doc/SQL-PROMPT/SQL-Prompt-History §16.2.
                [ThemeTokens.HistoryMatchHighlight] = Solid(0x4D, 0xDA, 0xA5, 0x20),
            };
            return new ThemePalette(ThemeVariant.Dark, d);
        }

        // -------------------------------------------------------------------
        // High Contrast palette — delegates to Windows SystemColors so the
        // OS-active High Contrast scheme drives every color.
        // -------------------------------------------------------------------
        private static ThemePalette BuildHighContrast()
        {
            // SystemColors.* brushes are already frozen by WPF.
            var window = SystemColors.WindowBrush;
            var control = SystemColors.ControlBrush;
            var windowText = SystemColors.WindowTextBrush;
            var grayText = SystemColors.GrayTextBrush;
            var hotTrack = SystemColors.HotTrackBrush;
            var highlight = SystemColors.HighlightBrush;
            var highlightText = SystemColors.HighlightTextBrush;
            var windowFrame = SystemColors.WindowFrameBrush;
            var controlDark = SystemColors.ControlDarkBrush;
            var info = SystemColors.InfoBrush;

            var d = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal)
            {
                // Surface
                [ThemeTokens.SurfaceCanvas]          = window,
                [ThemeTokens.SurfacePanel]           = window,
                [ThemeTokens.SurfaceElevated]        = window,
                [ThemeTokens.SurfaceSidebar]         = control,
                [ThemeTokens.SurfaceInput]           = window,
                [ThemeTokens.SurfaceInputReadOnly]   = control,
                [ThemeTokens.SurfaceHover]           = highlight,
                [ThemeTokens.SurfaceSelection]       = highlight,
                [ThemeTokens.SurfaceSelectionStrong] = highlight,

                // Text
                [ThemeTokens.TextPrimary]     = windowText,
                [ThemeTokens.TextSecondary]   = grayText,
                [ThemeTokens.TextDisabled]    = grayText,
                [ThemeTokens.TextPlaceholder] = grayText,
                [ThemeTokens.TextLink]        = hotTrack,
                [ThemeTokens.TextOnAccent]    = highlightText,
                [ThemeTokens.TextOnDanger]    = highlightText,

                // Border
                [ThemeTokens.BorderDefault]  = windowFrame,
                [ThemeTokens.BorderStrong]   = windowFrame,
                [ThemeTokens.BorderSubtle]   = controlDark,
                [ThemeTokens.BorderFocus]    = hotTrack,
                [ThemeTokens.BorderSplitter] = controlDark,

                // Accent
                [ThemeTokens.AccentPrimary]        = highlight,
                [ThemeTokens.AccentPrimaryHover]   = highlight,
                [ThemeTokens.AccentPrimaryPressed] = highlight,

                // Status
                [ThemeTokens.StatusSuccess] = highlight,
                [ThemeTokens.StatusWarning] = highlight,
                [ThemeTokens.StatusDanger]  = highlight,
                [ThemeTokens.StatusInfo]    = hotTrack,

                // Editor
                [ThemeTokens.EditorMarginBackground] = control,
                [ThemeTokens.EditorSpinnerStroke]    = hotTrack,
                [ThemeTokens.EditorPopupBackground]  = window,
                [ThemeTokens.EditorPopupBorder]      = windowFrame,

                // Chat
                [ThemeTokens.ChatUserBubble]      = window,
                [ThemeTokens.ChatAssistantBubble] = control,
                [ThemeTokens.ChatSystemBubble]    = info,

                // Spec 020 — IconBadge under High Contrast: SystemColors.HotTrack for emphasis,
                // GrayText for muted variants. Object type is still conveyed by the letter glyph
                // (the badge is paired with a letter in the renderer, per FR-029).
                [ThemeTokens.IconBadgeTable]      = hotTrack,
                [ThemeTokens.IconBadgeView]       = hotTrack,
                [ThemeTokens.IconBadgeColumn]     = hotTrack,
                [ThemeTokens.IconBadgeStoredProc] = hotTrack,
                [ThemeTokens.IconBadgeFunction]   = hotTrack,
                [ThemeTokens.IconBadgeSnippet]    = hotTrack,
                [ThemeTokens.IconBadgeKeyword]    = grayText,
                [ThemeTokens.IconBadgeDatabase]   = hotTrack,
                [ThemeTokens.IconBadgeSchema]     = hotTrack,
                [ThemeTokens.IconBadgeTrigger]    = hotTrack,
                [ThemeTokens.IconBadgeIndex]      = grayText,
                [ThemeTokens.IconBadgeSynonym]    = hotTrack,

                // Spec 020 — TabColor swatches under High Contrast: route to HotTrack for emphasis.
                // Tab-coloring rules typically rely on label / shape glyph at this contrast level
                // anyway, and the OS palette overrides anything we'd pick.
                [ThemeTokens.TabColorRed]    = hotTrack,
                [ThemeTokens.TabColorAmber]  = hotTrack,
                [ThemeTokens.TabColorGreen]  = hotTrack,
                [ThemeTokens.TabColorBlue]   = hotTrack,
                [ThemeTokens.TabColorTeal]   = hotTrack,
                [ThemeTokens.TabColorPurple] = hotTrack,
                [ThemeTokens.TabColorPink]   = hotTrack,
                [ThemeTokens.TabColorGray]   = grayText,

                // Spec 020 — History under High Contrast.
                [ThemeTokens.HistoryOpenIcon]       = hotTrack,
                [ThemeTokens.HistoryClosedIcon]     = hotTrack,
                [ThemeTokens.HistoryStarActive]     = hotTrack,
                [ThemeTokens.HistoryStarInactive]   = grayText,
                [ThemeTokens.HistoryMatchHighlight] = highlight,
            };
            return new ThemePalette(ThemeVariant.HighContrast, d);
        }
    }
}

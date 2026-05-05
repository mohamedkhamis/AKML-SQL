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
                // Surface
                [ThemeTokens.SurfaceCanvas]          = Solid(0xF0, 0xF0, 0xF0),
                [ThemeTokens.SurfacePanel]           = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceElevated]        = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceSidebar]         = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceInput]           = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.SurfaceInputReadOnly]   = Solid(0xF8, 0xF8, 0xF8),
                [ThemeTokens.SurfaceHover]           = Solid(0xF0, 0xF0, 0xF0),
                [ThemeTokens.SurfaceSelection]       = Solid(0x1F, 0x00, 0x78, 0xD4), // ~12% accent
                [ThemeTokens.SurfaceSelectionStrong] = Solid(0x00, 0x78, 0xD4),

                // Text
                [ThemeTokens.TextPrimary]     = Solid(0x1E, 0x1E, 0x1E),
                [ThemeTokens.TextSecondary]   = Solid(0x55, 0x55, 0x55),
                [ThemeTokens.TextDisabled]    = Solid(0xA0, 0xA0, 0xA0),
                [ThemeTokens.TextPlaceholder] = Solid(0xA0, 0xA0, 0xA0),
                [ThemeTokens.TextLink]        = Solid(0x00, 0x78, 0xD4),
                [ThemeTokens.TextOnAccent]    = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.TextOnDanger]    = Solid(0xFF, 0xFF, 0xFF),

                // Border
                [ThemeTokens.BorderDefault]  = Solid(0xCC, 0xCC, 0xCC),
                [ThemeTokens.BorderStrong]   = Solid(0x99, 0x99, 0x99),
                [ThemeTokens.BorderSubtle]   = Solid(0xEA, 0xEA, 0xEA),
                [ThemeTokens.BorderFocus]    = Solid(0x00, 0x78, 0xD4),
                [ThemeTokens.BorderSplitter] = Solid(0xCC, 0xCC, 0xCC),

                // Accent
                [ThemeTokens.AccentPrimary]        = Solid(0x00, 0x78, 0xD4),
                [ThemeTokens.AccentPrimaryHover]   = Solid(0x10, 0x6E, 0xBE),
                [ThemeTokens.AccentPrimaryPressed] = Solid(0x00, 0x5A, 0x9E),

                // Status
                [ThemeTokens.StatusSuccess] = Solid(0x2E, 0xCC, 0x71),
                [ThemeTokens.StatusWarning] = Solid(0xF3, 0x9C, 0x12),
                [ThemeTokens.StatusDanger]  = Solid(0xE7, 0x4C, 0x3C),
                [ThemeTokens.StatusInfo]    = Solid(0x00, 0x78, 0xD4),

                // Editor
                [ThemeTokens.EditorMarginBackground] = Solid(0xFB, 0xFB, 0xFB),
                [ThemeTokens.EditorSpinnerStroke]    = Solid(0x00, 0x78, 0xD4),
                [ThemeTokens.EditorPopupBackground]  = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.EditorPopupBorder]      = Solid(0xCC, 0xCC, 0xCC),

                // Chat
                [ThemeTokens.ChatUserBubble]      = Solid(0xE5, 0xF1, 0xFB),
                [ThemeTokens.ChatAssistantBubble] = Solid(0xF5, 0xF5, 0xF5),
                [ThemeTokens.ChatSystemBubble]    = Solid(0xFF, 0xF8, 0xE1),
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
                // Surface
                [ThemeTokens.SurfaceCanvas]          = Solid(0x2D, 0x2D, 0x3B),
                [ThemeTokens.SurfacePanel]           = Solid(0x1E, 0x1E, 0x2E),
                [ThemeTokens.SurfaceElevated]        = Solid(0x25, 0x28, 0x36),
                [ThemeTokens.SurfaceSidebar]         = Solid(0x1E, 0x1E, 0x2E),
                [ThemeTokens.SurfaceInput]           = Solid(0x2D, 0x2D, 0x3B),
                [ThemeTokens.SurfaceInputReadOnly]   = Solid(0x25, 0x28, 0x36),
                [ThemeTokens.SurfaceHover]           = Solid(0x25, 0x28, 0x36),
                [ThemeTokens.SurfaceSelection]       = Solid(0x26, 0x00, 0x78, 0xD4), // ~15% accent
                [ThemeTokens.SurfaceSelectionStrong] = Solid(0x00, 0x78, 0xD4),

                // Text
                [ThemeTokens.TextPrimary]     = Solid(0xD4, 0xD4, 0xD4),
                [ThemeTokens.TextSecondary]   = Solid(0x88, 0x92, 0xA8),
                [ThemeTokens.TextDisabled]    = Solid(0x5C, 0x63, 0x70),
                [ThemeTokens.TextPlaceholder] = Solid(0x6E, 0x6E, 0x6E),
                [ThemeTokens.TextLink]        = Solid(0x4F, 0x8C, 0xFF),
                [ThemeTokens.TextOnAccent]    = Solid(0xFF, 0xFF, 0xFF),
                [ThemeTokens.TextOnDanger]    = Solid(0xFF, 0xFF, 0xFF),

                // Border
                [ThemeTokens.BorderDefault]  = Solid(0x3A, 0x3F, 0x4E),
                [ThemeTokens.BorderStrong]   = Solid(0x5C, 0x63, 0x70),
                [ThemeTokens.BorderSubtle]   = Solid(0x2A, 0x2D, 0x3A),
                [ThemeTokens.BorderFocus]    = Solid(0x4F, 0x8C, 0xFF),
                [ThemeTokens.BorderSplitter] = Solid(0x3A, 0x3F, 0x4E),

                // Accent
                [ThemeTokens.AccentPrimary]        = Solid(0x00, 0x78, 0xD4),
                [ThemeTokens.AccentPrimaryHover]   = Solid(0x1A, 0x8C, 0xDC),
                [ThemeTokens.AccentPrimaryPressed] = Solid(0x00, 0x66, 0xB5),

                // Status
                [ThemeTokens.StatusSuccess] = Solid(0x3D, 0xD6, 0x8C),
                [ThemeTokens.StatusWarning] = Solid(0xFB, 0xBF, 0x24),
                [ThemeTokens.StatusDanger]  = Solid(0xFF, 0x5C, 0x5C),
                [ThemeTokens.StatusInfo]    = Solid(0x4F, 0x8C, 0xFF),

                // Editor
                [ThemeTokens.EditorMarginBackground] = Solid(0x25, 0x25, 0x26),
                [ThemeTokens.EditorSpinnerStroke]    = Solid(0x4F, 0x8C, 0xFF),
                [ThemeTokens.EditorPopupBackground]  = Solid(0x25, 0x25, 0x26),
                [ThemeTokens.EditorPopupBorder]      = Solid(0x3A, 0x3F, 0x4E),

                // Chat
                [ThemeTokens.ChatUserBubble]      = Solid(0x1A, 0x3A, 0x5C),
                [ThemeTokens.ChatAssistantBubble] = Solid(0x25, 0x28, 0x36),
                [ThemeTokens.ChatSystemBubble]    = Solid(0x3A, 0x30, 0x00),
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
            };
            return new ThemePalette(ThemeVariant.HighContrast, d);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Provides SQL Prompt-style colored icons for completion items.
    /// Colors match the Redgate SQL Prompt color scheme:
    ///   Table    = Blue (#1565C0)
    ///   View     = Green (#2E7D32)
    ///   Column   = Gold (#F9A825)
    ///   Keyword  = Blue-Gray (#546E7A)
    ///   Snippet  = Orange (#E65100)
    ///   Function = Magenta (#AD1457)
    ///   Procedure= Purple (#6A1B9A)
    ///   Schema   = Gray (#616161)
    ///   Database = Teal (#00695C)
    ///   Variable = Cyan (#00838F)
    ///   Alias    = Indigo (#283593)
    ///   Parameter= Brown (#4E342E)
    /// </summary>
    internal static class SqlPromptIcons
    {
        private static readonly Dictionary<int, ImageSource> Cache = new Dictionary<int, ImageSource>();
        private static readonly object Lock = new object();

        // SQL Prompt color palette
        private static readonly Color TableColor = Color.FromRgb(0x15, 0x65, 0xC0);       // Blue
        private static readonly Color ViewColor = Color.FromRgb(0x2E, 0x7D, 0x32);         // Green
        private static readonly Color ColumnColor = Color.FromRgb(0xF9, 0xA8, 0x25);       // Gold
        private static readonly Color KeywordColor = Color.FromRgb(0x54, 0x6E, 0x7A);      // Blue-Gray
        private static readonly Color SnippetColor = Color.FromRgb(0xE6, 0x51, 0x00);      // Orange
        private static readonly Color FunctionColor = Color.FromRgb(0xAD, 0x14, 0x57);     // Magenta
        private static readonly Color ProcedureColor = Color.FromRgb(0x6A, 0x1B, 0x9A);    // Purple
        private static readonly Color SchemaColor = Color.FromRgb(0x61, 0x61, 0x61);       // Gray
        private static readonly Color DatabaseColor = Color.FromRgb(0x00, 0x69, 0x5C);     // Teal
        private static readonly Color VariableColor = Color.FromRgb(0x00, 0x83, 0x8F);     // Cyan
        private static readonly Color AliasColor = Color.FromRgb(0x28, 0x35, 0x93);        // Indigo
        private static readonly Color ParameterColor = Color.FromRgb(0x4E, 0x34, 0x2E);    // Brown

        /// <summary>
        /// Gets a colored icon for the given CompletionObjectType.
        /// Icons are 16x16 filled squares with a 1px border and inner letter.
        /// </summary>
        public static ImageSource GetIcon(int objectType)
        {
            lock (Lock)
            {
                if (Cache.TryGetValue(objectType, out var cached))
                    return cached;

                var icon = CreateIcon(objectType);
                Cache[objectType] = icon;
                return icon;
            }
        }

        private static ImageSource CreateIcon(int objectType)
        {
            var color = GetColor(objectType);
            var letter = GetLetter(objectType);

            // Create a 16x16 icon with colored background and white letter
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var pen = new Pen(new SolidColorBrush(DarkenColor(color, 0.3)), 1);
                pen.Freeze();

                // Background rounded rectangle
                ctx.DrawRoundedRectangle(brush, pen,
                    new Rect(0.5, 0.5, 15, 15), 2, 2);

                // White letter centered
                var formattedText = new FormattedText(
                    letter,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10,
                    Brushes.White,
                    1.0);

                var x = (16 - formattedText.Width) / 2;
                var y = (16 - formattedText.Height) / 2;
                ctx.DrawText(formattedText, new Point(x, y));
            }

            var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static Color GetColor(int objectType)
        {
            switch (objectType)
            {
                case 0: return TableColor;
                case 1: return ViewColor;
                case 2: return ColumnColor;
                case 3: return KeywordColor;
                case 4: return SnippetColor;
                case 5: return FunctionColor;
                case 6: return ProcedureColor;
                case 7: return SchemaColor;
                case 8: return DatabaseColor;
                case 9: return VariableColor;
                case 10: return AliasColor;
                case 11: return ParameterColor;
                default: return SchemaColor;
            }
        }

        private static string GetLetter(int objectType)
        {
            switch (objectType)
            {
                case 0: return "T";  // Table
                case 1: return "V";  // View
                case 2: return "C";  // Column
                case 3: return "K";  // Keyword
                case 4: return "S";  // Snippet
                case 5: return "F";  // Function
                case 6: return "P";  // Procedure
                case 7: return "S";  // Schema
                case 8: return "D";  // Database
                case 9: return "@";  // Variable
                case 10: return "A"; // Alias
                case 11: return "P"; // Parameter
                default: return "?";
            }
        }

        private static Color DarkenColor(Color c, double factor)
        {
            return Color.FromRgb(
                (byte)(c.R * (1 - factor)),
                (byte)(c.G * (1 - factor)),
                (byte)(c.B * (1 - factor)));
        }
    }
}

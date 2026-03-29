using System;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Data model for a single completion item with SQL Prompt-style presentation.
    /// Maps Engine CompletionItem types to colors/letters matching Redgate SQL Prompt.
    /// </summary>
    internal sealed class CompletionItemModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public string InsertText { get; set; } = string.Empty;
        public string SecondaryText { get; set; } = string.Empty;
        public int ObjectType { get; set; }
        public int SortPriority { get; set; }

        // Computed presentation properties
        public string IconLetter => GetLetter(ObjectType);
        public Color IconColor => GetColor(ObjectType);
        public SolidColorBrush IconBrush => new SolidColorBrush(IconColor);

        /// <summary>
        /// Case-insensitive prefix + substring match for client-side filtering.
        /// Returns true if filter is empty or DisplayText contains the filter text.
        /// </summary>
        public bool MatchesFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            return DisplayText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scoring for sort order during filtering. Lower = better match.
        /// Prefix matches score higher than substring matches.
        /// </summary>
        public int FilterScore(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return SortPriority;

            if (DisplayText.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                return 0; // Prefix match — best

            if (DisplayText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return 100; // Substring match

            return int.MaxValue; // No match
        }

        // SQL Prompt color scheme
        private static Color GetColor(int objectType)
        {
            switch (objectType)
            {
                case 0: return Color.FromRgb(0x15, 0x65, 0xC0);   // Table — Blue
                case 1: return Color.FromRgb(0x2E, 0x7D, 0x32);   // View — Green
                case 2: return Color.FromRgb(0xF9, 0xA8, 0x25);   // Column — Gold
                case 3: return Color.FromRgb(0x54, 0x6E, 0x7A);   // Keyword — Blue-Gray
                case 4: return Color.FromRgb(0xE6, 0x51, 0x00);   // Snippet — Orange
                case 5: return Color.FromRgb(0xAD, 0x14, 0x57);   // Function — Magenta
                case 6: return Color.FromRgb(0x6A, 0x1B, 0x9A);   // Procedure — Purple
                case 7: return Color.FromRgb(0x61, 0x61, 0x61);   // Schema — Gray
                case 8: return Color.FromRgb(0x00, 0x69, 0x5C);   // Database — Teal
                case 9: return Color.FromRgb(0x00, 0x83, 0x8F);   // Variable — Cyan
                case 10: return Color.FromRgb(0x28, 0x35, 0x93);  // Alias — Indigo
                case 11: return Color.FromRgb(0x4E, 0x34, 0x2E);  // Parameter — Brown
                default: return Color.FromRgb(0x61, 0x61, 0x61);  // Unknown — Gray
            }
        }

        private static string GetLetter(int objectType)
        {
            switch (objectType)
            {
                case 0: return "T";   // Table
                case 1: return "V";   // View
                case 2: return "C";   // Column
                case 3: return "K";   // Keyword
                case 4: return "S";   // Snippet
                case 5: return "F";   // Function
                case 6: return "P";   // Procedure
                case 7: return "S";   // Schema
                case 8: return "D";   // Database
                case 9: return "@";   // Variable
                case 10: return "A";  // Alias
                case 11: return "P";  // Parameter
                default: return "?";
            }
        }
    }
}

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
        /// Matches via prefix, substring, or CamelCase initials.
        /// CamelCase: "PC" matches "ProductCategory", "sc" matches "sys_columns".
        /// </summary>
        public bool MatchesFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;

            // Prefix or substring match (case-insensitive)
            if (DisplayText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // CamelCase / underscore initial match: extract initials from DisplayText
            // and check if the filter matches them as a prefix
            return MatchesCamelCase(DisplayText, filter);
        }

        /// <summary>
        /// Scoring for sort order during filtering. Lower = better match.
        /// Prefix > CamelCase > Substring.
        /// </summary>
        public int FilterScore(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return SortPriority;

            if (DisplayText.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                return 0; // Prefix match — best

            if (MatchesCamelCase(DisplayText, filter))
                return 50; // CamelCase match — good

            if (DisplayText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return 100; // Substring match

            return int.MaxValue; // No match
        }

        /// <summary>
        /// CamelCase / underscore boundary matching.
        /// Extracts initials from word boundaries and checks if filter matches.
        /// Examples: "PC" → ProductCategory, "gco" → GetCustomerOrders, "sc" → sys_columns
        /// </summary>
        private static bool MatchesCamelCase(string text, string filter)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(filter))
                return false;

            // Extract initials: first char + each char after uppercase boundary or underscore
            var initials = new char[text.Length];
            int count = 0;
            initials[count++] = text[0];

            for (int i = 1; i < text.Length; i++)
            {
                var c = text[i];
                // Uppercase letter after lowercase = CamelCase boundary
                if (char.IsUpper(c) && i > 0 && char.IsLower(text[i - 1]))
                {
                    initials[count++] = c;
                }
                // Letter after underscore = boundary
                else if (char.IsLetterOrDigit(c) && text[i - 1] == '_')
                {
                    initials[count++] = c;
                }
            }

            if (count < filter.Length)
                return false;

            // Check if filter matches the initials as a prefix (case-insensitive)
            for (int i = 0; i < filter.Length; i++)
            {
                if (char.ToUpperInvariant(filter[i]) != char.ToUpperInvariant(initials[i]))
                    return false;
            }

            return true;
        }

        // SQL Prompt One Dark color scheme (from SQL_Prompt_Features_Core.md §1.2)
        private static Color GetColor(int objectType)
        {
            switch (objectType)
            {
                case 0: return Color.FromRgb(0xE5, 0xC0, 0x4B);   // Table — Yellow #E5C04B
                case 1: return Color.FromRgb(0x56, 0xB6, 0xC2);   // View — Teal #56B6C2
                case 2: return Color.FromRgb(0x61, 0xAF, 0xEF);   // Column — Blue #61AFEF
                case 3: return Color.FromRgb(0xAB, 0xB2, 0xBF);   // Keyword — Silver #ABB2BF
                case 4: return Color.FromRgb(0x3D, 0xD6, 0x8C);   // Snippet — Green #3DD68C
                case 5: return Color.FromRgb(0xD1, 0x9A, 0x66);   // Function — Orange #D19A66
                case 6: return Color.FromRgb(0xC6, 0x78, 0xDD);   // Procedure — Purple #C678DD
                case 7: return Color.FromRgb(0x98, 0xC3, 0x79);   // Schema — Green #98C379
                case 8: return Color.FromRgb(0xE0, 0x6C, 0x75);   // Database — Red #E06C75
                case 9: return Color.FromRgb(0x56, 0xB6, 0xC2);   // Variable — Teal #56B6C2
                case 10: return Color.FromRgb(0x61, 0xAF, 0xEF);  // Alias — Blue #61AFEF
                case 11: return Color.FromRgb(0xC6, 0x78, 0xDD);  // Parameter — Purple #C678DD
                default: return Color.FromRgb(0xAB, 0xB2, 0xBF);  // Unknown — Silver #ABB2BF
            }
        }

        /// <summary>Returns the badge background opacity (0.20 for most types, 0.15 for Keyword).</summary>
        public double IconBackgroundOpacity => ObjectType == 3 ? 0.15 : 0.20;

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

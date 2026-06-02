using System;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Data model for a single completion item with SQL Prompt-style presentation.
    /// Maps Engine CompletionItem types to colors/letters matching Redgate SQL Prompt.
    /// <para>
    /// Spec 020 US4 (T062): icon colours now flow through <see cref="ThemeTokens.IconBadgeTable"/>
    /// etc. via <see cref="ThemeRegistry"/>. Visually identical across Light / Dark (the IconBadge
    /// palette uses the same hex in both variants so object-type recognition stays consistent),
    /// but the lookup goes through the token system so SC-001 has zero hex-literal violations in
    /// this file and any future palette tuning lands in one place.
    /// </para>
    /// </summary>
    internal sealed class CompletionItemModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public string InsertText { get; set; } = string.Empty;
        public string SecondaryText { get; set; } = string.Empty;
        public int ObjectType { get; set; }
        public int SortPriority { get; set; }

        // Computed presentation properties — resolved fresh each access so theme switches
        // pick up the new palette automatically without rebuilding the model.
        public string IconLetter => GetLetter(ObjectType);
        public Color IconColor => GetIconBrush(ObjectType).Color;
        public SolidColorBrush IconBrush => GetIconBrush(ObjectType);

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

        // SQL Prompt One Dark color scheme (from SQL_Prompt_Features_Core.md §1.2). Spec 020 US4
        // (T062): each case resolves to a brush from ThemeRegistry so the SC-001 hex scanner sees
        // no literals here and runtime theme switches pick up the (currently theme-invariant)
        // IconBadge palette automatically.
        private static SolidColorBrush GetIconBrush(int objectType)
        {
            var key = objectType switch
            {
                0  => ThemeTokens.IconBadgeTable,        // T — Yellow
                1  => ThemeTokens.IconBadgeView,         // V — Teal
                2  => ThemeTokens.IconBadgeColumn,       // C — Blue
                3  => ThemeTokens.IconBadgeKeyword,      // K — Silver/Gray
                4  => ThemeTokens.IconBadgeSnippet,      // S — Green
                5  => ThemeTokens.IconBadgeFunction,     // F — Orange
                6  => ThemeTokens.IconBadgeStoredProc,   // P — Purple
                7  => ThemeTokens.IconBadgeSchema,       // Sc — Green2
                8  => ThemeTokens.IconBadgeDatabase,     // D — Red
                9  => ThemeTokens.IconBadgeView,         // Variable — shares Teal with View
                10 => ThemeTokens.IconBadgeColumn,       // Alias — shares Blue with Column
                11 => ThemeTokens.IconBadgeStoredProc,   // Parameter — shares Purple with StoredProc
                12 => ThemeTokens.IconBadgeFunction,     // SmartAction — shares Orange with Function
                _  => ThemeTokens.IconBadgeKeyword,      // Unknown — Silver/Gray
            };

            // The brushes are pre-frozen by ThemePalette (SC-001 / FR-004); no per-call allocation.
            // Defensive fallback if the registry isn't initialised (e.g. in a unit test): return a
            // single fixed Keyword/Gray brush so the popup still renders.
            return ThemeRegistry.Instance.Resources[key] as SolidColorBrush ?? _fallbackBrush;
        }

        private static readonly SolidColorBrush _fallbackBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xAB, 0xB2, 0xBF)));

        private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

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
                case 12: return "▶";  // SmartAction
                default: return "?";
            }
        }
    }
}

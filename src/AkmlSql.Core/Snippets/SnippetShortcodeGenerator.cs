using System.Text;

namespace AkmlSql.Core.Snippets
{
    /// <summary>
    /// Spec 030 T044 / FR-033 — derives an automatic snippet shortcode from a SQL selection. Pure and
    /// deterministic so it can be unit-tested without the shell. Used by "Create Snippet from Selection".
    /// </summary>
    public static class SnippetShortcodeGenerator
    {
        /// <summary>Hard cap on the generated shortcode length.</summary>
        public const int MaxLength = 16;

        /// <summary>Fallback returned when the selection contributes no alphanumeric initials.</summary>
        public const string Fallback = "snip";

        /// <summary>
        /// Builds a shortcode from the INITIALS of the selected SQL.
        ///
        /// Rule:
        ///   1. Split the selection on whitespace into tokens.
        ///   2. For each token, take its first letter-or-digit character (skipping leading punctuation
        ///      such as '(', '@', '['); a token with no alphanumeric character contributes nothing.
        ///   3. Lowercase each contributed character.
        ///   4. Collapse runs of the SAME consecutive character to one (so "SELECT sales" → "s", not "ss").
        ///   5. Cap the result at <see cref="MaxLength"/> characters.
        ///   6. If nothing was contributed, return <see cref="Fallback"/> ("snip").
        /// </summary>
        public static string FromSelection(string? selection)
        {
            if (string.IsNullOrWhiteSpace(selection)) return Fallback;

            var sb = new StringBuilder(MaxLength);
            char last = '\0';

            foreach (var token in selection!.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
            {
                char? initial = null;
                foreach (var ch in token)
                {
                    if (char.IsLetterOrDigit(ch)) { initial = char.ToLowerInvariant(ch); break; }
                }
                if (initial == null) continue;

                var c = initial.Value;
                if (c == last) continue; // collapse consecutive duplicates
                sb.Append(c);
                last = c;
                if (sb.Length >= MaxLength) break;
            }

            return sb.Length > 0 ? sb.ToString() : Fallback;
        }
    }
}

using System.Diagnostics;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Toggles square brackets on identifiers.
/// If the profile's addSquareBrackets is true, wraps unbracketed identifiers with [brackets].
/// If false, removes brackets from quoted identifiers.
/// </summary>
public class ToggleBracketsAction : IFormatAction
{
    public FormatResult Execute(string sql, FormattingProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<FormatDiagnostic>();
        bool addBrackets = profile.FormatActions.AddSquareBrackets;

        try
        {
            // Scan noformat regions
            var noformatScanner = new NoformatScanner();
            var noformatRegions = noformatScanner.Scan(sql);

            // Parse
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            var script = parser.Parse(reader, out var errors) as TSqlScript;
            var tokens = script?.ScriptTokenStream;

            if (script == null || tokens == null)
            {
                return new FormatResult
                {
                    Success = false,
                    FormattedText = sql,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = "Failed to parse SQL" }]
                };
            }

            // Collect replacements in reverse order
            var replacements = new List<(int Offset, int Length, string NewText)>();

            foreach (var t in tokens)
            {
                if (t.TokenType == TSqlTokenType.EndOfFile) break;

                if (NoformatScanner.IsInNoformatRegion(noformatRegions, t.Offset))
                    continue;

                if (addBrackets)
                {
                    // Add brackets to plain identifiers
                    if (t.TokenType == TSqlTokenType.Identifier)
                    {
                        replacements.Add((t.Offset, t.Text.Length, $"[{t.Text}]"));
                    }
                }
                else
                {
                    // Remove brackets from quoted identifiers
                    if (t.TokenType == TSqlTokenType.QuotedIdentifier &&
                        t.Text.StartsWith("[") && t.Text.EndsWith("]"))
                    {
                        var inner = t.Text[1..^1];
                        // Only remove brackets if the inner text is a simple identifier
                        if (IsSimpleIdentifier(inner))
                        {
                            replacements.Add((t.Offset, t.Text.Length, inner));
                        }
                    }
                }
            }

            // Apply replacements using forward-scan segment merge (O(n))
            replacements.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            var sb = new StringBuilder(sql.Length + replacements.Count * 2);
            int cursor = 0;
            foreach (var (offset, length, newText) in replacements)
            {
                // Append text before this replacement
                if (offset > cursor)
                    sb.Append(sql, cursor, offset - cursor);
                sb.Append(newText);
                cursor = offset + length;
            }
            // Append remaining text after last replacement
            if (cursor < sql.Length)
                sb.Append(sql, cursor, sql.Length - cursor);

            var formatted = sb.ToString();
            sw.Stop();

            return new FormatResult
            {
                Success = true,
                FormattedText = formatted,
                WasModified = formatted != sql,
                ValidationPassed = true,
                ElapsedMs = sw.ElapsedMilliseconds,
                Diagnostics = diagnostics.ToArray()
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new FormatResult
            {
                Success = false,
                FormattedText = sql,
                ElapsedMs = sw.ElapsedMilliseconds,
                Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = ex.Message }]
            };
        }
    }

    /// <summary>
    /// Returns true if the text is a valid simple SQL identifier (no special chars needing brackets).
    /// </summary>
    private static bool IsSimpleIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Must start with letter or underscore
        if (!char.IsLetter(text[0]) && text[0] != '_') return false;

        for (int i = 1; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }
}

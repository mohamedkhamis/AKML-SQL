using System.Diagnostics;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Removes semicolons from statement endings.
/// Does not change layout or casing.
/// </summary>
public class RemoveSemicolonsAction : IFormatAction
{
    public FormatResult Execute(string sql, FormattingProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<FormatDiagnostic>();

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

            // Find semicolon tokens to remove (reverse order for safe removal)
            var removals = new List<(int Offset, int Length)>();

            foreach (var t in tokens)
            {
                if (t.TokenType == TSqlTokenType.Semicolon)
                {
                    if (NoformatScanner.IsInNoformatRegion(noformatRegions, t.Offset))
                        continue;

                    removals.Add((t.Offset, t.Text.Length));
                }
            }

            // Sort descending by offset
            removals.Sort((a, b) => b.Offset.CompareTo(a.Offset));

            var sb = new StringBuilder(sql);
            foreach (var (offset, length) in removals)
            {
                sb.Remove(offset, length);
            }

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
}

using System.Diagnostics;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Applies casing rules to SQL without changing layout or whitespace.
/// Parses the SQL, applies casing to each token, and reconstructs the text
/// preserving original whitespace and line breaks.
/// </summary>
public class CasingOnlyAction : IFormatAction
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

            // Build layout nodes preserving original positions
            var nodes = new List<LayoutNode>();
            foreach (var t in tokens)
            {
                if (t.TokenType == TSqlTokenType.EndOfFile) break;

                nodes.Add(new LayoutNode
                {
                    TokenIndex = t.Offset,
                    TokenType = t.TokenType,
                    OriginalText = t.Text,
                    FormattedText = t.Text,
                    IsInNoformatRegion = NoformatScanner.IsInNoformatRegion(noformatRegions, t.Offset)
                });
            }

            // Apply casing only
            var casingEngine = new CasingEngine();
            casingEngine.ApplyCasing(nodes, profile);

            // Reconstruct text preserving original whitespace
            var sb = new System.Text.StringBuilder(sql.Length);
            foreach (var node in nodes)
            {
                if (node.TokenType == TSqlTokenType.WhiteSpace)
                {
                    sb.Append(node.OriginalText);
                }
                else
                {
                    sb.Append(node.FormattedText);
                }
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

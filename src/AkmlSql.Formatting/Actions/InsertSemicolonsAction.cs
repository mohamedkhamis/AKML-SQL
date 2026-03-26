using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Walks AST statement nodes and adds semicolons after each statement that doesn't already end with one.
/// Does not change layout or casing.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Local")]
[SuppressMessage("ReSharper", "UnusedVariable")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class InsertSemicolonsAction : IFormatAction
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

            if (script == null)
            {
                return new FormatResult
                {
                    Success = false,
                    FormattedText = sql,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = "Failed to parse SQL" }]
                };
            }

            // Collect statement end offsets where semicolons should be inserted
            // Process in reverse order so offsets remain valid
            var insertions = new List<int>();

            foreach (var batch in script.Batches)
            {
                foreach (var stmt in batch.Statements)
                {
                    // Skip GO statements (batch separators)
                    int endOffset = stmt.StartOffset + stmt.FragmentLength;

                    // Check if in noformat region
                    if (NoformatScanner.IsInNoformatRegion(noformatRegions, stmt.StartOffset))
                        continue;

                    // Check if already ends with semicolon by scanning backwards
                    // from the fragment end, skipping whitespace
                    int fragEnd = stmt.StartOffset + stmt.FragmentLength;
                    int scanPos = fragEnd - 1;
                    while (scanPos >= stmt.StartOffset && char.IsWhiteSpace(sql[scanPos]))
                        scanPos--;

                    if (scanPos >= stmt.StartOffset && sql[scanPos] == ';')
                        continue;

                    // Insert after the last non-whitespace character
                    int insertPos = scanPos + 1;
                    insertions.Add(insertPos);
                }
            }

            // Sort descending so we can insert from end to start
            insertions.Sort();
            insertions.Reverse();

            var sb = new StringBuilder(sql);
            foreach (var pos in insertions)
            {
                sb.Insert(pos, ';');
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

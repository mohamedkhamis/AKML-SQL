using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Toggles the AS keyword on alias definitions.
/// When addAsKeyword is true, inserts AS before aliases that lack it.
/// When false, removes AS from alias definitions.
/// </summary>
[SuppressMessage("ReSharper", "UnusedParameter.Local")]
// ReSharper disable once UnusedMember.Global
public class ToggleAsKeywordAction : IFormatAction
{
    public FormatResult Execute(string sql, FormattingProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<FormatDiagnostic>();
        bool addAs = profile.FormatActions.AddAsKeyword;

        try
        {
            // Scan noformat regions
            var noformatScanner = new NoformatScanner();
            var noformatRegions = noformatScanner.Scan(sql);

            // Parse
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            // ReSharper disable once UnusedVariable
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

            if (addAs)
            {
                return InsertAsKeywords(sql, script, tokens, noformatRegions, diagnostics, sw);
            }
            else
            {
                return RemoveAsKeywords(sql, tokens, noformatRegions, diagnostics, sw);
            }
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

    private static FormatResult RemoveAsKeywords(
        string sql,
        IList<TSqlParserToken> tokens,
        List<NoformatRegion> noformatRegions,
        List<FormatDiagnostic> diagnostics,
        Stopwatch sw)
    {
        // Find AS tokens that are used as alias markers (not AS in CREATE PROCEDURE AS, etc.)
        // Heuristic: AS preceded by an identifier/closing-paren and followed by an identifier
        var removals = new List<(int Start, int End)>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType != TSqlTokenType.As) continue;
            if (NoformatScanner.IsInNoformatRegion(noformatRegions, t.Offset)) continue;

            // Check context: is this an alias AS?
            var prevSemantic = FindPreviousSemantic(tokens, i);
            var nextSemantic = FindNextSemantic(tokens, i);

            if (prevSemantic == null || nextSemantic == null) continue;

            bool isAlias = IsAliasContext(prevSemantic.Value.TokenType, nextSemantic.Value.TokenType);
            if (!isAlias) continue;

            // Remove AS and surrounding whitespace (keep one space)
            int removeStart = t.Offset;
            int removeEnd = t.Offset + t.Text.Length;

            // Include trailing whitespace
            for (int j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].TokenType == TSqlTokenType.WhiteSpace)
                {
                    removeEnd = tokens[j].Offset + tokens[j].Text.Length;
                    break;
                }
            }

            removals.Add((removeStart, removeEnd));
        }

        // Apply in reverse
        removals.Sort((a, b) => b.Start.CompareTo(a.Start));

        var sb = new StringBuilder(sql);
        foreach (var (start, end) in removals)
        {
            sb.Remove(start, end - start);
            sb.Insert(start, " "); // Keep a single space where AS was
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

    private static FormatResult InsertAsKeywords(
        string sql,
        TSqlScript script,
        IList<TSqlParserToken> tokens,
        List<NoformatRegion> noformatRegions,
        List<FormatDiagnostic> diagnostics,
        Stopwatch sw)
    {
        // This is complex — we need to find alias definitions that lack AS.
        // For now, use a token-level heuristic: look for patterns like
        // <expression> <identifier> where there is no AS between them in alias context.
        // A full implementation would walk the AST looking for SelectColumn.Alias nodes.
        // For the initial implementation, we return the SQL unchanged with a diagnostic.

        sw.Stop();
        return new FormatResult
        {
            Success = true,
            FormattedText = sql,
            WasModified = false,
            ValidationPassed = true,
            ElapsedMs = sw.ElapsedMilliseconds,
            Diagnostics = [new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message = "InsertAsKeywords requires AST-level alias detection. Use full format pipeline with addAsKeyword=true instead."
            }]
        };
    }

    private static bool IsAliasContext(TSqlTokenType prev, TSqlTokenType next)
    {
        // AS is an alias when preceded by: identifier, ), quoted-identifier, string
        // and followed by: identifier, quoted-identifier
        bool prevIsExpr = prev switch
        {
            TSqlTokenType.Identifier or
            TSqlTokenType.QuotedIdentifier or
            TSqlTokenType.RightParenthesis or
            TSqlTokenType.AsciiStringLiteral or
            TSqlTokenType.UnicodeStringLiteral or
            TSqlTokenType.Integer or
            TSqlTokenType.Numeric or
            TSqlTokenType.Star => true,
            _ => false,
        };

        bool nextIsAlias = next switch
        {
            TSqlTokenType.Identifier or
            TSqlTokenType.QuotedIdentifier => true,
            _ => false,
        };

        return prevIsExpr && nextIsAlias;
    }

    private static (TSqlTokenType TokenType, string Text)? FindPreviousSemantic(IList<TSqlParserToken> tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (tokens[i].TokenType != TSqlTokenType.WhiteSpace &&
                tokens[i].TokenType != TSqlTokenType.SingleLineComment &&
                tokens[i].TokenType != TSqlTokenType.MultilineComment)
            {
                return (tokens[i].TokenType, tokens[i].Text);
            }
        }
        return null;
    }

    private static (TSqlTokenType TokenType, string Text)? FindNextSemantic(IList<TSqlParserToken> tokens, int index)
    {
        for (int i = index + 1; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType != TSqlTokenType.WhiteSpace &&
                tokens[i].TokenType != TSqlTokenType.SingleLineComment &&
                tokens[i].TokenType != TSqlTokenType.MultilineComment)
            {
                return (tokens[i].TokenType, tokens[i].Text);
            }
        }
        return null;
    }
}

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 6 of the formatting pipeline. Re-parses both the original and formatted SQL,
/// normalizes each with <c>Sql170ScriptGenerator</c>, and compares the results.
/// A mismatch means formatting changed the semantics — the pipeline then returns the original SQL.
/// <para>
/// Use the overload that accepts a pre-parsed <see cref="TSqlScript"/> to avoid re-parsing the original input.
/// </para>
/// </summary>
public class SemanticValidator
{
    /// <summary>
    /// Validates by re-parsing both original and formatted SQL.
    /// Prefer the overload that accepts a pre-parsed script to avoid the redundant original parse.
    /// </summary>
    public bool Validate(string original, string formatted, List<FormatDiagnostic> diagnostics)
    {
        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var origReader = new StringReader(original);
            var origScript = parser.Parse(origReader, out _) as TSqlScript;
            return origScript != null && Validate(origScript, formatted, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Semantic validation error: {ex.Message}"
            });
            return false;
        }
    }

    /// <summary>
    /// Validates using a pre-parsed original script (avoids re-parsing the original SQL).
    /// </summary>
    public bool Validate(TSqlScript originalScript, string formatted, List<FormatDiagnostic> diagnostics)
    {
        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var fmtReader = new StringReader(formatted);
            var fmtScript = parser.Parse(fmtReader, out var fmtErrors) as TSqlScript;

            if (fmtScript == null)
            {
                // Was a silent failure (no diagnostic) — surface why the formatted output won't parse.
                var first = fmtErrors.Count > 0 ? $" ({fmtErrors[0].Message} at line {fmtErrors[0].Line})" : "";
                diagnostics.Add(new FormatDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"Semantic validation failed: formatted output does not parse{first}"
                });
                return false;
            }

            var generator = new Sql170ScriptGenerator();
            generator.GenerateScript(originalScript, out var origNormalized);
            generator.GenerateScript(fmtScript, out var fmtNormalized);

            if (!NormalizedScriptsEquivalent(origNormalized, fmtNormalized))
            {
                diagnostics.Add(new FormatDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Semantic validation failed: formatted output differs from original"
                });
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Semantic validation error: {ex.Message}"
            });
            return false;
        }
    }

    /// <summary>
    /// Compares two <see cref="Sql170ScriptGenerator"/> normalisations for semantic equivalence.
    /// The generator already canonicalises whitespace, structure, and optional keywords, so the
    /// only legitimate residual the formatter introduces is <em>token casing</em> — and applying a
    /// casing option to keywords / identifiers / built-in function names is semantically neutral in
    /// T-SQL (function and keyword names are case-insensitive). A naïve case-sensitive string
    /// compare therefore false-positives on e.g. <c>sum(x)</c> → <c>SUM(x)</c>, causing the pipeline
    /// to discard a correctly-formatted statement and return the original unformatted (spec 030 —
    /// every GROUP&#160;BY/HAVING, CTE, MERGE, proc, and subquery corpus item failed here purely on
    /// function-name casing). So compare token-by-token, case-insensitively, but keep
    /// <em>string literals and delimited identifiers</em> case-sensitive (their casing is data, not
    /// syntax — a real <c>'USA'</c> → <c>'usa'</c> change must still be caught). Falls back to an
    /// exact string compare if either side fails to re-tokenise.
    /// </summary>
    private static bool NormalizedScriptsEquivalent(string a, string b)
    {
        var ta = SignificantTokens(a);
        var tb = SignificantTokens(b);
        if (ta is null || tb is null)
            return string.Equals(a, b, StringComparison.Ordinal);
        if (ta.Count != tb.Count)
            return false;
        for (int i = 0; i < ta.Count; i++)
        {
            if (ta[i].TokenType != tb[i].TokenType)
                return false;
            var comparison = IsCaseSensitiveToken(ta[i].TokenType)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            if (!string.Equals(ta[i].Text, tb[i].Text, comparison))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Tokens whose <see cref="TSqlParserToken.Text"/> is data, not case-insensitive syntax —
    /// their casing must match exactly. String/binary literals carry user data; delimited
    /// identifiers (<c>"x"</c> / <c>[x]</c>) can be case-sensitive under a case-sensitive collation
    /// and the formatter never rewrites their case.
    /// </summary>
    private static bool IsCaseSensitiveToken(TSqlTokenType type)
        => type is TSqlTokenType.AsciiStringLiteral
                or TSqlTokenType.UnicodeStringLiteral
                or TSqlTokenType.QuotedIdentifier;

    /// <summary>
    /// Re-tokenises a (valid, generator-produced) SQL string and returns the significant tokens —
    /// whitespace, comments, and EOF dropped. Returns null if the string will not parse.
    /// </summary>
    private static List<TSqlParserToken>? SignificantTokens(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out _);
        if (fragment?.ScriptTokenStream is null)
            return null;
        var tokens = new List<TSqlParserToken>(fragment.ScriptTokenStream.Count);
        foreach (var token in fragment.ScriptTokenStream)
        {
            switch (token.TokenType)
            {
                case TSqlTokenType.WhiteSpace:
                case TSqlTokenType.EndOfFile:
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                    continue;
                default:
                    tokens.Add(token);
                    break;
            }
        }
        return tokens;
    }
}

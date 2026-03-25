using System.Text.RegularExpressions;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Ai.Privacy;

/// <summary>
/// AST-based literal redaction for SQL sent to AI providers.
/// <para>
/// Parses the SQL using <see cref="TsqlParserService"/> and replaces literal values
/// (strings, integers, numerics, money) with deterministic placeholders. A mapping from
/// placeholder to original value is returned so the caller can reverse the transformation.
/// </para>
/// <para>
/// Falls back to conservative regex replacement when the SQL cannot be parsed (syntax errors).
/// </para>
/// </summary>
public sealed class LiteralRedactor
{
    private readonly TsqlParserService _parser;

    /// <summary>Initializes a new <see cref="LiteralRedactor"/> with the shared parser service.</summary>
    /// <param name="parser">The T-SQL parser service used to obtain the AST.</param>
    public LiteralRedactor(TsqlParserService parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Redacts literal values in <paramref name="sql"/> by replacing them with placeholders.
    /// </summary>
    /// <param name="sql">The raw SQL text to redact.</param>
    /// <returns>
    /// A tuple of the redacted SQL and a dictionary mapping each placeholder to its original value.
    /// </returns>
    public (string redactedSql, Dictionary<string, string> literalMap) Redact(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return (sql, new Dictionary<string, string>());

        var script = _parser.Parse(sql, out var errors);

        // If parsing succeeded with at least one batch, use AST-based redaction
        if (script != null && script.Batches.Count > 0 && errors.Count == 0)
        {
            return RedactViaAst(sql, script);
        }

        // Fallback: regex-based redaction for unparseable SQL
        Log.Debug("LiteralRedactor: AST parse failed with {ErrorCount} errors, falling back to regex", errors.Count);
        return RedactViaRegex(sql);
    }

    /// <summary>
    /// Walks the parsed AST, collects all literal nodes, and replaces them in the original text.
    /// Replacements are applied in reverse offset order to preserve character positions.
    /// </summary>
    private static (string redactedSql, Dictionary<string, string> literalMap) RedactViaAst(
        string sql, TSqlScript script)
    {
        var visitor = new LiteralCollector();
        script.Accept(visitor);

        if (visitor.Literals.Count == 0)
            return (sql, new Dictionary<string, string>());

        // Sort by descending start offset so replacements don't shift earlier positions
        visitor.Literals.Sort((a, b) => b.StartOffset.CompareTo(a.StartOffset));

        var literalMap = new Dictionary<string, string>();
        var result = sql.AsSpan().ToString(); // mutable copy

        foreach (var literal in visitor.Literals)
        {
            var (placeholder, originalValue) = literal.Type switch
            {
                LiteralType.String => ($"'__STR_{literal.Index}__'", literal.Value),
                LiteralType.Integer => ($"__INT_{literal.Index}__", literal.Value),
                LiteralType.Numeric => ($"__NUM_{literal.Index}__", literal.Value),
                LiteralType.Money => ($"__MONEY_{literal.Index}__", literal.Value),
                _ => (literal.Value, literal.Value) // should not happen
            };

            // Only add if it's a real redaction
            if (placeholder != originalValue)
            {
                literalMap[placeholder.Trim('\'')] = originalValue;
                result = string.Concat(
                    result.AsSpan(0, literal.StartOffset),
                    placeholder,
                    result.AsSpan(literal.StartOffset + literal.Length));
            }
        }

        return (result, literalMap);
    }

    /// <summary>
    /// Conservative regex-based fallback for SQL that cannot be parsed.
    /// Replaces string literals and standalone numeric values.
    /// </summary>
    private static (string redactedSql, Dictionary<string, string> literalMap) RedactViaRegex(string sql)
    {
        var literalMap = new Dictionary<string, string>();
        int strCounter = 0;
        int numCounter = 0;

        // Replace string literals: 'anything' (handles escaped quotes via non-greedy)
        var result = StringLiteralPattern().Replace(sql, match =>
        {
            var placeholder = $"__STR_{strCounter}__";
            literalMap[placeholder] = match.Value.Trim('\'');
            strCounter++;
            return $"'__STR_{strCounter - 1}__'";
        });

        // Replace standalone numeric literals (not part of identifiers)
        result = NumericLiteralPattern().Replace(result, match =>
        {
            // Skip if preceded by an underscore or letter (part of identifier)
            var placeholder = $"__NUM_{numCounter}__";
            literalMap[placeholder] = match.Value;
            numCounter++;
            return $"__NUM_{numCounter - 1}__";
        });

        return (result, literalMap);
    }

    /// <summary>Matches SQL string literals including escaped single quotes.</summary>
    private static Regex StringLiteralPattern() => new(@"'(?:[^']|'')*'", RegexOptions.Compiled);

    /// <summary>Matches standalone numeric literals not part of identifiers.</summary>
    private static Regex NumericLiteralPattern() => new(@"(?<![_a-zA-Z])\b\d+(\.\d+)?\b(?![_a-zA-Z])", RegexOptions.Compiled);

    /// <summary>Type of literal found during AST walking.</summary>
    private enum LiteralType
    {
        String,
        Integer,
        Numeric,
        Money
    }

    /// <summary>A collected literal with its position, type, and assigned index.</summary>
    private sealed record LiteralInfo(
        int StartOffset,
        int Length,
        string Value,
        LiteralType Type,
        int Index);

    /// <summary>
    /// TSqlFragmentVisitor that collects all non-NULL literal nodes from the AST.
    /// </summary>
    private sealed class LiteralCollector : TSqlFragmentVisitor
    {
        public List<LiteralInfo> Literals { get; } = [];
        private int _strCounter;
        private int _intCounter;
        private int _numCounter;
        private int _moneyCounter;

        public override void Visit(StringLiteral node)
        {
            // The full fragment includes the surrounding quotes
            Literals.Add(new LiteralInfo(
                node.StartOffset,
                node.FragmentLength,
                node.Value,
                LiteralType.String,
                _strCounter++));
        }

        public override void Visit(IntegerLiteral node)
        {
            Literals.Add(new LiteralInfo(
                node.StartOffset,
                node.FragmentLength,
                node.Value,
                LiteralType.Integer,
                _intCounter++));
        }

        public override void Visit(NumericLiteral node)
        {
            Literals.Add(new LiteralInfo(
                node.StartOffset,
                node.FragmentLength,
                node.Value,
                LiteralType.Numeric,
                _numCounter++));
        }

        public override void Visit(MoneyLiteral node)
        {
            Literals.Add(new LiteralInfo(
                node.StartOffset,
                node.FragmentLength,
                node.Value,
                LiteralType.Money,
                _moneyCounter++));
        }

        // NullLiteral is intentionally NOT visited — NULL is preserved as-is
    }
}

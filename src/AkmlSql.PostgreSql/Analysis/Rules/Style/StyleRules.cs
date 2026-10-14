using AkmlSql.Core.Models.Analysis;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Language;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Style;

// Spec 042 (task T166, FR-150, FR-153): the style rules PGST001 to PGST005. All are hints: they never change what a statement means.

/// <summary>PGST001 - key words written in more than one style.</summary>
public sealed class Pgst001KeywordCasing : PgRuleBase
{
    public Pgst001KeywordCasing() : base("PGST001", "Style", "Inconsistent keyword case", DiagnosticSeverity.Hint,
        "PostgreSQL reads key words in any case, so SELECT and select are the same word; a script that mixes styles is harder to read. "
        + "The Format SQL command applies the active style's casing to the whole script.",
        "sql-syntax-lexical") { }

    private enum Style { Upper, Lower, Mixed }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var words = new List<(int Start, int End, Style Style)>();
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            foreach (var (start, end, kind) in s.Keywords())
            {
                // Reserved words and the words that cannot be column names are always key words; the rest are often used as names.
                if (kind is not (KeywordKind.ReservedKeyword or KeywordKind.TypeFuncNameKeyword)) continue;
                words.Add((start, end, StyleOf(ctx.Text, start, end)));
            }
        }

        var upper = words.Count(w => w.Style == Style.Upper);
        var lower = words.Count(w => w.Style == Style.Lower);
        var mixed = words.Count(w => w.Style == Style.Mixed);
        if ((upper > 0 ? 1 : 0) + (lower > 0 ? 1 : 0) + (mixed > 0 ? 1 : 0) < 2) return;

        var dominant = upper >= lower && upper >= mixed ? Style.Upper : lower >= mixed ? Style.Lower : Style.Mixed;
        var first = words.First(w => w.Style != dominant);
        report.At(first.Start, first.End,
            $"Key words are written in more than one style ({upper} upper case, {lower} lower case, {mixed} other); most are {dominant.ToString().ToLowerInvariant()} case.");
    }

    private static Style StyleOf(string text, int start, int end)
    {
        var anyUpper = false;
        var anyLower = false;
        for (var i = start; i < end; i++)
        {
            if (char.IsUpper(text[i])) anyUpper = true;
            else if (char.IsLower(text[i])) anyLower = true;
        }
        return anyUpper && !anyLower ? Style.Upper : anyLower && !anyUpper ? Style.Lower : Style.Mixed;
    }
}

/// <summary>PGST002 - the last statement has no semicolon.</summary>
public sealed class Pgst002MissingTerminator : PgRuleBase
{
    public Pgst002MissingTerminator() : base("PGST002", "Style", "Missing statement terminator", DiagnosticSeverity.Hint,
        "A semicolon ends a statement. The last statement of a script may omit it, but adding another statement after it then merges the two, and psql and most tools "
        + "need it to know the statement is complete.",
        "sql-syntax", autoFixable: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var last = ctx.Statements.LastOrDefault(s => s.Raw != null && !s.IsMetaCommand);
        if (last == null || last.Tokens.Count == 0) return;

        // Is the next thing after the statement (skipping white space and comments) a semicolon?
        foreach (var token in ctx.LexTokens)
        {
            if (token.Start < last.End) continue;
            if (token.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            if (token.Kind == PgTokenKind.Semicolon) return;
            break;
        }

        var tail = last.Tokens[last.Tokens.Count - 1].Token;
        report.At(tail.Start, tail.End, "This statement is not ended with a semicolon.", PgReporter.Replace("Add the semicolon", last.End, last.End, ";"));
    }
}

/// <summary>PGST003 - != where the standard writes &lt;&gt;.</summary>
public sealed class Pgst003NotEqualsOperator : PgRuleBase
{
    public Pgst003NotEqualsOperator() : base("PGST003", "Style", "!= instead of <>", DiagnosticSeverity.Hint,
        "PostgreSQL accepts != but converts it to <>, the SQL standard operator, while parsing; both mean the same. <> is what the server prints in plans, "
        + "logs and view definitions.",
        "functions-comparison", autoFixable: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var token in s.Tokens.Sig)
            {
                if (token.Kind == PgTokenKind.Operator && token.Text == "!=")
                    report.Token(token, "Use <> for \"not equal\".", PgReporter.Replace("Replace with <>", token.Token.Start, token.Token.End, "<>"));
            }
        }
    }
}

/// <summary>PGST004 - a column alias written without AS.</summary>
public sealed class Pgst004AliasWithoutAs : PgRuleBase
{
    private static readonly HashSet<string> EndsItem = new(StringComparer.Ordinal)
    {
        "FROM", "INTO", "WHERE", "GROUP", "HAVING", "WINDOW", "ORDER", "LIMIT", "OFFSET", "FETCH", "FOR", "UNION", "INTERSECT", "EXCEPT", "RETURNING", "ON",
    };

    public Pgst004AliasWithoutAs() : base("PGST004", "Style", "Column alias without AS", DiagnosticSeverity.Hint,
        "AS is optional before a column alias, but leaving it out turns a forgotten comma into an alias: SELECT a b FROM t returns one column named b. "
        + "Writing AS makes the alias explicit.",
        "queries-select-lists", autoFixable: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            var items = s.All<SelectStmt>().SelectMany(sel => sel.TargetList)
                .Concat(s.All<ReturningClause>().SelectMany(r => r.Exprs))
                .Select(n => n.ResTarget)
                .Where(t => t != null && !string.IsNullOrEmpty(t.Name) && t.Val != null);

            foreach (var item in items)
            {
                var alias = AliasToken(s, item!);
                if (alias is not { } found) continue;

                var sig = s.Tokens.Sig;
                if (found.Index > 0 && sig[found.Index - 1].IsWordEqual("AS")) continue;
                report.Token(sig[found.Index], $"Write AS before the alias {item!.Name}.", PgReporter.Replace("Add AS", sig[found.Index].Token.Start, sig[found.Index].Token.Start, "AS "));
            }
        }
    }

    // The last token of the select-list item that starts at the ResTarget's location, if it spells the alias.
    private static (int Index, PgSig Token)? AliasToken(PgScriptStatement s, ResTarget item)
    {
        var at = s.LocationOf(item);
        var first = at < 0 ? -1 : s.TokenIndexAt(at);
        if (first < 0) return null;

        var sig = s.Tokens.Sig;
        var depth = sig[first].Depth;
        var last = first;
        for (var j = first; j < sig.Count; j++)
        {
            var t = sig[j];
            if (t.Depth < depth) break;
            if (t.Depth == depth)
            {
                if (t.IsComma || t.Kind == PgTokenKind.Semicolon) break;
                if (j > first && t.IsWord && EndsItem.Contains(t.Upper) && !(t.Upper == "FROM" && sig[j - 1].IsWordEqual("DISTINCT"))) break;
                last = j;
            }
        }

        var candidate = sig[last];
        if (last == first || !candidate.IsName) return null;
        var spelled = candidate.Name.Text;
        return string.Equals(spelled, item.Name, StringComparison.OrdinalIgnoreCase) ? (last, candidate) : null;
    }
}

/// <summary>PGST005 - a very long line.</summary>
public sealed class Pgst005LongLine : PgRuleBase
{
    public const int MaxLength = 200;

    public Pgst005LongLine() : base("PGST005", "Style", "Line longer than 200 characters", DiagnosticSeverity.Hint,
        "Lines of more than 200 characters are hard to read and to review, and a diff shows the whole line as changed. Break the statement over several lines "
        + "(the Format SQL command does).",
        ProductDocs) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var text = ctx.Text;
        var lineStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n' && text[i] != '\r') continue;

            if (i - lineStart > MaxLength)
                report.At(lineStart + MaxLength, i, $"This line is {i - lineStart} characters long; the limit is {MaxLength}.");

            if (i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n') i++;
            lineStart = i + 1;
        }
    }
}

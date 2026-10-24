using System.Text;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Language;
using AkmlSql.PostgreSql.Reference;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Naming;

// Spec 042 (task T170, FR-150, FR-153): the naming rules PGNM001 to PGNM005. They read the script's names as the lexical scanner
// sees them: an unquoted name is folded to lower case, a quoted one is kept exactly, and either is cut at 63 bytes.

/// <summary>PGNM001 - an identifier PostgreSQL will cut short.</summary>
public sealed class Pgnm001IdentifierTooLong : PgRuleBase
{
    public const int MaxBytes = 63;

    public Pgnm001IdentifierTooLong() : base("PGNM001", "Naming", "Identifier longer than 63 bytes", DiagnosticSeverity.Warning,
        "PostgreSQL stores a name in 63 bytes (NAMEDATALEN - 1) and silently truncates a longer one, with only a NOTICE. Two long names that differ after the 63rd byte "
        + "become the same name, and the stored name no longer matches what was written. The limit counts UTF-8 bytes, not characters.",
        "sql-syntax-lexical#sql-syntax-identifiers") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var token in s.Tokens.Sig)
            {
                if (!token.IsName) continue;
                var bytes = Encoding.UTF8.GetByteCount(token.Name.Text);
                if (bytes > MaxBytes)
                    report.Token(token, $"This name is {bytes} bytes long; PostgreSQL keeps only the first {MaxBytes}.");
            }
        }
    }
}

/// <summary>PGNM002 - an unquoted name written with capitals.</summary>
public sealed class Pgnm002FoldedCase : PgRuleBase
{
    public Pgnm002FoldedCase() : base("PGNM002", "Naming", "Capitals in an unquoted name", DiagnosticSeverity.Information,
        "PostgreSQL folds an unquoted name to lower case: Orders, ORDERS and orders are the same table. A table created as \"Orders\" (quoted) is a different table that "
        + "unquoted references can never reach. Write names in lower case, or quote them every time. Function names and types are not reported.",
        "sql-syntax-lexical#sql-syntax-identifiers") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            foreach (var (start, end) in s.NameTokens())
            {
                var text = ctx.Text.Substring(start, end - start);
                if (text.All(c => !char.IsUpper(c)) || !reported.Add(text)) continue;
                if (IsFunctionOrType(s, start, text)) continue;

                report.At(start, end, $"{text} is folded to {text.ToLowerInvariant()}; write it in lower case or quote it.");
            }
        }
    }

    private static bool IsFunctionOrType(PgScriptStatement s, int start, string text)
    {
        var i = s.TokenIndexAt(start);
        var sig = s.Tokens.Sig;
        if (i >= 0)
        {
            if (i + 1 < sig.Count && sig[i + 1].IsOpen) return true;                 // name(  -  a call
            if (i > 0 && sig[i - 1].IsCast) return true;                              // ::type
        }
        var lower = text.ToLowerInvariant();
        return PgReference.Instance.Type(lower) != null;
    }
}

/// <summary>PGNM003 - a reserved word used as a name, which then has to be quoted for ever.</summary>
public sealed class Pgnm003ReservedWordName : PgRuleBase
{
    public Pgnm003ReservedWordName() : base("PGNM003", "Naming", "Reserved word used as a name", DiagnosticSeverity.Warning,
        "A reserved word (user, order, group, table, select ...) can only be used as a name when it is written in double quotes, and then every statement that mentions "
        + "it, in every client and tool, must quote it too. A different name avoids the problem.",
        "sql-keywords-appendix") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var s in ctx.Statements)
        {
            foreach (var token in s.Tokens.Sig)
            {
                if (!token.IsQuoted) continue;
                var inner = token.Name.Text;
                if (inner.Length == 0 || inner != inner.ToLowerInvariant()) continue;      // "User" is not the word USER

                if (!cache.TryGetValue(inner, out var reserved))
                    cache[inner] = reserved = IsReserved(inner);
                if (reserved) report.Token(token, $"\"{inner}\" is a reserved word; it has to be quoted wherever it is used.");
            }
        }
    }

    private static bool IsReserved(string word)
    {
        var tokens = PgParser.Tokenize(word);
        return tokens.Count == 1 && tokens[0].Kind == SqlTokenKind.Keyword
            && tokens[0].KeywordKind is KeywordKind.ReservedKeyword or KeywordKind.TypeFuncNameKeyword;
    }
}

/// <summary>PGNM004 - the same name written quoted and unquoted, which are different names when it has capitals.</summary>
public sealed class Pgnm004QuotedUnquotedMix : PgRuleBase
{
    public Pgnm004QuotedUnquotedMix() : base("PGNM004", "Naming", "Name used both quoted and unquoted", DiagnosticSeverity.Warning,
        "\"Orders\" (quoted, with a capital) and Orders (unquoted, folded to orders) are two different names. A script that uses both for one object "
        + "refers to two objects, and the unquoted one usually fails with \"does not exist\".",
        "sql-syntax-lexical#sql-syntax-identifiers") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var unquoted = new HashSet<string>(StringComparer.Ordinal);
        var quoted = new List<(PgSig Token, string Text)>();
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            foreach (var (start, end) in s.NameTokens())
                unquoted.Add(ctx.Text.Substring(start, end - start).ToLowerInvariant());
            foreach (var token in s.Tokens.Sig)
            {
                if (token.IsQuoted) quoted.Add((token, token.Name.Text));
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (token, text) in quoted)
        {
            // Only a quoted name that folding would change can differ from its unquoted spelling.
            if (text == text.ToLowerInvariant() || !unquoted.Contains(text.ToLowerInvariant()) || !reported.Add(text)) continue;
            report.Token(token, $"\"{text}\" is not the same name as {text.ToLowerInvariant()} (which is also used in this script).");
        }
    }
}

/// <summary>PGNM005 - a name that can never be written without quotes.</summary>
public sealed class Pgnm005NameNeedsQuotes : PgRuleBase
{
    public Pgnm005NameNeedsQuotes() : base("PGNM005", "Naming", "Name that always needs quotes", DiagnosticSeverity.Information,
        "A name with spaces or punctuation, or one that begins with a digit, cannot be written without double quotes in any statement, view, function body or tool. "
        + "Letters, digits and underscores, starting with a letter or underscore, keep the name usable everywhere.",
        "sql-syntax-lexical#sql-syntax-identifiers") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var token in s.Tokens.Sig)
            {
                if (!token.IsQuoted) continue;
                var inner = token.Name.Text;
                if (inner.Length > 0 && !IsPlain(inner))
                    report.Token(token, $"\"{inner}\" has to be quoted every time because of its spaces, punctuation or first character.");
            }
        }
    }

    // What an unquoted name may be made of: letters (of any alphabet), digits, underscore and $, not starting with a digit or $.
    private static bool IsPlain(string name)
    {
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$');
    }
}

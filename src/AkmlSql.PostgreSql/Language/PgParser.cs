using PgSqlParser;

namespace AkmlSql.PostgreSql.Language;

/// <summary>A syntax error with its position as an editor needs it (0-based UTF-16 offset, 1-based line and column).</summary>
/// <param name="SqlState">Always <c>42601</c> (syntax_error): the parser does not expose a SQLSTATE and syntax errors are what it reports.</param>
public sealed record PgParseError(string Message, int Offset, int Line, int Column, string SqlState);

/// <summary>The outcome of parsing a script: a tree per statement, or the first error.</summary>
public sealed class PgParseResult
{
    internal PgParseResult(ParseResult? tree, PgParseError? error)
    {
        Tree = tree;
        Error = error;
    }

    public bool Success => Error == null;
    public ParseResult? Tree { get; }
    public PgParseError? Error { get; }
}

/// <summary>
/// Spec 042 (research R1) - the thin wrapper over the real PostgreSQL parser (libpg_query, the server's own grammar,
/// through the pgsqlparser package). Everything in the library that needs an exact parse of PostgreSQL text - Parse,
/// formatting, the analysis rules - goes through here, so offsets and error positions are converted in one place.
/// </summary>
public static class PgParser
{
    private const string SyntaxErrorSqlState = "42601";

    public static PgParseResult Parse(string sql)
    {
        var result = Parser.Parse(sql);
        if (result.IsSuccess) return new PgParseResult(result.Value, null);

        var error = result.Error!;
        // CursorPos is 1-based in code points (0 when unknown); the wrapper converts it to a UTF-16 offset.
        var offset = error.CursorPos > 0 ? Math.Min(error.GetCursorCharOffset(sql), sql.Length) : 0;
        var (line, column) = PgPositions.ToLineColumn(sql, offset);
        return new PgParseResult(null, new PgParseError(error.Message, offset, line, column, SyntaxErrorSqlState));
    }

    /// <summary>Tokens for display and analysis: keywords with their reservation kind, literals, operators, comments. UTF-16 offsets.</summary>
    public static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        var result = Parser.Tokenize(sql);
        return result.IsSuccess ? result.Value : Array.Empty<SqlToken>();
    }

    /// <summary>The kind of each statement (Select, Transaction, Copy, Ddl ...); empty when the text does not parse.</summary>
    public static IReadOnlyList<StatementInfo> Classify(string sql)
    {
        var result = Parser.Classify(sql);
        return result.IsSuccess ? result.Value : Array.Empty<StatementInfo>();
    }

    /// <summary>
    /// True when two parse trees mean the same statements: same count, each pair equal once every source location is
    /// ignored. Whitespace, comments, keyword case and layout therefore do not matter; a changed literal, identifier
    /// (quoted or not), operator or clause does. This is the formatter's "meaning preserved" check (FR-141).
    /// </summary>
    public static bool TreesEquivalent(ParseResult left, ParseResult right)
    {
        if (left.Stmts.Count != right.Stmts.Count) return false;
        for (var i = 0; i < left.Stmts.Count; i++)
        {
            if (!left.Stmts[i].Stmt.EqualsIgnoringLocations(right.Stmts[i].Stmt)) return false;
        }
        return true;
    }
}

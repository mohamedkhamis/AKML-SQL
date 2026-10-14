using PgSqlParser;

namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// One statement of a script. <see cref="Start"/> and <see cref="End"/> are UTF-16 offsets into the script;
/// <see cref="Text"/> is <c>script[Start..End]</c> without the terminating semicolon (comments inside it stay, a hint
/// comment at the front must reach the server).
/// </summary>
/// <param name="IsMetaCommand">The statement is, or ends with, a psql meta-command; it is never sent to the server.</param>
/// <param name="Unterminated">The statement ends inside an unfinished string, quoted name, dollar body or comment.</param>
public sealed record PgStatement(int Start, int End, string Text, int StartLine, int StartColumn, bool Unterminated, bool IsMetaCommand);

/// <summary>
/// Spec 042 (FR-010, FR-019, research R7) - splits a script into the statements PostgreSQL runs one at a time. A
/// semicolon ends a statement unless it is inside a string, a quoted name, a comment, a dollar-quoted body, or the body of
/// a SQL-standard function (<c>BEGIN ATOMIC ... END</c>). There is no batch separator: a <c>GO</c> line is just text.
///
/// <para>
/// Why not the parser's own splitter: it is exact for valid scripts (the tests compare the two on those) but gives up on
/// the first syntax error and knows nothing of psql meta-commands, while a workspace must run the statements before a
/// mistake and report the mistake where it is.
/// </para>
/// </summary>
public static class PgStatementSplitter
{
    public static IReadOnlyList<PgStatement> Split(string sql)
    {
        var statements = new List<PgStatement>();
        var tokens = PgLexicalScanner.Scan(sql);

        var start = -1;            // first non-white-space character of the statement being read
        var end = -1;              // end of its last non-white-space token
        var hasCode = false;       // something other than comments
        var unterminated = false;
        string? firstWord = null;
        var atomicDepth = 0;       // > 0 while inside BEGIN ATOMIC ... END
        string? previousWord = null;

        void Flush(bool isMeta)
        {
            if (start >= 0 && (hasCode || isMeta))
            {
                var (line, column) = PgPositions.ToLineColumn(sql, start);
                statements.Add(new PgStatement(start, end, sql.Substring(start, end - start), line, column, unterminated, isMeta));
            }
            start = end = -1;
            hasCode = unterminated = false;
            firstWord = previousWord = null;
            atomicDepth = 0;
        }

        for (var t = 0; t < tokens.Count; t++)
        {
            var token = tokens[t];
            if (token.Kind == PgTokenKind.Whitespace) continue;

            if (token.Kind == PgTokenKind.Semicolon)
            {
                if (atomicDepth > 0)
                {
                    end = token.End; // part of the body
                    continue;
                }
                Flush(isMeta: false);
                continue;
            }

            if (start < 0) start = token.Start;

            if (token.Kind == PgTokenKind.MetaCommand)
            {
                end = token.End;
                hasCode = true;
                Flush(isMeta: true);
                continue;
            }

            end = token.End;
            if (!token.Terminated) unterminated = true;

            if (token.Kind is PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            hasCode = true;

            if (token.Kind == PgTokenKind.Identifier)
            {
                var word = sql.Substring(token.Start, token.End - token.Start).ToUpperInvariant();
                firstWord ??= word;

                if (atomicDepth > 0)
                {
                    if (word == "CASE") atomicDepth++;
                    else if (word == "END") atomicDepth--;
                }
                else if (word == "ATOMIC" && previousWord == "BEGIN" && firstWord == "CREATE")
                {
                    atomicDepth = 1;
                }
                previousWord = word;
            }
            else
            {
                previousWord = null;
            }
        }

        Flush(isMeta: false);
        return statements;
    }
}

/// <summary>Facts about a statement that need the PostgreSQL parser rather than the scanner.</summary>
public static class PgStatementInspector
{
    /// <summary>
    /// True for <c>COPY ... FROM STDIN</c> and <c>COPY ... TO STDOUT</c>: they stream data through the client, which a
    /// script run from the workspace cannot do (FR-019). <c>COPY</c> from or to a server file or <c>PROGRAM</c> is an
    /// ordinary statement and is not matched. Text that does not parse is not a client copy (the server will say why).
    /// </summary>
    public static bool UsesClientCopy(string statementText)
    {
        // Parsing every statement of a script just to rule this out would double the cost of a run.
        if (!StartsWithCopy(statementText)) return false;

        var parsed = Parser.Parse(statementText);
        if (!parsed.IsSuccess || parsed.Value.Stmts.Count == 0) return false;

        var copy = parsed.Value.Stmts[0].Stmt?.CopyStmt;
        return copy != null && !copy.IsProgram && string.IsNullOrEmpty(copy.Filename);
    }

    /// <summary>
    /// True when the statement's first word is <c>SELECT</c>, <c>WITH</c>, <c>TABLE</c> or <c>VALUES</c> - a statement that returns
    /// a table's rows, for which the driver is asked where each column came from (spec 042, FR-130). Anything else, such as
    /// <c>INSERT ... RETURNING</c> or <c>EXPLAIN</c>, returns rows that cannot be written back to a table by key.
    /// </summary>
    public static bool ReturnsRowsOfATable(string statementText)
    {
        foreach (var token in PgLexicalScanner.Scan(statementText))
        {
            if (token.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            if (token.Kind != PgTokenKind.Identifier) return false;
            var word = statementText.AsSpan(token.Start, token.End - token.Start);
            return word.Equals("SELECT", StringComparison.OrdinalIgnoreCase) || word.Equals("WITH", StringComparison.OrdinalIgnoreCase)
                || word.Equals("TABLE", StringComparison.OrdinalIgnoreCase) || word.Equals("VALUES", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // The first word that is not white space or a comment is COPY.
    private static bool StartsWithCopy(string text)
    {
        foreach (var token in PgLexicalScanner.Scan(text))
        {
            if (token.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            return token.Kind == PgTokenKind.Identifier
                && token.End - token.Start == 4
                && string.Compare(text, token.Start, "COPY", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
        }
        return false;
    }
}

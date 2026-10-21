using PgSqlParser;

namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// One statement of a script. <see cref="Start"/> and <see cref="End"/> are UTF-16 offsets into the script;
/// <see cref="Text"/> is <c>script[Start..End]</c> without the terminating semicolon (comments inside it stay, a hint
/// comment at the front must reach the server).
/// </summary>
/// <param name="IsMetaCommand">The statement is, or ends with, a psql meta-command; it is never sent to the server.</param>
/// <param name="Unterminated">The statement ends inside an unfinished string, quoted name, dollar body or comment.</param>
public sealed record PgStatement(int Start, int End, string Text, int StartLine, int StartColumn, bool Unterminated, bool IsMetaCommand)
{
    private IReadOnlyList<string>? _leadingWords;

    /// <summary>
    /// The first words of the statement (<see cref="PgLeadingWords"/>), recorded by the splitter as it read the script so that
    /// the command tag, the transaction tracker and the inspectors do not read the text again; found from the text for a
    /// statement made another way.
    /// </summary>
    public IReadOnlyList<string> LeadingWords
    {
        get => _leadingWords ??= PgLeadingWords.Of(Text);
        init => _leadingWords = value;
    }

    /// <summary>True when the words were recorded (by the splitter) rather than read from the text on first use; for the tests.</summary>
    internal bool HasRecordedLeadingWords => _leadingWords != null;

    // The leading words follow from Text, so they take no part in equality: a record would otherwise compare whether they have been read yet.
    public bool Equals(PgStatement? other) =>
        other is not null && Start == other.Start && End == other.End && Text == other.Text && StartLine == other.StartLine
        && StartColumn == other.StartColumn && Unterminated == other.Unterminated && IsMetaCommand == other.IsMetaCommand;

    public override int GetHashCode() => HashCode.Combine(Start, End, Text, StartLine, StartColumn, Unterminated, IsMetaCommand);
}

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
        var leading = new List<string>();   // the statement's leading words, read here so no reader has to scan the text again
        var leadingOpen = true;             // false once a token that is not a leading word has been seen
        var cursor = new PgPositions.Cursor(sql);     // statements come in order, so each position is found from the last one

        void Flush(bool isMeta)
        {
            if (start >= 0 && (hasCode || isMeta))
            {
                var (line, column) = cursor.MoveTo(start);
                statements.Add(new PgStatement(start, end, sql.Substring(start, end - start), line, column, unterminated, isMeta)
                {
                    LeadingWords = leading.ToArray(),
                });
            }
            start = end = -1;
            hasCode = unterminated = false;
            firstWord = previousWord = null;
            atomicDepth = 0;
            leading = new List<string>();
            leadingOpen = true;
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
            if (leadingOpen) leadingOpen = PgLeadingWords.Take(sql, token, leading);

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
    public static bool UsesClientCopy(string statementText) => UsesClientCopy(statementText, PgLeadingWords.Of(statementText, 1));

    /// <summary>The same for a statement of a split script, which knows its first word already.</summary>
    public static bool UsesClientCopy(PgStatement statement) => UsesClientCopy(statement.Text, statement.LeadingWords);

    private static bool UsesClientCopy(string statementText, IReadOnlyList<string> leadingWords)
    {
        // Parsing every statement of a script just to rule this out would double the cost of a run.
        if (leadingWords.Count == 0 || leadingWords[0] != "COPY") return false;

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
    public static bool ReturnsRowsOfATable(string statementText) => ReturnsRowsOfATable(PgLeadingWords.Of(statementText, 1));

    /// <summary>The same for a statement of a split script, which knows its first word already.</summary>
    public static bool ReturnsRowsOfATable(PgStatement statement) => ReturnsRowsOfATable(statement.LeadingWords);

    private static bool ReturnsRowsOfATable(IReadOnlyList<string> leadingWords) =>
        leadingWords.Count > 0 && leadingWords[0] is "SELECT" or "WITH" or "TABLE" or "VALUES";
}

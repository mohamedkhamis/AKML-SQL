namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// Spec 042 (FR-010, FR-011, task T058) - the explanatory text this product adds to what the server says, or says in its
/// place when a statement is not sent at all: a <c>GO</c> line that the server rejects, a psql meta-command, a
/// <c>COPY ... FROM STDIN</c> that needs a client to stream data. None of it replaces the server's own message.
/// </summary>
public static class PgErrorHints
{
    /// <summary>
    /// A hint when <paramref name="sql"/> has a line that is only <c>GO</c> (optionally with a count) outside any string,
    /// comment or dollar-quoted body; null otherwise. <c>GO</c> is a SQL Server batch separator that PostgreSQL does not
    /// know, so it is neither split on nor removed - the server's error stands and this says what it probably means.
    /// </summary>
    public static string? GoSeparator(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return null;

        var tokens = PgLexicalScanner.Scan(sql);
        for (var t = 0; t < tokens.Count; t++)
        {
            var token = tokens[t];
            if (token.Kind != PgTokenKind.Identifier || token.End - token.Start != 2) continue;
            if (!string.Equals(sql.Substring(token.Start, 2), "GO", StringComparison.OrdinalIgnoreCase)) continue;
            if (!OnlyWhiteSpaceBefore(sql, token.Start)) continue;
            if (!OnlyACountAfter(sql, tokens, t)) continue;

            return "GO is a SQL Server batch separator and PostgreSQL does not know it. Remove the GO line and end each statement with a semicolon.";
        }
        return null;
    }

    /// <summary>The server's hint first, then ours, one per line; null when there is neither.</summary>
    public static string? Combine(string? serverHint, string? ours)
    {
        var hasServer = !string.IsNullOrEmpty(serverHint);
        var hasOurs = !string.IsNullOrEmpty(ours);
        if (hasServer && hasOurs) return serverHint + "\n" + ours;
        if (hasServer) return serverHint;
        return hasOurs ? ours : null;
    }

    /// <summary>Why a psql meta-command (a backslash command) was not run.</summary>
    public static string MetaCommandSkipped(string command)
    {
        var name = FirstWord(command);
        return name + " is a psql client command, not SQL, so it was not sent to the server. "
            + "Use the Schema explorer to browse objects, or query the system catalogs.";
    }

    /// <summary>Why <c>COPY ... FROM STDIN</c> or <c>TO STDOUT</c> was not run.</summary>
    public static string ClientCopySkipped() =>
        "COPY with STDIN or STDOUT needs a client to stream the data and was not run. "
        + "Use INSERT statements, or COPY from or to a file on the server.";

    private static string FirstWord(string command)
    {
        var trimmed = command.TrimStart();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
        return end == 0 ? "This command" : trimmed.Substring(0, end);
    }

    private static bool OnlyWhiteSpaceBefore(string sql, int position)
    {
        for (var i = position - 1; i >= 0 && sql[i] != '\n' && sql[i] != '\r'; i--)
        {
            if (!char.IsWhiteSpace(sql[i])) return false;
        }
        return true;
    }

    // GO may be followed by a repeat count, then the end of the line (or a line comment).
    private static bool OnlyACountAfter(string sql, IReadOnlyList<PgLexToken> tokens, int index)
    {
        for (var t = index + 1; t < tokens.Count; t++)
        {
            var next = tokens[t];
            switch (next.Kind)
            {
                case PgTokenKind.Whitespace:
                    // A run with a line break ends this line; one without is trailing blanks and the next token decides.
                    for (var i = next.Start; i < next.End; i++)
                    {
                        if (sql[i] == '\n' || sql[i] == '\r') return true;
                    }
                    break;
                case PgTokenKind.Number:
                    break;
                case PgTokenKind.LineComment:
                    return true;
                default:
                    return false;
            }
        }
        return true;
    }
}

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>A run of tokens, by index into the script's token stream, both ends inclusive.</summary>
public readonly record struct TokenRange(int First, int Last);

/// <summary>
/// Finds the text the parser could not read. ScriptDom recovers from a syntax error by leaving the
/// statement out of the tree (and a whole batch, when its only statement is broken), so the tokens
/// of that statement belong to no statement. Both layouts place tokens from the tree, and a token
/// no statement owned used to be flushed on the way to the next placed one with default spacing:
/// the broken statement collapsed onto one line and the statement after it was appended to that
/// line (UI scan 2026-11-03, S2). Every run found here is kept exactly as written instead, on its
/// own lines, and the statements around it are formatted as usual.
/// </summary>
public static class UnparsedText
{
    /// <summary>
    /// Runs of significant tokens outside every top-level statement, other than batch separators
    /// (<c>GO</c> and its repeat count) and stray terminators, which a valid script can have between
    /// statements and which the layouts already place. The batch after a <c>GO n</c> is not a run
    /// either: ScriptDom cannot parse the count and leaves that whole batch out of the tree, and the
    /// layouts lay it out from its tokens as they always have. Empty for a script that parsed.
    /// </summary>
    public static List<TokenRange> Find(TSqlScript script, IList<TSqlParserToken> tokens)
    {
        var runs = new List<TokenRange>();
        var next = 0;
        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (statement.FirstTokenIndex < 0) continue;
                Scan(tokens, next, statement.FirstTokenIndex - 1, runs);
                next = Math.Max(next, statement.LastTokenIndex + 1);
            }
        }
        Scan(tokens, next, tokens.Count - 1, runs);
        return runs;
    }

    private static void Scan(IList<TSqlParserToken> tokens, int from, int to, List<TokenRange> runs)
    {
        int first = -1, last = -1;
        var afterCountedGo = false;   // inside the batch after a "GO n": not unread text
        for (var i = Math.Max(0, from); i <= to && i < tokens.Count; i++)
        {
            switch (tokens[i].TokenType)
            {
                case TSqlTokenType.WhiteSpace:
                case TSqlTokenType.EndOfFile:
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                    break;
                case TSqlTokenType.Go:
                    Close();
                    // "GO 5": the count on the same line belongs to the separator.
                    var count = NextSignificant(tokens, i + 1, to);
                    afterCountedGo = count >= 0 && tokens[count].TokenType == TSqlTokenType.Integer && !NewLineBetween(tokens, i, count);
                    if (afterCountedGo) i = count;
                    break;
                case TSqlTokenType.Semicolon when first < 0:
                    break;
                default:
                    if (afterCountedGo) break;
                    if (first < 0) first = i;
                    last = i;
                    break;
            }
        }
        Close();

        void Close()
        {
            if (first >= 0) runs.Add(new TokenRange(first, last));
            first = last = -1;
        }
    }

    private static int NextSignificant(IList<TSqlParserToken> tokens, int from, int to)
    {
        for (var i = from; i <= to && i < tokens.Count; i++)
        {
            if (tokens[i].TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                return i;
        }
        return -1;
    }

    private static bool NewLineBetween(IList<TSqlParserToken> tokens, int from, int to)
    {
        for (var i = from + 1; i < to; i++)
        {
            if (tokens[i].TokenType == TSqlTokenType.WhiteSpace && tokens[i].Text.Contains('\n')) return true;
        }
        return false;
    }
}

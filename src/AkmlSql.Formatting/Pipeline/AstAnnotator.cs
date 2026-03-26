using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 2: Scans the token stream and classifies each comment as Trailing, Leading, or Standalone.
/// A Trailing comment is on the same line as a preceding semantic token.
/// A Leading comment appears on its own line before the next semantic token.
/// A Standalone comment has blank lines separating it from surrounding code.
/// </summary>
public class AstAnnotator
{
    public List<CommentAttachment> AttachComments(IList<TSqlParserToken> tokens)
    {
        var comments = new List<CommentAttachment>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                comments.Add(new CommentAttachment
                {
                    TokenIndex = i,
                    Text = t.Text,
                    AttachmentType = ClassifyComment(tokens, i),
                    OriginalIndent = ComputeOriginalIndent(tokens, i)
                });
            }
        }
        return comments;
    }

    private static CommentAttachmentType ClassifyComment(IList<TSqlParserToken> tokens, int index)
    {
        // Check if trailing: same line as preceding semantic token
        bool hasPrecedingSemantic = false;
        bool newLineBeforeComment = false;

        if (index > 0)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                var tt = tokens[i].TokenType;
                if (tt == TSqlTokenType.WhiteSpace)
                {
                    if (tokens[i].Text.Contains('\n'))
                    {
                        newLineBeforeComment = true;
                        break;
                    }
                    continue;
                }
                if (tt == TSqlTokenType.EndOfFile)
                    break;

                // Found a semantic token on the same line
                hasPrecedingSemantic = true;
                break;
            }
        }

        if (hasPrecedingSemantic && !newLineBeforeComment)
            return CommentAttachmentType.Trailing;

        // Check for Standalone: blank line before OR blank line after the comment
        bool blankLineBefore = HasBlankLineBefore(tokens, index);
        bool blankLineAfter = HasBlankLineAfter(tokens, index);

        if (blankLineBefore && blankLineAfter)
            return CommentAttachmentType.Standalone;

        // Default: Leading (on its own line, attaches to next semantic token)
        return CommentAttachmentType.Leading;
    }

    private static bool HasBlankLineBefore(IList<TSqlParserToken> tokens, int index)
    {
        int newlineCount = 0;
        for (int i = index - 1; i >= 0; i--)
        {
            if (tokens[i].TokenType == TSqlTokenType.WhiteSpace)
            {
                newlineCount += CountNewlines(tokens[i].Text);
                if (newlineCount >= 2)
                    return true;
            }
            else
            {
                break;
            }
        }
        return false;
    }

    private static bool HasBlankLineAfter(IList<TSqlParserToken> tokens, int index)
    {
        int newlineCount = 0;
        for (int i = index + 1; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType == TSqlTokenType.WhiteSpace)
            {
                newlineCount += CountNewlines(tokens[i].Text);
                if (newlineCount >= 2)
                    return true;
            }
            else
            {
                break;
            }
        }
        return false;
    }

    private static int CountNewlines(string text)
    {
        int count = 0;
        foreach (var t in text)
        {
            if (t == '\n')
                count++;
        }
        return count;
    }

    private static int ComputeOriginalIndent(IList<TSqlParserToken> tokens, int index)
    {
        // Look at the whitespace token immediately before this comment
        if (index > 0 && tokens[index - 1].TokenType == TSqlTokenType.WhiteSpace)
        {
            var ws = tokens[index - 1].Text;
            // Find the last line of the whitespace (after the last newline)
            int lastNewline = ws.LastIndexOf('\n');
            var indentPart = lastNewline >= 0 ? ws[(lastNewline + 1)..] : ws;
            return indentPart.Length;
        }
        return 0;
    }
}

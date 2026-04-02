#nullable enable
using System.Text;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Collapses all non-significant whitespace to produce compact, single-line SQL.
/// Preserves content inside string literals ('...'), single-line comments (-- ...\n),
/// and block comments (/* ... */). Newlines before single-line comments are retained
/// so that -- comments remain on their own line.
/// </summary>
public class UnformatOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        if (string.IsNullOrEmpty(context.DocumentText))
            return (context.DocumentText, []);

        try
        {
            var result = Compact(context.DocumentText);
            return (result, []);
        }
        catch
        {
            return (context.DocumentText, []);
        }
    }

    internal static string Compact(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        int len = text.Length;
        bool lastWasSpace = false;

        while (i < len)
        {
            char c = text[i];

            // ── String literal: '...' with '' escaping ────────────────────
            if (c == '\'')
            {
                FlushSpace(sb, ref lastWasSpace);
                sb.Append(c);
                i++;
                while (i < len)
                {
                    char sc = text[i];
                    sb.Append(sc);
                    i++;
                    if (sc == '\'')
                    {
                        // Check for escaped quote ''
                        if (i < len && text[i] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                        }
                        else
                        {
                            break; // End of string literal
                        }
                    }
                }
                continue;
            }

            // ── Single-line comment: -- until \n ──────────────────────────
            if (c == '-' && i + 1 < len && text[i + 1] == '-')
            {
                // Preserve a newline before single-line comments so they stay on their own line
                // But avoid doubled newlines between consecutive -- comments
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                {
                    if (lastWasSpace)
                        lastWasSpace = false;
                    sb.Append('\n');
                }

                // Copy comment verbatim (including the --)
                while (i < len && text[i] != '\n')
                {
                    sb.Append(text[i]);
                    i++;
                }
                // Append the newline if present
                if (i < len && text[i] == '\n')
                {
                    sb.Append('\n');
                    i++;
                }
                lastWasSpace = false;
                continue;
            }

            // ── Block comment: /* ... */ ──────────────────────────────────
            if (c == '/' && i + 1 < len && text[i + 1] == '*')
            {
                FlushSpace(sb, ref lastWasSpace);
                sb.Append('/');
                sb.Append('*');
                i += 2;
                while (i < len)
                {
                    if (text[i] == '*' && i + 1 < len && text[i + 1] == '/')
                    {
                        sb.Append('*');
                        sb.Append('/');
                        i += 2;
                        break;
                    }
                    sb.Append(text[i]);
                    i++;
                }
                continue;
            }

            // ── Whitespace: collapse consecutive whitespace to single space ─
            if (char.IsWhiteSpace(c))
            {
                lastWasSpace = true;
                i++;
                continue;
            }

            // ── Normal character ──────────────────────────────────────────
            FlushSpace(sb, ref lastWasSpace);
            sb.Append(c);
            i++;
        }

        // Trim leading/trailing whitespace from each line
        return TrimLines(sb.ToString());
    }

    private static void FlushSpace(StringBuilder sb, ref bool lastWasSpace)
    {
        if (lastWasSpace && sb.Length > 0)
        {
            sb.Append(' ');
        }
        lastWasSpace = false;
    }

    private static string TrimLines(string text)
    {
        var lines = text.Split('\n');
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) result.Append('\n');
            result.Append(lines[i].TrimEnd('\r').Trim());
        }
        return result.ToString().Trim();
    }
}

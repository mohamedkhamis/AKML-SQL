using System.Text;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 5: Converts LayoutNode list into final formatted text string.
/// Handles indentation (tabs or spaces), line breaks, comment emission,
/// trailing whitespace removal, and final newline.
/// </summary>
public class TextEmitter
{
    public string Emit(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var sb = new StringBuilder();
        var ws = profile.Whitespace;
        bool useTabs = ws.TabStyle == "tabs";
        int tabSize = ws.TabSize;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (node.IsInNoformatRegion)
            {
                sb.Append(node.OriginalText);
                continue;
            }

            switch (node.PrecedingBreak)
            {
                case BreakType.EmptyLine:
                    sb.Append('\n');
                    sb.Append('\n');
                    AppendIndent(sb, node.IndentLevel, useTabs, tabSize);
                    break;

                case BreakType.NewLine:
                    sb.Append('\n');
                    AppendIndent(sb, node.IndentLevel, useTabs, tabSize);
                    break;

                case BreakType.None:
                    if (node.PrecedingSpaces > 0)
                        sb.Append(' ', node.PrecedingSpaces);
                    break;
            }

            sb.Append(node.FormattedText);

            // Emit trailing comment on same line
            if (node.TrailingComment != null)
            {
                sb.Append(' ');
                sb.Append(node.TrailingComment.Text.TrimEnd());
            }
        }

        var result = sb.ToString();

        // Trailing whitespace removal
        if (ws.TrailingWhitespace == "remove")
        {
            result = RemoveTrailingWhitespace(result);
        }

        // Final newline handling
        if (ws.FinalNewline == "ensure")
        {
            if (result.Length > 0 && !result.EndsWith('\n'))
                result += "\n";
        }
        else if (ws.FinalNewline == "remove")
        {
            result = result.TrimEnd('\r', '\n');
        }

        return result;
    }

    private static void AppendIndent(StringBuilder sb, int level, bool useTabs, int tabSize)
    {
        if (level <= 0) return;

        if (useTabs)
        {
            sb.Append('\t', level);
        }
        else
        {
            sb.Append(' ', level * tabSize);
        }
    }

    private static string RemoveTrailingWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                // Find the end of content (excluding trailing spaces/tabs before this newline)
                int end = i;
                if (end > start && text[end - 1] == '\r')
                    end--;

                // Trim trailing whitespace from this line
                while (end > start && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                    end--;

                sb.Append(text, start, end - start);
                sb.Append('\n');
                start = i + 1;
            }
        }

        // Handle last line (no trailing newline)
        if (start < text.Length)
        {
            int end = text.Length;
            while (end > start && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                end--;
            sb.Append(text, start, end - start);
        }

        return sb.ToString();
    }
}

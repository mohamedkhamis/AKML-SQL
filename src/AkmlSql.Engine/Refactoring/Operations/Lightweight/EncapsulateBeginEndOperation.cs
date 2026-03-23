using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Refactoring.Operations;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Wraps a selection (or the entire batch if no selection) in BEGIN ... END.
/// </summary>
public class EncapsulateBeginEndOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var text = context.DocumentText;

            int wrapStart;
            int wrapEnd;

            if (context.HasSelection)
            {
                // Find statements fully within selection
                var script = context.Script;
                if (script == null)
                    return (context.DocumentText, []);

                var statementsInSelection = GetStatementsInSelection(
                    script, context.SelectionStart, context.SelectionLength);

                if (statementsInSelection.Count == 0)
                    return (context.DocumentText, []);

                wrapStart = statementsInSelection.Min(s => s.StartOffset);
                wrapEnd = statementsInSelection.Max(s => s.StartOffset + s.FragmentLength);
            }
            else
            {
                // Wrap entire document text
                wrapStart = 0;
                wrapEnd = text.Length;
            }

            // Determine indentation at the wrap start
            var indent = GetIndentation(text, wrapStart);

            var inner = text[wrapStart..wrapEnd];
            // Indent each line of the inner content
            var indentedInner = IndentText(inner, indent + "    ");

            var wrapped = $"BEGIN\r\n{indentedInner}\r\n{indent}END";
            return (text[..wrapStart] + wrapped + text[wrapEnd..], []);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private static List<TSqlStatement> GetStatementsInSelection(
        TSqlScript script, int selStart, int selLength)
    {
        var result = new List<TSqlStatement>();
        int selEnd = selStart + selLength;

        foreach (var batch in script.Batches)
        {
            foreach (var stmt in batch.Statements)
            {
                var stmtEnd = stmt.StartOffset + stmt.FragmentLength;
                if (stmt.StartOffset >= selStart && stmtEnd <= selEnd)
                    result.Add(stmt);
            }
        }

        return result;
    }

    private static string GetIndentation(string text, int offset)
    {
        // Walk back to find the start of the line
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
            lineStart--;

        var sb = new System.Text.StringBuilder();
        for (int i = lineStart; i < offset && i < text.Length; i++)
        {
            if (text[i] == ' ' || text[i] == '\t')
                sb.Append(text[i]);
            else
                break;
        }
        return sb.ToString();
    }

    private static string IndentText(string text, string indent)
    {
        var lines = text.Split('\n');
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) result.Append('\n');
            var line = lines[i].TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line))
                result.Append(indent).Append(line);
            else
                result.Append(line);
        }
        return result.ToString();
    }
}

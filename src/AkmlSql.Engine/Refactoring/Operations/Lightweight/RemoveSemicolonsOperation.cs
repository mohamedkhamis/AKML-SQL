using System;
using System.Collections.Generic;
using AkmlSql.Engine.Refactoring.Operations;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Removes semicolon statement terminators from the document or selection.
/// </summary>
public class RemoveSemicolonsOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var tokens = context.Tokens;
            if (tokens == null || tokens.Count == 0)
                return (context.DocumentText, []);

            // Collect semicolon offsets in descending order so we can remove them safely
            var offsets = new List<int>();

            foreach (var token in tokens)
            {
                if (token.TokenType != TSqlTokenType.Semicolon)
                    continue;

                if (context.HasSelection)
                {
                    var tokenOffset = token.Offset;
                    if (tokenOffset < context.SelectionStart ||
                        tokenOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                offsets.Add(token.Offset);
            }

            if (offsets.Count == 0)
                return (context.DocumentText, []);

            // Sort descending so removals don't shift earlier offsets
            offsets.Sort((a, b) => b.CompareTo(a));

            var text = context.DocumentText;
            foreach (var offset in offsets)
            {
                if (offset < text.Length && text[offset] == ';')
                    text = text.Remove(offset, 1);
            }

            return (text, []);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }
}

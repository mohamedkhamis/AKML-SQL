using System;
using System.Collections.Generic;
using AkmlSql.Engine.Refactoring.Operations;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Replaces deprecated SQL syntax patterns:
/// - "!=" operator → "&lt;&gt;"
/// - "*=" or "=*" (old outer join operators) → warning (manual conversion required)
/// - "text", "ntext", "image" data types → "nvarchar(max)", "nvarchar(max)", "varbinary(max)"
/// </summary>
public class ReplaceDeprecatedSyntaxOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var script = context.Script;
            var tokens = context.Tokens;

            var warnings     = new List<string>();
            var replacements = new List<(int offset, int length, string newText)>();

            // Scan token stream for deprecated operator syntax (*=, =*)
            if (tokens != null)
            {
                foreach (var token in tokens)
                {
                    var tokenText = token.Text;

                    // Old outer join operators — warn, don't auto-convert
                    if (tokenText == "*=" || tokenText == "=*")
                    {
                        warnings.Add($"Manual conversion required for old-style outer join at line {token.Line}");
                    }
                }
            }

            // Use AST visitor for != → <> replacement (avoids tokenizer ambiguity)
            if (script != null)
            {
                var notEqVisitor = new NotEqualVisitor();
                script.Accept(notEqVisitor);

                foreach (var (startOffset, fragLen) in notEqVisitor.NotEqualRanges)
                {
                    // Verify the original text is indeed "!=" before replacing
                    if (startOffset >= 0 && startOffset + fragLen <= context.DocumentText.Length)
                    {
                        // The BooleanComparisonExpression spans from first operand to last;
                        // we need to find the != operator within the fragment
                        var fragText = context.DocumentText.Substring(startOffset, fragLen);
                        var neIdx    = fragText.IndexOf("!=", StringComparison.Ordinal);
                        if (neIdx >= 0)
                            replacements.Add((startOffset + neIdx, 2, "<>"));
                    }
                }

                // Also use AST visitor for deprecated data types
                var dataTypeVisitor = new DeprecatedDataTypeVisitor();
                script.Accept(dataTypeVisitor);
                replacements.AddRange(dataTypeVisitor.Replacements);
            }

            if (replacements.Count == 0 && warnings.Count == 0)
                return (context.DocumentText, []);

            // Apply replacements in descending offset order
            replacements.Sort((a, b) => b.offset.CompareTo(a.offset));
            var text = context.DocumentText;
            foreach (var (offset, length, newText) in replacements)
            {
                if (offset >= 0 && offset + length <= text.Length)
                    text = text[..offset] + newText + text[(offset + length)..];
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private sealed class NotEqualVisitor : TSqlFragmentVisitor
    {
        public List<(int startOffset, int fragLen)> NotEqualRanges { get; } = [];

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType == BooleanComparisonType.NotEqualToExclamation)
                NotEqualRanges.Add((node.StartOffset, node.FragmentLength));
        }
    }

    private sealed class DeprecatedDataTypeVisitor : TSqlFragmentVisitor
    {
        public List<(int offset, int length, string newText)> Replacements { get; } = [];

        public override void Visit(ColumnDefinition node)       => CheckDataType(node.DataType);
        public override void Visit(ProcedureParameter node)     => CheckDataType(node.DataType);
        public override void Visit(DeclareVariableElement node) => CheckDataType(node.DataType);

        private void CheckDataType(DataTypeReference? dataType)
        {
            if (dataType is not SqlDataTypeReference sqlType) return;

            var replacement = sqlType.SqlDataTypeOption switch
            {
                SqlDataTypeOption.Text  => "nvarchar(max)",
                SqlDataTypeOption.NText => "nvarchar(max)",
                SqlDataTypeOption.Image => "varbinary(max)",
                _                       => null
            };

            if (replacement == null) return;
            Replacements.Add((sqlType.StartOffset, sqlType.FragmentLength, replacement));
        }
    }
}

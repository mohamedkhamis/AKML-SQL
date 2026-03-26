using System.Text;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Extract to Stored Procedure operation.
/// </summary>
public class ExtractToProcOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasSelection)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["A text selection is required to extract a stored procedure"]
            });
        }

        var selStart  = ctx.SelectionStart;
        var selLength = ctx.SelectionLength;
        var docText   = ctx.DocumentText;

        if (selStart < 0 || selStart + selLength > docText.Length)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection bounds are out of range"]
            });
        }

        var selectedText = docText.Substring(selStart, selLength);
        var procName     = string.IsNullOrWhiteSpace(request.ExtractedUnitName)
            ? "NewProcedure"
            : request.ExtractedUnitName;

        // Collect variable references inside the selection
        var innerVars = CollectVariableReferences(ctx.Script, selStart, selStart + selLength);

        // Collect DECLARE statements outside the selection (potential parameters)
        var outerDeclarations = CollectOuterDeclarations(ctx.Script, selStart, selStart + selLength);

        // Parameters = variables that are declared OUTSIDE the selected block
        var parameters = innerVars
            .Where(v => outerDeclarations.ContainsKey(v))
            .Select(v => (Name: v, Type: outerDeclarations[v]))
            .Distinct()
            .ToList();

        // Build CREATE PROCEDURE
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE PROCEDURE dbo.{procName}");
        if (parameters.Count > 0)
        {
            var paramLines = parameters
                .Select(p => $"    {p.Name} {p.Type}")
                .ToArray();
            sb.AppendLine(string.Join(",\n", paramLines));
        }
        sb.AppendLine("AS");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    SET NOCOUNT ON");

        // Indent selected text
        foreach (var line in selectedText.Split('\n'))
            sb.AppendLine("    " + line.TrimEnd('\r'));

        sb.AppendLine("END");
        var procText = sb.ToString().TrimEnd();

        // Build EXEC replacement
        var execParams = string.Join(", ", parameters.Select(p => $"{p.Name} = {p.Name}"));
        var execText   = parameters.Count > 0
            ? $"EXEC dbo.{procName} {execParams}"
            : $"EXEC dbo.{procName}";

        var changes = new[]
        {
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = selStart,
                EndOffset      = selStart + selLength,
                OldText        = selectedText,
                NewText        = execText,
                ChangeCategory = ChangeCategory.Structure
            }
        };

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply             = true,
            Changes              = changes,
            Warnings             = [],
            Errors               = [],
            GeneratedObjectTexts = [procText]
        });
    }

    public override Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        var result = ApplyChanges(request.ApprovedChanges, string.Empty);
        return Task.FromResult(new RefactorApplyResponse
        {
            Success             = true,
            AppliedCount        = request.ApprovedChanges.Length,
            UpdatedDocumentText = result
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static HashSet<string> CollectVariableReferences(
        TSqlScript script, int start, int end)
    {
        var visitor = new VariableRefVisitor(start, end);
        script.Accept(visitor);
        return visitor.Variables;
    }

    private static Dictionary<string, string> CollectOuterDeclarations(
        TSqlScript script, int selStart, int selEnd)
    {
        var visitor = new DeclareVisitor(selStart, selEnd);
        script.Accept(visitor);
        return visitor.Declarations;
    }

    // ─── Nested Visitors ────────────────────────────────────────────────────────

    private sealed class VariableRefVisitor(int start, int end) : TSqlFragmentVisitor
    {
        public HashSet<string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(VariableReference node)
        {
            if (node.StartOffset >= start && node.StartOffset + node.FragmentLength <= end)
                Variables.Add(node.Name);
        }
    }

    private sealed class DeclareVisitor(int selStart, int selEnd) : TSqlFragmentVisitor
    {
        /// <summary>Maps variable name → SQL type string, for declarations OUTSIDE the selection.</summary>
        public Dictionary<string, string> Declarations { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(DeclareVariableStatement node)
        {
            // Only collect declarations that are OUTSIDE the selection
            var nodeStart = node.StartOffset;
            var nodeEnd   = node.StartOffset + node.FragmentLength;

            if (nodeStart >= selStart && nodeEnd <= selEnd)
                return; // inside selection — local variable, not a parameter

            foreach (var decl in node.Declarations)
            {
                var varName = decl.VariableName?.Value;
                if (string.IsNullOrEmpty(varName)) continue;

                var typeName = decl.DataType != null
                    ? GetTypeName(decl.DataType)
                    : "NVARCHAR(MAX)";

                Declarations[$"@{varName}"] = typeName;
            }
        }

        private static string GetTypeName(DataTypeReference dt)
        {
            if (dt is SqlDataTypeReference sql)
            {
                var name = sql.SqlDataTypeOption.ToString().ToUpperInvariant();
                if (sql.Parameters.Count > 0)
                {
                    var ps = string.Join(", ", sql.Parameters.Select(p => p.LiteralType == LiteralType.Max ? "MAX" : p.Value));
                    return $"{name}({ps})";
                }
                return name;
            }
            return "NVARCHAR(MAX)";
        }
    }
}

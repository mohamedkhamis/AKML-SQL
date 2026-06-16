using AkmlSql.Core.Ipc.Messages;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Extract to CTE operation — extracts a subquery to a Common Table Expression.
/// </summary>
public class ExtractToCteOperation : HeavyweightOperationBase
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
                Errors   = ["Selection is not a valid standalone query"]
            });
        }

        var selectionStart  = ctx.SelectionStart;
        var selectionLength = ctx.SelectionLength;
        var selectionEnd    = selectionStart + selectionLength;
        var documentText    = ctx.DocumentText;

        if (selectionStart < 0 || selectionEnd > documentText.Length || selectionLength <= 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection is not a valid standalone query"]
            });
        }

        var selectedText = documentText.Substring(selectionStart, selectionLength);

        // Find a QuerySpecification or subquery within the selection bounds
        var querySpec = FindQuerySpecificationInSelection(ctx.Script, selectionStart, selectionEnd);
        if (querySpec == null)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection is not a valid standalone query"]
            });
        }

        var name = string.IsNullOrWhiteSpace(request.ExtractedUnitName)
            ? "CteQuery"
            : request.ExtractedUnitName;

        // Infer CTE column names from SelectElements
        var columnNames = InferColumnNames(querySpec);

        // Build the CTE text
        var indentedText = IndentText(selectedText, "    ");
        var ctePrependText = $"WITH {name} AS (\n{indentedText}\n)\n";

        var changes = new[]
        {
            // Change 1: replace the selected subquery with the CTE name reference
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = selectionStart,
                EndOffset      = selectionEnd,
                OldText        = selectedText,
                NewText        = name,
                ChangeCategory = ChangeCategory.Structure
            },
            // Change 2: insert the CTE block before offset 0 (prepend to document)
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = 0,
                EndOffset      = 0,
                OldText        = string.Empty,
                NewText        = ctePrependText,
                ChangeCategory = ChangeCategory.Structure
            }
        };

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply             = true,
            Changes              = changes,
            Warnings             = [],
            Errors               = [],
            GeneratedObjectTexts = [ctePrependText]
        });
    }

    public override Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        var result = ApplyChanges(request.ApprovedChanges, request.DocumentText);
        return Task.FromResult(new RefactorApplyResponse
        {
            Success             = true,
            AppliedCount        = request.ApprovedChanges.Length,
            UpdatedDocumentText = result
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static QuerySpecification? FindQuerySpecificationInSelection(
        TSqlScript script, int start, int end)
    {
        var visitor = new QuerySpecificationFinder(start, end);
        script.Accept(visitor);
        return visitor.Found;
    }

    private static List<string> InferColumnNames(QuerySpecification querySpec)
    {
        var names = new List<string>();
        var counter = 1;

        foreach (var element in querySpec.SelectElements)
        {
            if (element is SelectScalarExpression sse)
            {
                if (sse.ColumnName?.Value is string alias && !string.IsNullOrEmpty(alias))
                {
                    names.Add(alias);
                    continue;
                }

                if (sse.Expression is ColumnReferenceExpression cre &&
                    cre.MultiPartIdentifier?.Identifiers?.Count > 0)
                {
                    var lastPart = cre.MultiPartIdentifier.Identifiers.Last().Value;
                    if (!string.IsNullOrEmpty(lastPart))
                    {
                        names.Add(lastPart);
                        continue;
                    }
                }

                names.Add($"Col{counter}");
            }
            else
            {
                names.Add($"Col{counter}");
            }

            counter++;
        }

        return names;
    }

    private static string IndentText(string text, string indent)
    {
        var lines = text.Split('\n');
        return string.Join("\n", lines.Select(l => indent + l));
    }

    // ─── Nested Visitor ─────────────────────────────────────────────────────────

    private sealed class QuerySpecificationFinder(int start, int end) : TSqlFragmentVisitor
    {
        public QuerySpecification? Found { get; private set; }

        public override void Visit(QuerySpecification node)
        {
            if (Found != null) return;
            var nodeStart = node.StartOffset;
            var nodeEnd   = node.StartOffset + node.FragmentLength;

            if (nodeStart >= start && nodeEnd <= end)
                Found = node;
        }
    }
}

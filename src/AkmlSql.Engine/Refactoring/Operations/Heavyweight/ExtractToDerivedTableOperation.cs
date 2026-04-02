using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Extract to Derived Table operation.
/// </summary>
public class ExtractToDerivedTableOperation : HeavyweightOperationBase
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
                Errors   = ["Selection is not a valid query expression"]
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

        var selectedText = docText.Substring(selStart, selLength).Trim();

        // Validate selection starts with SELECT
        if (!selectedText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection is not a valid query expression"]
            });
        }

        var name           = string.IsNullOrWhiteSpace(request.ExtractedUnitName)
            ? "DerivedTable"
            : request.ExtractedUnitName;
        var derivedForm    = $"({selectedText}) AS {name}";

        var changes = new[]
        {
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = ctx.SelectionStart,
                EndOffset      = ctx.SelectionStart + ctx.SelectionLength,
                OldText        = docText.Substring(ctx.SelectionStart, ctx.SelectionLength),
                NewText        = derivedForm,
                ChangeCategory = ChangeCategory.Structure
            }
        };

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = changes,
            Warnings = [],
            Errors   = []
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
}

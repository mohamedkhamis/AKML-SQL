using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Encapsulate as View operation.
/// </summary>
public class EncapsulateAsViewOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var docText = ctx.DocumentText;

        // Use selection if available, else full document
        int  selStart;
        int  selLength;
        string selectedText;

        if (ctx.HasSelection &&
            ctx.SelectionStart >= 0 &&
            ctx.SelectionStart + ctx.SelectionLength <= docText.Length)
        {
            selStart    = ctx.SelectionStart;
            selLength   = ctx.SelectionLength;
            selectedText = docText.Substring(selStart, selLength).Trim();
        }
        else
        {
            selStart    = 0;
            selLength   = docText.Length;
            selectedText = docText.Trim();
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection is not a valid SELECT statement"]
            });
        }

        // Validate it's a SELECT statement
        if (!selectedText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Selection is not a valid SELECT statement"]
            });
        }

        var name        = string.IsNullOrWhiteSpace(request.ExtractedUnitName)
            ? "NewView"
            : request.ExtractedUnitName;
        var viewText    = $"CREATE VIEW dbo.{name} AS\n{selectedText}";
        var replacement = $"SELECT * FROM dbo.{name}";

        var changes = new[]
        {
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = selStart,
                EndOffset      = selStart + selLength,
                OldText        = docText.Substring(selStart, selLength),
                NewText        = replacement,
                ChangeCategory = ChangeCategory.Structure
            }
        };

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply             = true,
            Changes              = changes,
            Warnings             = [],
            Errors               = [],
            GeneratedObjectTexts = [viewText]
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

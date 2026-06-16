using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Convert Temp Table ↔ Table Variable operation.
/// </summary>
public class ConvertTempTableOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var direction = (RefactorOperationType)request.OperationType;

        return direction == RefactorOperationType.ConvertTempToTableVar
            ? Task.FromResult(ConvertTempToTableVar(ctx))
            : Task.FromResult(ConvertTableVarToTemp(ctx));
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

    // ─── Direction: #Name → @Name ─────────────────────────────────────────────

    private static RefactorPreviewResponse ConvertTempToTableVar(RefactoringContext ctx)
    {
        var docText = ctx.DocumentText;

        // Find CREATE TABLE #Name (...)
        var createMatch = Regex.Match(
            docText,
            @"CREATE\s+TABLE\s+(#\w+)\s*\(",
            RegexOptions.IgnoreCase);

        if (!createMatch.Success)
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["No CREATE TABLE #TempTable statement found"]
            };
        }

        var tempName = createMatch.Groups[1].Value;            // e.g. "#TempOrders"
        var varName  = "@" + tempName.Substring(1);            // e.g. "@TempOrders"

        // Check for name collision: does @varName already exist as a declared variable?
        var declarePattern = new Regex(
            $@"\bDECLARE\s+{Regex.Escape(varName)}\b",
            RegexOptions.IgnoreCase);
        if (declarePattern.IsMatch(docText))
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"A variable named {varName} already exists in this script."]
            };
        }

        var changes = new List<RefactorChangeInfo>();

        // 1. Replace "CREATE TABLE #Name (" with "DECLARE @Name TABLE ("
        var createText   = createMatch.Value;
        var createStart  = createMatch.Index;
        var replaceCreate = Regex.Replace(
            createText,
            @"CREATE\s+TABLE\s+#(\w+)\s*\(",
            m => $"DECLARE @{m.Groups[1].Value} TABLE (",
            RegexOptions.IgnoreCase);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = createStart,
            EndOffset      = createStart + createText.Length,
            OldText        = createText,
            NewText        = replaceCreate,
            ChangeCategory = ChangeCategory.Structure
        });

        // 2. Replace all remaining references to #Name with @Name (sorted descending by offset)
        // Use (?<!\w) and (?!\w) because # is not a word char so \b won't match at start of #Name
        var refPattern = new Regex(
            $@"(?<!\w){Regex.Escape(tempName)}(?!\w)",
            RegexOptions.IgnoreCase);
        foreach (Match m in refPattern.Matches(docText).Cast<Match>().OrderByDescending(m2 => m2.Index))
        {
            // Skip the CREATE TABLE span we already covered
            if (m.Index >= createStart && m.Index < createStart + createText.Length)
                continue;

            changes.Add(new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = m.Index,
                EndOffset      = m.Index + m.Length,
                OldText        = m.Value,
                NewText        = varName,
                ChangeCategory = ChangeCategory.Structure
            });
        }

        // Sort descending by StartOffset for safe reverse-order application
        var sorted = changes.OrderByDescending(c => c.StartOffset).ToArray();

        var warnings = new[]
        {
            $"Table variables do not support statistics. Queries using {varName} may perform differently from {tempName}."
        };

        return new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = sorted,
            Warnings = warnings,
            Errors   = []
        };
    }

    // ─── Direction: @Name TABLE → #Name ──────────────────────────────────────

    private static RefactorPreviewResponse ConvertTableVarToTemp(RefactoringContext ctx)
    {
        var docText = ctx.DocumentText;

        // Find DECLARE @Name TABLE (...)
        var declareMatch = Regex.Match(
            docText,
            @"DECLARE\s+(@\w+)\s+TABLE\s*\(",
            RegexOptions.IgnoreCase);

        if (!declareMatch.Success)
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["No DECLARE @TableVar TABLE statement found"]
            };
        }

        var varName  = declareMatch.Groups[1].Value;           // e.g. "@TempOrders"
        var tempName = "#" + varName.Substring(1);             // e.g. "#TempOrders"

        // Check for name collision: does #tempName already exist?
        var tempPattern = new Regex(
            $@"(?<!\w){Regex.Escape(tempName)}(?!\w)",
            RegexOptions.IgnoreCase);
        if (tempPattern.IsMatch(docText))
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"A temp table named {tempName} already exists in this script."]
            };
        }

        var changes = new List<RefactorChangeInfo>();

        // 1. Replace "DECLARE @Name TABLE (" with "CREATE TABLE #Name ("
        var declareText   = declareMatch.Value;
        var declareStart  = declareMatch.Index;
        var replaceDeclare = Regex.Replace(
            declareText,
            @"DECLARE\s+@(\w+)\s+TABLE\s*\(",
            m => $"CREATE TABLE #{m.Groups[1].Value} (",
            RegexOptions.IgnoreCase);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = declareStart,
            EndOffset      = declareStart + declareText.Length,
            OldText        = declareText,
            NewText        = replaceDeclare,
            ChangeCategory = ChangeCategory.Structure
        });

        // 2. Replace all remaining @Name references with #Name
        // @ is not a word char so \b won't match; use (?<!\w) / (?!\w) instead
        var refPattern = new Regex(
            $@"(?<!\w){Regex.Escape(varName)}(?!\w)",
            RegexOptions.IgnoreCase);
        foreach (Match m in refPattern.Matches(docText).Cast<Match>().OrderByDescending(m2 => m2.Index))
        {
            if (m.Index >= declareStart && m.Index < declareStart + declareText.Length)
                continue;

            changes.Add(new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = m.Index,
                EndOffset      = m.Index + m.Length,
                OldText        = m.Value,
                NewText        = tempName,
                ChangeCategory = ChangeCategory.Structure
            });
        }

        var sorted = changes.OrderByDescending(c => c.StartOffset).ToArray();

        return new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = sorted,
            Warnings = [],
            Errors   = []
        };
    }
}

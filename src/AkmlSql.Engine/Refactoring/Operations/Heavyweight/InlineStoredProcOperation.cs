using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Navigation;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 T063 / FR-020 — Inline a stored-procedure call into its (single-query) body.
///
/// Locates the <c>EXEC &lt;proc&gt; …</c> at the caret, fetches the procedure's live definition from
/// <c>sys.sql_modules</c> via <see cref="ObjectDefinitionService"/> (Preview only — Apply just
/// replays the reviewed text), and delegates the actual rewrite to the pure, unit-tested
/// <see cref="InlineStoredProcRewriter"/>. The user reviews the inlined SQL in the preview dialog
/// before it is applied, so the operation errs on the side of refusing anything the rewriter can't
/// inline cleanly.
/// </summary>
public class InlineStoredProcOperation : HeavyweightOperationBase
{
    public override async Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var docText = ctx.DocumentText;

        // 1. Find the EXEC at / containing the caret (else the first one).
        var collector = new ExecuteStatementCollector();
        ctx.Script.Accept(collector);

        var exec = FindTarget(collector.Statements, ctx.SelectionStart);
        if (exec is null)
            return Fail("Place the caret on an EXEC <procedure> statement to inline it.");

        if (exec.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference procRef)
            return Fail("The statement is not a stored-procedure call (EXEC <proc>).");

        // EXEC @rc = dbo.P captures the procedure's return code; inlining the body would silently
        // drop that assignment, so refuse.
        if (exec.ExecuteSpecification.Variable != null)
            return Fail("The EXEC captures the procedure's return code (EXEC @rc = …); it cannot be inlined.");

        var nameRef = procRef.ProcedureReference?.ProcedureReference?.Name;
        var procName = nameRef?.BaseIdentifier?.Value;
        var schemaName = nameRef?.SchemaIdentifier?.Value;
        if (string.IsNullOrEmpty(procName))
            return Fail("Could not resolve the procedure name from the EXEC statement.");

        if (string.Equals(procName, "sp_executesql", StringComparison.OrdinalIgnoreCase))
            return Fail("sp_executesql is dynamic SQL, not a stored procedure — use Inline EXEC instead.");

        // 2. A live connection is required to read the procedure body.
        if (string.IsNullOrEmpty(ctx.ConnectionString))
            return Fail("Inline Stored Procedure needs a live database connection to read the procedure body.");

        // 3. Collect the call-site arguments from the document.
        var callArgs = new List<InlineCallArg>();
        if (procRef.Parameters != null)
        {
            foreach (var p in procRef.Parameters)
            {
                if (p.ParameterValue == null) continue;
                callArgs.Add(new InlineCallArg
                {
                    Name      = p.Variable?.Name,
                    ValueText = docText.Substring(p.ParameterValue.StartOffset, p.ParameterValue.FragmentLength),
                    IsOutput  = p.IsOutput
                });
            }
        }

        // 4. Fetch the procedure definition live.
        var (definition, objectType, _) = await new ObjectDefinitionService()
            .GetDefinitionAsync(procName, schemaName, ctx.ConnectionString!, ctx.SchemaCache, ct);

        var display = schemaName != null ? $"{schemaName}.{procName}" : procName;
        if (string.IsNullOrEmpty(definition))
            return Fail($"Could not retrieve the definition for {display}.");
        if (!string.Equals(objectType, "Procedure", StringComparison.OrdinalIgnoreCase))
            return Fail($"{display} is not a stored procedure (type: {objectType ?? "unknown"}).");

        // 5. Pure transform.
        var result = InlineStoredProcRewriter.Inline(definition, callArgs);
        if (!result.Ok)
            return Fail(result.Error!);

        // 6. Replace the EXEC statement (keeping any trailing ';').
        var start = exec.StartOffset;
        var end   = TrimTrailingTerminator(docText, start, exec.StartOffset + exec.FragmentLength);
        var oldText = (start >= 0 && end <= docText.Length && end >= start)
            ? docText.Substring(start, end - start)
            : string.Empty;
        var (line, col) = OffsetToLineCol(docText, start);

        var change = new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = start,
            EndOffset      = end,
            OldText        = oldText,
            NewText        = result.InlinedSql!,
            Line           = line,
            Column         = col,
            ContextSnippet = ExtractContext(docText, start),
            ChangeCategory = ChangeCategory.Structure
        };

        return new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = [change],
            Warnings = [.. result.Warnings],
            Errors   = []
        };
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

    private static ExecuteStatement? FindTarget(List<ExecuteStatement> statements, int caret)
    {
        if (statements.Count == 0) return null;
        foreach (var s in statements)
        {
            var start = s.StartOffset;
            var end   = s.StartOffset + s.FragmentLength;
            if (caret >= start && caret <= end) return s;
        }
        return statements.OrderBy(s => s.StartOffset).First();
    }

    private static RefactorPreviewResponse Fail(string error) => new()
    {
        CanApply = false,
        Changes  = [],
        Warnings = [],
        Errors   = [error]
    };

    private sealed class ExecuteStatementCollector : TSqlFragmentVisitor
    {
        public List<ExecuteStatement> Statements { get; } = [];
        public override void Visit(ExecuteStatement node) => Statements.Add(node);
    }
}

using System.Text;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// T064 / FR-020 — Inline a dynamic EXEC into its underlying query.
///
/// Handles dynamic EXEC where the SQL is a string literal in the script:
///   • <c>EXEC('SELECT ...')</c> / <c>EXECUTE('...')</c> (incl. concatenated string literals)
///   • <c>EXEC sp_executesql N'...', N'@p type, ...', @p = value, ...</c> with literal bindings
///
/// The EXEC statement is replaced with the unwrapped inner SQL (doubled <c>''</c> quotes are
/// un-escaped via <see cref="StringLiteral.Value"/>). For sp_executesql, literal parameter
/// bindings are substituted into the inlined text; non-literal bindings are left as <c>@param</c>
/// and a warning is emitted.
///
/// No live DB. If the EXEC target is a stored-procedure name (<c>EXEC dbo.usp_x</c>) rather than
/// dynamic SQL, the operation returns <c>CanApply = false</c> (inlining a proc body needs the
/// live proc definition, which is out of scope here).
/// </summary>
public class InlineExecOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var docText = ctx.DocumentText;

        // 1. Locate the EXEC statement at / containing the caret, else the first one in the script.
        var collector = new ExecuteStatementCollector();
        ctx.Script.Accept(collector);

        var exec = FindTargetExec(collector.Statements, ctx.SelectionStart);
        if (exec is null)
        {
            return Fail("No EXEC / EXECUTE statement found to inline.");
        }

        var entity = exec.ExecuteSpecification?.ExecutableEntity;

        // 2. Dynamic EXEC('...') / EXECUTE('...')  → ExecutableStringList.
        if (entity is ExecutableStringList stringList)
        {
            var (innerSql, error) = JoinStringList(stringList);
            if (error is not null)
                return Fail(error);

            return Ok(BuildReplacement(docText, exec, innerSql!), warnings: []);
        }

        // 3. sp_executesql N'...', ... vs. a real stored-proc name → ExecutableProcedureReference.
        if (entity is ExecutableProcedureReference procRef)
        {
            var procName = GetProcName(procRef);

            if (!string.Equals(procName, "sp_executesql", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    $"EXEC target '{procName}' is a stored procedure, not dynamic SQL. " +
                    "Inlining a procedure body requires its live definition (out of scope).");
            }

            var (innerSql, warnings, error) = InlineSpExecutesql(docText, procRef);
            if (error is not null)
                return Fail(error);

            return Ok(BuildReplacement(docText, exec, innerSql!), warnings);
        }

        return Fail("EXEC statement does not contain inlinable dynamic SQL.");
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

    // ─── EXEC('...') string list ─────────────────────────────────────────────

    /// <summary>
    /// Concatenates an <see cref="ExecutableStringList"/> into a single inner SQL string.
    /// ScriptDom flattens <c>'a' + 'b'</c> into multiple <c>Strings</c> entries; a nested
    /// <see cref="BinaryExpression"/> of literals is also handled. Any non-literal entry
    /// (variable, function call, …) means the SQL is not a pure literal and cannot be inlined.
    /// </summary>
    private static (string? innerSql, string? error) JoinStringList(ExecutableStringList list)
    {
        if (list.Strings is null || list.Strings.Count == 0)
            return (null, "EXEC has no SQL string to inline.");

        var sb = new StringBuilder();
        foreach (var expr in list.Strings)
        {
            if (!TryCollectLiteral(expr, sb, out var blocker))
                return (null,
                    $"Cannot inline: the EXEC SQL is built from a non-literal expression ({blocker}). " +
                    "Only literal string SQL can be inlined without a live database.");
        }

        return (sb.ToString(), null);
    }

    /// <summary>
    /// Appends the de-escaped value of a literal (or recursively, a binary concatenation of
    /// literals) to <paramref name="sb"/>. Returns false and the offending fragment description
    /// when a non-literal node is encountered.
    /// </summary>
    private static bool TryCollectLiteral(ScalarExpression expr, StringBuilder sb, out string blocker)
    {
        switch (expr)
        {
            case StringLiteral sl:
                sb.Append(sl.Value); // .Value already un-escapes '' and strips quotes / N prefix.
                blocker = string.Empty;
                return true;

            case ParenthesisExpression pe:
                return TryCollectLiteral(pe.Expression, sb, out blocker);

            case BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } be:
                return TryCollectLiteral(be.FirstExpression, sb, out blocker)
                       && TryCollectLiteral(be.SecondExpression, sb, out blocker);

            case VariableReference vr:
                blocker = vr.Name;
                return false;

            default:
                blocker = expr.GetType().Name;
                return false;
        }
    }

    // ─── sp_executesql ───────────────────────────────────────────────────────

    /// <summary>
    /// Inlines an <c>sp_executesql</c> call: takes the first positional argument as the SQL
    /// template literal and substitutes literal <c>@param = value</c> bindings into it. The
    /// optional second positional argument (the parameter-definitions string) is ignored.
    /// Non-literal bindings are left in place and surfaced as warnings.
    /// </summary>
    private static (string? innerSql, string[] warnings, string? error) InlineSpExecutesql(
        string docText, ExecutableProcedureReference procRef)
    {
        var parameters = procRef.Parameters;
        if (parameters is null || parameters.Count == 0)
            return (null, [], "sp_executesql call has no SQL string argument.");

        // First positional argument (Variable == null) is the SQL template.
        ExecuteParameter? templateParam = null;
        foreach (var p in parameters)
        {
            if (p.Variable is null)
            {
                templateParam = p;
                break;
            }
        }

        if (templateParam?.ParameterValue is not StringLiteral templateLiteral)
            return (null, [], "sp_executesql first argument is not a literal SQL string and cannot be inlined.");

        var template = templateLiteral.Value; // de-escaped inner SQL.

        // Collect named bindings (Variable != null). The first non-template positional argument
        // (the parameter-definitions string) is ignored.
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var p in parameters)
        {
            if (p.Variable is null)
                continue; // template or param-defs string — not a binding.

            var name = p.Variable.Name; // includes leading '@'
            if (string.IsNullOrEmpty(name) || !IsLiteral(p.ParameterValue))
                continue; // non-literal bindings are left as @param and reported by SubstituteParameters.

            // Use the RAW document text of the value so quotes / N-prefix are preserved.
            bindings[name] = docText.Substring(p.ParameterValue.StartOffset, p.ParameterValue.FragmentLength);
        }

        var inlined = SubstituteParameters(template, bindings, warnings);
        return (inlined, [.. warnings], null);
    }

    /// <summary>
    /// Token-aware single-pass substitution: tokenizes the template and replaces ONLY variable
    /// tokens that have a literal binding, emitting every other token (keywords, string literals,
    /// comments, …) verbatim. This avoids the corruption a regex-over-raw-text pass causes — it
    /// never re-substitutes an already-inlined value and never touches a <c>@param</c> that appears
    /// inside the template's own string literals / comments. Any variable left without a binding is
    /// reported as a warning (so positional / unbound / non-literal params are never silently lost).
    /// </summary>
    private static string SubstituteParameters(string template, Dictionary<string, string> bindings, List<string> warnings)
    {
        var sb = new StringBuilder();
        var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tok in new AkmlSql.Engine.Parser.TsqlParserService().GetTokenStream(template))
        {
            if (tok.TokenType == TSqlTokenType.Variable)
            {
                if (bindings.TryGetValue(tok.Text, out var value))
                {
                    sb.Append(value);
                    continue;
                }
                unresolved.Add(tok.Text);
            }
            sb.Append(tok.Text);
        }

        if (unresolved.Count > 0)
            warnings.Add(
                $"sp_executesql parameter(s) not inlined (no literal binding): {string.Join(", ", unresolved)}. " +
                "They remain in the inlined SQL — declare or replace them manually.");

        return sb.ToString();
    }

    private static bool IsLiteral(ScalarExpression? expr) => expr is Literal;

    private static string GetProcName(ExecutableProcedureReference procRef)
    {
        var schemaObj = procRef.ProcedureReference?.ProcedureReference?.Name;
        return schemaObj?.BaseIdentifier?.Value ?? string.Empty;
    }

    // ─── Statement selection ─────────────────────────────────────────────────

    private static ExecuteStatement? FindTargetExec(List<ExecuteStatement> statements, int caret)
    {
        if (statements.Count == 0)
            return null;

        // Prefer the EXEC statement whose span contains the caret.
        foreach (var s in statements)
        {
            var start = s.StartOffset;
            var end   = s.StartOffset + s.FragmentLength;
            if (caret >= start && caret <= end)
                return s;
        }

        // Otherwise the first EXEC in document order.
        return statements.OrderBy(s => s.StartOffset).First();
    }

    // ─── Response helpers ────────────────────────────────────────────────────

    private static RefactorChangeInfo[] BuildReplacement(string docText, ExecuteStatement exec, string newText)
    {
        var start = exec.StartOffset;
        var end   = exec.StartOffset + exec.FragmentLength;
        // Don't consume the statement terminator: if the fragment ends with an optional-whitespace ';',
        // shrink the replaced span so the ';' (and any trailing whitespace) survives the edit.
        end = TrimTrailingTerminator(docText, start, end);
        var oldText = (start >= 0 && end <= docText.Length && end >= start)
            ? docText.Substring(start, end - start)
            : string.Empty;

        var (line, col) = OffsetToLineCol(docText, start);

        return
        [
            new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = start,
                EndOffset      = end,
                OldText        = oldText,
                NewText        = newText,
                Line           = line,
                Column         = col,
                ContextSnippet = ExtractContext(docText, start),
                ChangeCategory = ChangeCategory.Structure
            }
        ];
    }

    private static Task<RefactorPreviewResponse> Ok(RefactorChangeInfo[] changes, string[] warnings) =>
        Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = changes,
            Warnings = warnings,
            Errors   = []
        });

    private static Task<RefactorPreviewResponse> Fail(string error) =>
        Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = false,
            Changes  = [],
            Warnings = [],
            Errors   = [error]
        });

    // ─── Visitor ─────────────────────────────────────────────────────────────

    private sealed class ExecuteStatementCollector : TSqlFragmentVisitor
    {
        public List<ExecuteStatement> Statements { get; } = [];

        public override void Visit(ExecuteStatement node) => Statements.Add(node);
    }
}

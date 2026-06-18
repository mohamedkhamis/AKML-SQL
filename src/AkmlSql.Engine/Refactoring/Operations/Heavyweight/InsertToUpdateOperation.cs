using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight INSERT → UPDATE operation (FR-021).
///
/// Converts a single-row <c>INSERT … VALUES</c> statement into an equivalent
/// <c>UPDATE &lt;table&gt; SET &lt;col&gt; = &lt;value&gt; … WHERE &lt;pk&gt; = &lt;value&gt; …</c>:
/// <list type="bullet">
///   <item>SET   = columns that are NOT primary-key, NOT identity, NOT computed.</item>
///   <item>WHERE = the table's PRIMARY KEY columns (looked up in <see cref="RefactoringContext.SchemaCache"/>).</item>
/// </list>
///
/// Edge cases:
/// <list type="bullet">
///   <item>Identity / computed columns are skipped from SET.</item>
///   <item>No PK found → emit the UPDATE with a <c>-- TODO: WHERE …</c> placeholder + a Warning; CanApply stays true.</item>
///   <item>INSERT … SELECT → CanApply = false (only single VALUES row is in scope).</item>
///   <item>Multi-row VALUES → CanApply = false (only a single row maps to one UPDATE).</item>
/// </list>
/// </summary>
public class InsertToUpdateOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var docText = ctx.DocumentText;

        // ── Find the INSERT statement at / containing the selection (else first in doc order) ──
        var visitor = new InsertStatementCollector();
        ctx.Script.Accept(visitor);

        if (visitor.Statements.Count == 0)
            return Ok(); // no INSERT in scope — nothing to do

        var insert = SelectTargetStatement(visitor.Statements, ctx.SelectionStart);
        var spec   = insert.InsertSpecification;
        if (spec == null)
            return Ok();

        // ── Only single-row VALUES is supported ──
        if (spec.InsertSource is SelectInsertSource)
            return Fail("Convert INSERT…SELECT to UPDATE is not supported — only a single-row INSERT…VALUES can be converted.");

        if (spec.InsertSource is not ValuesInsertSource values)
            return Fail("Only an INSERT…VALUES statement can be converted to an UPDATE.");

        if (values.IsDefaultValues || values.RowValues == null || values.RowValues.Count == 0)
            return Fail("The INSERT has no VALUES row to convert.");

        if (values.RowValues.Count > 1)
            return Fail("Multi-row INSERT…VALUES cannot be converted to a single UPDATE.");

        // ── Resolve the target table ──
        if (spec.Target is not NamedTableReference tableRef || tableRef.SchemaObject == null)
            return Fail("Could not resolve the INSERT target table.");

        var tableText  = docText.Substring(tableRef.StartOffset, tableRef.FragmentLength);
        var tableName  = tableRef.SchemaObject.BaseIdentifier?.Value ?? string.Empty;
        var schemaName = tableRef.SchemaObject.SchemaIdentifier?.Value ?? "dbo";

        // ── Build the column → value pairs from the explicit column list + the single VALUES row ──
        var row = values.RowValues[0];
        if (spec.Columns is { Count: > 0 } && row.ColumnValues is { } rv && spec.Columns.Count != rv.Count)
            return Fail("The INSERT column count does not match the VALUES count — cannot convert reliably.");

        var pairs = BuildPairs(spec, row, docText);
        if (pairs.Count == 0)
            return Fail("The INSERT has no explicit column list to map to values — a positional INSERT without a column list is not supported.");

        // ── Classify columns against the schema cache, but ONLY when the table's columns are loaded.
        //    During Phase-B-pending cold start dbObj exists with no columns; classifying then would put
        //    identity/key columns in SET. columnsKnown gates that and drives the warning below. ──
        var dbObj   = ctx.SchemaCache?.FindObject(schemaName, tableName);
        bool columnsKnown = dbObj is { ColumnsLoaded: true };
        var warnings = new List<string>();

        var setLines   = new List<string>();
        var whereLines = new List<string>();
        bool anyPk     = false;

        foreach (var (colName, valueText) in pairs)
        {
            var meta = columnsKnown
                ? dbObj!.Columns.FirstOrDefault(c => c.ColumnName.Equals(colName, StringComparison.OrdinalIgnoreCase))
                : null;

            if (meta is { IsPrimaryKey: true })
            {
                anyPk = true;
                whereLines.Add($"{colName} = {valueText}");
                continue;
            }

            // Skip identity / computed columns from SET (can't be assigned).
            if (meta is { IsIdentity: true } or { IsComputed: true })
                continue;

            setLines.Add($"{colName} = {valueText}");
        }

        if (setLines.Count == 0)
            return Fail("No assignable columns remain after excluding primary-key, identity and computed columns.");

        // ── Emit the UPDATE text ──
        var sb = new StringBuilder();
        sb.Append("UPDATE ").Append(tableText).Append('\n');
        sb.Append("SET ").Append(string.Join(",\n    ", setLines));

        if (anyPk)
        {
            sb.Append('\n').Append("WHERE ").Append(string.Join("\n  AND ", whereLines));
        }
        else
        {
            // No usable PK → placeholder WHERE + warn (CanApply stays true). Distinguish a real "no PK"
            // from "columns not loaded yet": in the latter the SET may wrongly include identity/key
            // columns, so the warning tells the user to review before applying.
            sb.Append('\n').Append("WHERE -- TODO: WHERE <key column(s)> = <value>");
            warnings.Add(columnsKnown
                ? $"No PRIMARY KEY found for {schemaName}.{tableName}; complete the placeholder WHERE clause."
                : $"Schema metadata for {schemaName}.{tableName} is not loaded yet — the SET clause may include identity/key columns and the WHERE is a placeholder; review before applying.");
        }

        var newText = sb.ToString();
        var insStart = insert.StartOffset;
        var insEnd   = TrimTrailingTerminator(docText, insStart, insert.StartOffset + insert.FragmentLength);
        var oldText  = docText.Substring(insStart, insEnd - insStart);
        var (line, col) = OffsetToLineCol(docText, insStart);

        var change = new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = insStart,
            EndOffset      = insEnd,
            OldText        = oldText,
            NewText        = newText,
            Line           = line,
            Column         = col,
            ContextSnippet = ExtractContext(docText, insert.StartOffset),
            ChangeCategory = ChangeCategory.Structure
        };

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = [change],
            Warnings = [.. warnings],
            Errors   = []
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

    /// <summary>
    /// Returns the INSERT statement whose span contains <paramref name="selectionStart"/>,
    /// falling back to the first INSERT in document order.
    /// </summary>
    private static InsertStatement SelectTargetStatement(List<InsertStatement> statements, int selectionStart)
    {
        var containing = statements.FirstOrDefault(s =>
            selectionStart >= s.StartOffset &&
            selectionStart <= s.StartOffset + s.FragmentLength);

        return containing ?? statements.OrderBy(s => s.StartOffset).First();
    }

    /// <summary>
    /// Zips the explicit column list to the single VALUES row, extracting each value's
    /// source text verbatim from the document. Columns without a matching value (or vice
    /// versa) are skipped defensively. Returns an empty list when no explicit column list
    /// is present (we cannot map positional values to columns without the schema order).
    /// </summary>
    private static List<(string ColumnName, string ValueText)> BuildPairs(
        InsertSpecification spec, RowValue row, string docText)
    {
        var pairs = new List<(string, string)>();

        var cols   = spec.Columns;
        var vals   = row.ColumnValues;
        if (cols == null || cols.Count == 0 || vals == null || vals.Count == 0)
            return pairs;

        var count = Math.Min(cols.Count, vals.Count);
        for (int i = 0; i < count; i++)
        {
            var colName = cols[i].MultiPartIdentifier?.Identifiers?.LastOrDefault()?.Value;
            if (string.IsNullOrEmpty(colName))
                continue;

            var valExpr  = vals[i];
            var valueText = docText.Substring(valExpr.StartOffset, valExpr.FragmentLength);
            pairs.Add((colName!, valueText));
        }

        return pairs;
    }

    private static Task<RefactorPreviewResponse> Ok() =>
        Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = [],
            Warnings = [],
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

    // ─── Visitor ────────────────────────────────────────────────────────────────

    private sealed class InsertStatementCollector : TSqlFragmentVisitor
    {
        public List<InsertStatement> Statements { get; } = [];
        public override void Visit(InsertStatement node) => Statements.Add(node);
    }
}

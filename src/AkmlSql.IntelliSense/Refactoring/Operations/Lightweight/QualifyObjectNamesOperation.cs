using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Spec 030 — schema-aware "Qualify object names" format action (TABLE qualify only). Adds the
/// owning schema prefix (e.g. <c>Orders</c> → <c>dbo.Orders</c>) to unqualified table references.
/// <para>
/// Bug #2 ISOLATION: qualification here is UNCONDITIONAL — it does NOT reuse ObjectProvider's
/// join-conditional gate (that gate is the completion-only Bug #2 fix). Format-time qualify always
/// qualifies, matching SQL Prompt. ObjectProvider is deliberately left untouched.
/// </para>
/// <para>
/// ASYMMETRY vs ExpandWildcards: this op does NOT gate on <c>ColumnsLoaded</c> — qualification only
/// needs object-name → schema (Phase A). Copying the ExpandInsert ColumnsLoaded guard here would
/// wrongly fail Qualify before columns load. Column → table.column qualify is OUT OF SCOPE.
/// </para>
/// </summary>
public class QualifyObjectNamesOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            if (context.SchemaCache == null)
                return (context.DocumentText,
                    ["Schema cache not available — connect to a database to qualify object names."]);

            var script = context.Script;
            if (script == null)
                return (context.DocumentText, []);

            var collector = new NamedTableCollector();
            script.Accept(collector);

            if (collector.Tables.Count == 0)
                return (context.DocumentText, []);

            // Bug #1: collect every CTE name in the script. A FROM reference that matches a
            // CTE name resolves to the CTE — qualifying it to a same-named cached table would
            // silently bypass the CTE (WRONG SEMANTICS). Collected globally (across batches):
            // over-exclusion only means a missed qualification, never an incorrect one.
            var cteNames = collector.CteNames;

            // Bare-name → owning schema(s) lookup over Phase-A object names (no ColumnsLoaded gate).
            // Grouped case-insensitively; resolution qualifies only when exactly one schema owns
            // the name (no default-schema hook is reachable, so multi-schema names are ambiguous).
            var byName = context.SchemaCache.GetAllObjects()
                .GroupBy(o => o.ObjectName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(o => o.SchemaName).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var warnings = new List<string>();

            // Descending by the BaseIdentifier insertion offset so each edit preserves earlier offsets.
            var candidates = collector.Tables
                .Select(t => t.SchemaObject)
                .Where(n => n?.BaseIdentifier != null && n.SchemaIdentifier == null) // skip already-qualified
                .OrderByDescending(n => n.BaseIdentifier.StartOffset)
                .ToList();

            var text = context.DocumentText;

            foreach (var name in candidates)
            {
                var baseId = name.BaseIdentifier;
                var tableName = baseId.Value;
                if (string.IsNullOrEmpty(tableName))
                    continue;

                // Bug #1: skip #temp/##temp tables and @table variables. They are never in the
                // schema cache — qualifying is wrong and the "not found" warning would be spurious.
                // (@tablevar parses as VariableTableReference so the collector never sees it; the
                // '@' guard is defensive only — #temp DOES arrive here as a NamedTableReference.)
                if (tableName[0] == '#' || tableName[0] == '@')
                    continue;

                // Bug #1: a reference whose name matches a CTE resolves to that CTE, not a table.
                if (cteNames.Contains(tableName))
                    continue;

                if (context.HasSelection)
                {
                    if (baseId.StartOffset < context.SelectionStart ||
                        baseId.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                if (!byName.TryGetValue(tableName, out var schemas) || schemas.Count == 0)
                {
                    warnings.Add($"Could not qualify '{tableName}' — not found in schema cache.");
                    continue;
                }

                // Bug #2: there is NO reachable default/active schema (no DefaultSchema on
                // DatabaseCache / SchemaEntry / RefactoringContext), so we must never guess —
                // preferring 'dbo' when both dbo.Orders and app.Orders exist could qualify to the
                // WRONG table when the user's default schema is 'app'. Conservative rule: qualify
                // ONLY when the bare name exists in EXACTLY ONE schema; otherwise skip + warn.
                var distinct = schemas
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (distinct.Count != 1)
                {
                    warnings.Add($"Could not qualify '{tableName}' — ambiguous; '{tableName}' exists in multiple schemas: {string.Join(", ", distinct)}.");
                    continue;
                }

                var resolvedSchema = distinct[0];

                // Preserve the BaseIdentifier's bracket style: bracket the injected schema only when
                // the original table name was bracketed ([Orders] → [dbo].[Orders]; Orders → dbo.Orders).
                var schemaToken = baseId.QuoteType == QuoteType.SquareBracket
                    ? $"[{resolvedSchema}]."
                    : $"{resolvedSchema}.";

                // Insert 'schema.' before the BaseIdentifier (lands before any leading '[').
                text = text.Insert(baseId.StartOffset, schemaToken);
            }

            return (text, [.. warnings]);
        }
        catch (Exception ex)
        {
            // Bug #7: never a silent no-op. Return the original text with a warning that
            // describes the failure, and log it so the engine log captures the stack trace.
            Log.Error(ex, "QualifyObjectNamesOperation.Apply failed");
            return (context.DocumentText, [$"Qualify failed: {ex.Message}"]);
        }
    }

    private sealed class NamedTableCollector : TSqlFragmentVisitor
    {
        public List<NamedTableReference> Tables { get; } = [];

        /// <summary>All CTE names declared anywhere in the script (case-insensitive).</summary>
        public HashSet<string> CteNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(NamedTableReference node) => Tables.Add(node);

        public override void Visit(CommonTableExpression node)
        {
            var cteName = node.ExpressionName?.Value;
            if (!string.IsNullOrEmpty(cteName))
                CteNames.Add(cteName!);
        }
    }
}

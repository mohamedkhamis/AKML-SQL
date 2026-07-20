using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE002 — Table or view reference lacks a schema prefix (e.g. Orders instead of dbo.Orders).</summary>
public sealed class Pe002UnqualifiedObjectName : IAnalysisRule
{
    public string RuleId => "PE002";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        // Two passes on purpose: ScriptDom does NOT visit in syntax order here —
        // SelectStatement.AcceptChildren walks the QueryExpression BEFORE the WITH clause,
        // so a single-pass visitor sees 'FROM MyCte' before the CTE's declaration.
        var cteNames = CteNameCollector.Collect(ctx.CurrentBatch);
        var visitor = new Visitor(ctx, cteNames);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    /// <summary>
    /// Collects every CTE name in the batch. A CTE reference has no schema BY DEFINITION —
    /// 'dbo.MyCte' would break the query, so PE002 flagging one is a false positive (the
    /// spike corpus had even baked PE002-on-DirectReports into 04-cte.expected.json).
    /// Batch-wide rather than per-statement scoping is deliberate: a table shadowed by a
    /// same-named CTE in another statement is skipped too — a conservative false-negative
    /// over a guaranteed-wrong "add dbo." suggestion.
    /// </summary>
    private sealed class CteNameCollector : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public static HashSet<string> Collect(TSqlFragment fragment)
        {
            var collector = new CteNameCollector();
            fragment.Accept(collector);
            return collector._names;
        }

        public override void Visit(CommonTableExpression node)
        {
            var name = node.ExpressionName?.Value;
            if (!string.IsNullOrEmpty(name)) _names.Add(name!);
        }
    }

    private sealed class Visitor(AnalysisContext ctx, HashSet<string> cteNames) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(NamedTableReference node)
        {
            var schemaObj = node.SchemaObject;
            if (schemaObj == null) return;

            // Already has a schema qualifier
            if (schemaObj.SchemaIdentifier != null) return;

            // Skip if database or server qualifier is present (unusual but valid)
            if (schemaObj.DatabaseIdentifier != null || schemaObj.ServerIdentifier != null) return;

            var tableName = schemaObj.BaseIdentifier?.Value;
            if (string.IsNullOrEmpty(tableName)) return;

            // Skip temp tables (#..., ##...) and table variables (@...)
            if (tableName.StartsWith("#") || tableName.StartsWith("@")) return;

            // Skip CTE references — they cannot carry a schema prefix
            if (cteNames.Contains(tableName!)) return;

            var insertPos = schemaObj.StartOffset;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE002",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE002", DiagnosticSeverity.Warning),
                Message      = $"Object '{tableName}' has no schema prefix — use 'dbo.{tableName}' to avoid schema resolution overhead",
                StartOffset  = schemaObj.StartOffset,
                EndOffset    = schemaObj.StartOffset + schemaObj.FragmentLength,
                Line         = schemaObj.StartLine,
                Column       = schemaObj.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = $"Add schema prefix 'dbo.'",
                        FixType          = FixType.Insert,
                        ReplacementStart = insertPos,
                        ReplacementEnd   = insertPos,
                        ReplacementText  = "dbo."
                    }
                ]
            });
        }
    }
}

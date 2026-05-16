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
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
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

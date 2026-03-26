using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE007 — IDENTITY column should use an integer type (INT, BIGINT, SMALLINT, TINYINT).</summary>
public sealed class De007IdentityOnNonInteger : IAnalysisRule
{
    public string RuleId => "DE007";
    public string Category => "Design";
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

        public override void Visit(ColumnDefinition node)
        {
            // ColumnDefinition.IdentityOptions is non-null when IDENTITY is specified
            if (node.IdentityOptions == null) return;

            if (node.DataType is not SqlDataTypeReference sqlType) return;

            var isIntegerType = sqlType.SqlDataTypeOption is
                SqlDataTypeOption.Int      or
                SqlDataTypeOption.BigInt   or
                SqlDataTypeOption.SmallInt or
                SqlDataTypeOption.TinyInt;

            if (isIntegerType) return;

            var colName = node.ColumnIdentifier?.Value ?? "unknown";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DE007",
                CategoryCode = "DE",
                Severity     = ctx.Settings.GetSeverity("DE007", DiagnosticSeverity.Warning),
                Message      = $"IDENTITY column '{colName}' should use an integer type (INT, BIGINT, SMALLINT, TINYINT)",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}

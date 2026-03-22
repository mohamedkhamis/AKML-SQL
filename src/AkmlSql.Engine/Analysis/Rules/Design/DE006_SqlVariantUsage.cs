using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE006 — SQL_VARIANT usage reduces type safety and complicates queries; use a specific data type instead.</summary>
public sealed class DE006_SqlVariantUsage : IAnalysisRule
{
    public string RuleId => "DE006";
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

        public override void Visit(ColumnDefinition node)      => CheckDataType(node.DataType, node);
        public override void Visit(DeclareVariableElement node) => CheckDataType(node.DataType, node);
        public override void Visit(ProcedureParameter node)    => CheckDataType(node.DataType, node);

        private void CheckDataType(DataTypeReference? dataType, TSqlFragment node)
        {
            if (dataType is not SqlDataTypeReference sqlType) return;
            if (sqlType.SqlDataTypeOption != SqlDataTypeOption.Sql_Variant) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DE006",
                CategoryCode = "DE",
                Severity     = ctx.Settings.GetSeverity("DE006", DiagnosticSeverity.Warning),
                Message      = "SQL_VARIANT usage reduces type safety and complicates queries — use a specific data type instead",
                StartOffset  = sqlType.StartOffset,
                EndOffset    = sqlType.StartOffset + sqlType.FragmentLength,
                Line         = sqlType.StartLine,
                Column       = sqlType.StartColumn,
                FixActions   = []
            });
        }
    }
}

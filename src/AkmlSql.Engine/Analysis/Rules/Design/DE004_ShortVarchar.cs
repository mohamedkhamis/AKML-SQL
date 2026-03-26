using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE004 — VARCHAR(1) or VARCHAR(2) is very short; consider CHAR for fixed-length data or a larger VARCHAR size.</summary>
public sealed class De004ShortVarchar : IAnalysisRule
{
    public string RuleId => "DE004";
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
            CheckDataType(node.DataType, node);
        }

        public override void Visit(DeclareVariableElement node)
        {
            CheckDataType(node.DataType, node);
        }

        public override void Visit(ProcedureParameter node)
        {
            CheckDataType(node.DataType, node);
        }

        private void CheckDataType(DataTypeReference? dataType, TSqlFragment node)
        {
            if (dataType is not SqlDataTypeReference sqlType) return;
            if (sqlType.SqlDataTypeOption != SqlDataTypeOption.VarChar) return;
            if (sqlType.Parameters.Count == 0) return;

            var param = sqlType.Parameters[0];
            if (param is not IntegerLiteral intLiteral) return;

            if (!int.TryParse(intLiteral.Value, out var size) || size > 2) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DE004",
                CategoryCode = "DE",
                Severity     = ctx.Settings.GetSeverity("DE004", DiagnosticSeverity.Warning),
                Message      = $"VARCHAR({size}) is very short — consider using CHAR({size}) for fixed-length data or VARCHAR with a larger size",
                StartOffset  = sqlType.StartOffset,
                EndOffset    = sqlType.StartOffset + sqlType.FragmentLength,
                Line         = sqlType.StartLine,
                Column       = sqlType.StartColumn,
                FixActions   = []
            });
        }
    }
}

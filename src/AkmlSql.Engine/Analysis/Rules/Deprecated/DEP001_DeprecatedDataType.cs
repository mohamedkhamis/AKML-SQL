using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP001 — The text, ntext, and image data types are deprecated; use varchar(max), nvarchar(max), and varbinary(max).</summary>
public sealed class Dep001DeprecatedDataType : IAnalysisRule
{
    public string RuleId => "DEP001";
    public string Category => "Deprecated";
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
            CheckDataType(node.DataType);
        }

        public override void Visit(ProcedureParameter node)
        {
            CheckDataType(node.DataType);
        }

        public override void Visit(DeclareVariableElement node)
        {
            CheckDataType(node.DataType);
        }

        private void CheckDataType(DataTypeReference? dataType)
        {
            if (dataType is not SqlDataTypeReference sqlType) return;

            var (replacement, typeName) = sqlType.SqlDataTypeOption switch
            {
                SqlDataTypeOption.Text  => ("varchar(max)", "text"),
                SqlDataTypeOption.NText => ("nvarchar(max)", "ntext"),
                SqlDataTypeOption.Image => ("varbinary(max)", "image"),
                _                       => (null, null)
            };

            if (replacement == null) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP001",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP001", DiagnosticSeverity.Warning),
                Message      = $"Data type '{typeName}' is deprecated — use '{replacement}' instead",
                StartOffset  = sqlType.StartOffset,
                EndOffset    = sqlType.StartOffset + sqlType.FragmentLength,
                Line         = sqlType.StartLine,
                Column       = sqlType.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = $"Replace with {replacement}",
                        FixType          = FixType.Transform,
                        ReplacementStart = sqlType.StartOffset,
                        ReplacementEnd   = sqlType.StartOffset + sqlType.FragmentLength,
                        ReplacementText  = replacement
                    }
                ]
            });
        }
    }
}

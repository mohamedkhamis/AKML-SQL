using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE001 — Dynamic SQL executed via EXEC() contains string concatenation with a variable or column — potential SQL injection.</summary>
public sealed class Se001SqlInjectionRisk : IAnalysisRule
{
    public string RuleId => "SE001";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
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

        public override void Visit(ExecuteStatement node)
        {
            var entity = node.ExecuteSpecification?.ExecutableEntity;
            if (entity is not ExecutableStringList esl) return;

            foreach (var expr in esl.Strings)
            {
                if (!ContainsVariableRef(expr)) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "SE001",
                    CategoryCode = "SE",
                    Severity     = ctx.Settings.GetSeverity("SE001", DiagnosticSeverity.Error),
                    Message      = "Dynamic SQL executed via EXEC() includes a variable or column reference — use sp_executesql with parameterized queries to prevent SQL injection",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });

                // Report once per execute statement even if multiple expressions contain variable refs
                break;
            }
        }

        private static bool ContainsVariableRef(ScalarExpression expr)
        {
            return expr switch
            {
                VariableReference => true,
                ColumnReferenceExpression => true,
                BinaryExpression be => ContainsVariableRef(be.FirstExpression)
                                       || ContainsVariableRef(be.SecondExpression),
                ParenthesisExpression pe => ContainsVariableRef(pe.Expression),
                _ => false
            };
        }
    }
}

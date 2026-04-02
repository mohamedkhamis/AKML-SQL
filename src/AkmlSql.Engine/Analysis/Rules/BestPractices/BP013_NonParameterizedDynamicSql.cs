using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP013 — Dynamic SQL built by string concatenation can enable SQL injection; use sp_executesql with parameters.</summary>
public sealed class Bp013NonParameterizedDynamicSql : IAnalysisRule
{
    public string RuleId => "BP013";
    public string Category => "BestPractices";
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
                if (!ContainsBinaryWithVariable(expr)) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "BP013",
                    CategoryCode = "BP",
                    Severity     = ctx.Settings.GetSeverity("BP013", DiagnosticSeverity.Error),
                    Message      = "Dynamic SQL built by string concatenation can enable SQL injection — use sp_executesql with parameters",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });

                // Report once per execute statement
                break;
            }
        }

        private static bool ContainsBinaryWithVariable(ScalarExpression expr)
        {
            return expr switch
            {
                BinaryExpression be => ContainsVariableRef(be.FirstExpression)
                                       || ContainsVariableRef(be.SecondExpression)
                                       || ContainsBinaryWithVariable(be.FirstExpression)
                                       || ContainsBinaryWithVariable(be.SecondExpression),
                ParenthesisExpression pe => ContainsBinaryWithVariable(pe.Expression),
                _ => false
            };
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

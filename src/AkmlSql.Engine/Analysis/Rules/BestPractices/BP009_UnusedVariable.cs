using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP009 — Variable is declared but never used; remove the declaration.</summary>
public sealed class Bp009UnusedVariable : IAnalysisRule
{
    public string RuleId => "BP009";
    public string Category => "BestPractices";
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

        public override void Visit(DeclareVariableStatement node)
        {
            foreach (var decl in node.Declarations)
            {
                var varName = decl.VariableName.Value; // includes the @ prefix

                // Search for any reference to this variable after the declaration ends
                var searchStart   = node.StartOffset + node.FragmentLength;
                var documentText  = ctx.DocumentText;

                if (!HasUsageAfterDeclaration(documentText, varName, searchStart))
                {
                    Diagnostics.Add(new AnalysisDiagnostic
                    {
                        RuleId       = "BP009",
                        CategoryCode = "BP",
                        Severity     = ctx.Settings.GetSeverity("BP009", DiagnosticSeverity.Warning),
                        Message      = $"Variable '{varName}' is declared but never used — remove the declaration",
                        StartOffset  = node.StartOffset,
                        EndOffset    = node.StartOffset + node.FragmentLength,
                        Line         = node.StartLine,
                        Column       = node.StartColumn,
                        FixActions   =
                        [
                            new AnalysisFixAction
                            {
                                Label            = $"Remove declaration of {varName}",
                                FixType          = FixType.Remove,
                                ReplacementStart = node.StartOffset,
                                ReplacementEnd   = node.StartOffset + node.FragmentLength,
                                ReplacementText  = ""
                            }
                        ]
                    });
                }
            }
        }

        private static bool HasUsageAfterDeclaration(string text, string varName, int afterOffset)
        {
            if (afterOffset >= text.Length) return false;

            var pos = afterOffset;
            while (pos < text.Length)
            {
                var idx = text.IndexOf(varName, pos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return false;

                // Ensure this is not part of a longer identifier: the character after the variable name
                // must be a non-identifier character (or end of string)
                var endIdx = idx + varName.Length;
                if (endIdx >= text.Length || !IsIdentifierChar(text[endIdx]))
                    return true;

                pos = idx + 1;
            }

            return false;
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}

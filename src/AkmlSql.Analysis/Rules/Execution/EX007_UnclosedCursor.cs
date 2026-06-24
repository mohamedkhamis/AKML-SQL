using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>
/// EX007 — A cursor that is DECLARE-d in the batch but never both CLOSE-d AND DEALLOCATE-d
/// will leak server-side resources (open server cursor, locks on base tables).
/// </summary>
public sealed class Ex007UnclosedCursor : IAnalysisRule
{
    public string RuleId => "EX007";
    public string Category => "Execution";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new CursorVisitor();
        ctx.CurrentBatch.Accept(visitor);

        // Post-walk: emit one diagnostic per cursor that lacks CLOSE and/or DEALLOCATE.
        var diagnostics = new List<AnalysisDiagnostic>();
        foreach (var (name, declNode) in visitor.Declared)
        {
            var isClosed      = visitor.Closed.Contains(name);
            var isDeallocated = visitor.Deallocated.Contains(name);
            if (isClosed && isDeallocated) continue;

            var missing = (!isClosed && !isDeallocated) ? "CLOSE and DEALLOCATE"
                        : !isClosed ? "CLOSE"
                        : "DEALLOCATE";

            diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "EX007",
                CategoryCode = "EX",
                Severity     = ctx.Settings.GetSeverity("EX007", DiagnosticSeverity.Warning),
                Message      = $"Cursor '{name}' is declared but never {missing}-d — resource leak",
                StartOffset  = declNode.StartOffset,
                EndOffset    = declNode.StartOffset + declNode.FragmentLength,
                Line         = declNode.StartLine,
                Column       = declNode.StartColumn,
                FixActions   = []
            });
        }

        return diagnostics;
    }

    private sealed class CursorVisitor : TSqlFragmentVisitor
    {
        /// <summary>Maps cursor name (case-insensitive) → the DECLARE node (for location).</summary>
        public Dictionary<string, DeclareCursorStatement> Declared { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Closed { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Deallocated { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public override void ExplicitVisit(DeclareCursorStatement node)
        {
            var name = node.Name?.Value;
            if (!string.IsNullOrEmpty(name))
                Declared[name] = node;

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CloseCursorStatement node)
        {
            var name = GetCursorName(node.Cursor);
            if (!string.IsNullOrEmpty(name))
                Closed.Add(name);

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeallocateCursorStatement node)
        {
            var name = GetCursorName(node.Cursor);
            if (!string.IsNullOrEmpty(name))
                Deallocated.Add(name);

            base.ExplicitVisit(node);
        }

        private static string? GetCursorName(CursorId? cursor)
        {
            // CursorId.Name is an IdentifierOrValueExpression whose .Value property
            // returns the plain identifier string (e.g. "myCur").
            return cursor?.Name?.Value;
        }
    }
}

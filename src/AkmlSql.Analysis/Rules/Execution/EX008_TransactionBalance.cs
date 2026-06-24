using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>
/// EX008 — The number of BEGIN TRANSACTION statements in the batch does not equal
/// the number of COMMIT TRANSACTION + ROLLBACK TRANSACTION statements.  An imbalanced
/// batch either leaves an uncommitted transaction open or attempts to commit/rollback
/// outside an active transaction.
///
/// <para>Note: ROLLBACK statements inside a CATCH block are treated as error-recovery
/// guards and are NOT counted toward the closer tally, because the canonical
/// TRY … COMMIT / CATCH … ROLLBACK pattern uses one ROLLBACK as a safety guard
/// that balances zero or one BEGIN depending on runtime state.</para>
/// </summary>
public sealed class Ex008TransactionBalance : IAnalysisRule
{
    public string RuleId => "EX008";
    public string Category => "Execution";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new TransactionVisitor();
        ctx.CurrentBatch.Accept(visitor);

        var beginCount = visitor.BeginCount;
        var closeCount = visitor.CommitCount + visitor.RollbackCount;

        // Balanced or no transactions at all — nothing to report.
        if (beginCount == closeCount) return [];

        // Report at the first BEGIN (or if no BEGIN, at the first COMMIT/ROLLBACK).
        var anchor = visitor.FirstBeginNode ?? visitor.FirstCloseNode;
        if (anchor == null) return [];

        var message = beginCount > closeCount
            ? $"Transaction imbalance: {beginCount} BEGIN TRANSACTION(s) but only {closeCount} COMMIT/ROLLBACK(s) — transaction may be left open"
            : $"Transaction imbalance: {closeCount} COMMIT/ROLLBACK(s) but only {beginCount} BEGIN TRANSACTION(s) — possible commit outside an active transaction";

        return
        [
            new AnalysisDiagnostic
            {
                RuleId       = "EX008",
                CategoryCode = "EX",
                Severity     = ctx.Settings.GetSeverity("EX008", DiagnosticSeverity.Warning),
                Message      = message,
                StartOffset  = anchor.StartOffset,
                EndOffset    = anchor.StartOffset + anchor.FragmentLength,
                Line         = anchor.StartLine,
                Column       = anchor.StartColumn,
                FixActions   = []
            }
        ];
    }

    private sealed class TransactionVisitor : TSqlFragmentVisitor
    {
        /// <summary>Depth counter: > 0 means we are inside a CATCH block.</summary>
        private int _catchDepth;

        public int BeginCount    { get; private set; }
        public int CommitCount   { get; private set; }
        public int RollbackCount { get; private set; }

        public TSqlStatement? FirstBeginNode { get; private set; }
        public TSqlStatement? FirstCloseNode { get; private set; }

        public override void ExplicitVisit(TryCatchStatement node)
        {
            // Walk the TRY block normally (not inside CATCH).
            node.TryStatements?.Accept(this);

            // Walk the CATCH block with elevated depth — rollbacks here are guards.
            _catchDepth++;
            node.CatchStatements?.Accept(this);
            _catchDepth--;
        }

        public override void ExplicitVisit(BeginTransactionStatement node)
        {
            BeginCount++;
            FirstBeginNode ??= node;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CommitTransactionStatement node)
        {
            CommitCount++;
            FirstCloseNode ??= node;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(RollbackTransactionStatement node)
        {
            // Rollbacks inside a CATCH block are safety guards — skip counting them
            // to avoid false positives on the canonical TRY/COMMIT + CATCH/ROLLBACK pattern.
            if (_catchDepth == 0)
            {
                RollbackCount++;
                FirstCloseNode ??= node;
            }

            base.ExplicitVisit(node);
        }
    }
}

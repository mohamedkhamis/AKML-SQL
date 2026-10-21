using AkmlSql.Core.Models.Analysis;
using AkmlSql.PostgreSql.Language;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Execution;

// Spec 042 (task T169, FR-150, FR-153): the execution rules PGEX001 to PGEX005. They are about how a script runs: inside or
// outside a transaction block, and which parts the workspace can run at all.

/// <summary>Follows BEGIN / COMMIT through the script, so a rule can ask whether a statement runs inside a transaction block.</summary>
internal static class PgTransactionScan
{
    /// <summary>Each statement with whether a transaction block is open when it starts.</summary>
    public static IEnumerable<(PgScriptStatement Statement, bool InTransaction)> Scan(PgAnalysisContext ctx)
    {
        var open = false;
        foreach (var s in ctx.Statements)
        {
            yield return (s, open);

            var t = s.Stmt?.TransactionStmt;
            if (t == null) continue;
            switch (t.Kind)
            {
                case TransactionStmtKind.TransStmtBegin:
                case TransactionStmtKind.TransStmtStart:
                    open = true;
                    break;
                case TransactionStmtKind.TransStmtCommit:
                case TransactionStmtKind.TransStmtRollback:
                case TransactionStmtKind.TransStmtPrepare:
                    open = t.Chain;      // COMMIT AND CHAIN opens the next block at once
                    break;
            }
        }
    }
}

/// <summary>PGEX001 - a command that PostgreSQL refuses inside a transaction block.</summary>
public sealed class Pgex001NotInTransaction : PgRuleBase
{
    public Pgex001NotInTransaction() : base("PGEX001", "Execution", "Statement that cannot run in a transaction block", DiagnosticSeverity.Error,
        "CREATE INDEX CONCURRENTLY, DROP INDEX CONCURRENTLY, REINDEX CONCURRENTLY, VACUUM, CREATE and DROP DATABASE, CREATE and DROP TABLESPACE, ALTER SYSTEM, "
        + "CLUSTER without a table and DISCARD ALL manage resources outside the transaction system, so the server rejects them between BEGIN and COMMIT.",
        "sql-createindex") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var (s, inTransaction) in PgTransactionScan.Scan(ctx))
        {
            if (!inTransaction || s.Stmt == null) continue;
            var what = Refused(s.Stmt);
            if (what != null) report.Statement(s, $"{what} cannot run inside a transaction block.");
        }
    }

    private static string? Refused(Node stmt)
    {
        if (stmt.IndexStmt is { Concurrent: true }) return "CREATE INDEX CONCURRENTLY";
        if (stmt.DropStmt is { Concurrent: true }) return "DROP ... CONCURRENTLY";
        if (stmt.ReindexStmt is { } reindex)
        {
            if (PgTreeFacts.Option(reindex.Params, "concurrently") != null) return "REINDEX CONCURRENTLY";
            var kind = reindex.Kind.ToString();
            if (kind.Contains("Database") || kind.Contains("System")) return "REINDEX " + (kind.Contains("Database") ? "DATABASE" : "SYSTEM");
        }
        if (stmt.VacuumStmt is { IsVacuumcmd: true }) return "VACUUM";
        if (stmt.CreatedbStmt != null) return "CREATE DATABASE";
        if (stmt.DropdbStmt != null) return "DROP DATABASE";
        if (stmt.CreateTableSpaceStmt != null) return "CREATE TABLESPACE";
        if (stmt.DropTableSpaceStmt != null) return "DROP TABLESPACE";
        if (stmt.AlterSystemStmt != null) return "ALTER SYSTEM";
        if (stmt.ClusterStmt is { Relation: null }) return "CLUSTER without a table";
        if (stmt.DiscardStmt is { Target: DiscardMode.DiscardAll }) return "DISCARD ALL";
        return null;
    }
}

/// <summary>PGEX002 - dropping or truncating with no transaction to roll back.</summary>
public sealed class Pgex002DestructiveOutsideTransaction : PgRuleBase
{
    public Pgex002DestructiveOutsideTransaction() : base("PGEX002", "Execution", "DROP or TRUNCATE outside a transaction", DiagnosticSeverity.Hint,
        "In autocommit mode each statement commits as it finishes, so a DROP or TRUNCATE cannot be undone. PostgreSQL, unlike most databases, runs DDL inside a "
        + "transaction: BEGIN; ... ROLLBACK; lets you check the effect first.",
        "sql-drop") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var (s, inTransaction) in PgTransactionScan.Scan(ctx))
        {
            if (inTransaction || s.Stmt == null) continue;
            if (s.Stmt.TruncateStmt != null) report.Statement(s, "TRUNCATE outside a transaction commits at once and cannot be undone.");
            else if (s.Stmt.DropStmt is { Concurrent: false }) report.Statement(s, "DROP outside a transaction commits at once and cannot be undone.");
        }
    }
}

/// <summary>PGEX003 - SET LOCAL outside a transaction block.</summary>
public sealed class Pgex003SetLocalOutsideTransaction : PgRuleBase
{
    public Pgex003SetLocalOutsideTransaction() : base("PGEX003", "Execution", "SET LOCAL outside a transaction", DiagnosticSeverity.Warning,
        "SET LOCAL lasts until the end of the current transaction. Outside a transaction block that is the end of the statement itself, so the setting has no effect "
        + "and the server only issues a warning.",
        "sql-set") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var (s, inTransaction) in PgTransactionScan.Scan(ctx))
        {
            if (!inTransaction && s.Stmt?.VariableSetStmt is { IsLocal: true })
                report.Statement(s, "SET LOCAL outside a transaction block has no effect.");
        }
    }
}

/// <summary>PGEX004 - a psql command in the script.</summary>
public sealed class Pgex004MetaCommand : PgRuleBase
{
    public Pgex004MetaCommand() : base("PGEX004", "Execution", "psql meta-command", DiagnosticSeverity.Warning,
        "Backslash commands (\\d, \\c, \\gexec, \\i ...) belong to the psql client; the server does not understand them. The workspace does not send them to the server "
        + "and does not run them, so what follows may not behave as in psql.",
        "app-psql") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (!s.IsMetaCommand) continue;
            foreach (var token in ctx.LexTokens)
            {
                if (token.Kind == PgTokenKind.MetaCommand && token.Start >= s.Start && token.Start < s.End)
                    report.At(token.Start, token.End, "This is a psql command; the workspace does not run it.");
            }
        }
    }
}

/// <summary>PGEX005 - COPY through the client.</summary>
public sealed class Pgex005ClientCopy : PgRuleBase
{
    public Pgex005ClientCopy() : base("PGEX005", "Execution", "COPY FROM STDIN / TO STDOUT", DiagnosticSeverity.Warning,
        "COPY ... FROM STDIN and COPY ... TO STDOUT stream the data through the client connection. A script run from the workspace cannot supply or collect that stream, "
        + "so the statement is not run; use a query, INSERT, or psql's \\copy.",
        "sql-copy") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.All<CopyStmt>().Any(c => !c.IsProgram && string.IsNullOrEmpty(c.Filename)))
                report.Statement(s, "COPY through STDIN or STDOUT is not supported in the workspace.");
        }
    }
}

using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Parser;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T059, FR-021) - the Parse command for a PostgreSQL script: a syntax check with PostgreSQL's own grammar,
/// statement by statement, without touching any server. Every statement with a syntax error is reported (a script is
/// checked whole, not up to the first mistake), each at its line and column; names and permissions are not checked, and
/// the messages say so.
/// </summary>
public static class PgParseRunner
{
    /// <summary>The hint on every syntax error: what a parse cannot tell.</summary>
    public const string NotChecked = "Object names and permissions were not checked.";

    private const int ErrorSeverity = 16;

    public static ExecuteQueryResult Run(string sql)
    {
        sql ??= string.Empty;
        var outcome = new ExecuteOutcome();
        var statements = PgStatementSplitter.Split(sql);

        if (statements.Count > ExecuteOutcome.MaxBatches)
        {
            outcome.RefuseScript(ExecuteOutcome.TooManyBatchesText(statements.Count));
            return outcome.Build(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        outcome.ReserveBatches(statements.Count);

        var errors = 0;
        var notParsed = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            outcome.BeginBatch(new SqlBatchSpan(i, statement.Start, statement.End, statement.StartLine, 1, statement.Text), 1);

            // A client command is not SQL: there is nothing for the grammar to check.
            if (statement.IsMetaCommand || PgStatementInspector.UsesClientCopy(statement.Text))
            {
                notParsed++;
                outcome.AddSystem(statement.IsMetaCommand
                    ? PgErrorHints.MetaCommandSkipped(statement.Text)
                    : PgErrorHints.ClientCopySkipped());
                outcome.SetBatchStatus(ExecuteStatus.Skipped);
                outcome.EndBatch(0);
                continue;
            }

            var parsed = PgParser.Parse(statement.Text);
            if (!parsed.Success)
            {
                errors++;
                var error = parsed.Error!;
                // The error's offset is within the statement: shift it to the script, then read line and column there.
                var (line, column) = PgPositions.ToLineColumn(sql, statement.Start + error.Offset);
                outcome.AddPostgresMessage(ExecuteMessageKind.Error, error.Message, ErrorSeverity, error.SqlState,
                    hint: PgErrorHints.Combine(PgErrorHints.GoSeparator(statement.Text), NotChecked), line: line, column: column);
            }
            outcome.EndBatch(0);
        }

        if (errors == 0)
        {
            var checkedCount = statements.Count - notParsed;
            outcome.AddSystem(checkedCount == 0
                ? "There was nothing to check. " + NotChecked
                : $"No syntax errors in {checkedCount} statement(s). {NotChecked}");
        }

        return outcome.Build(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}

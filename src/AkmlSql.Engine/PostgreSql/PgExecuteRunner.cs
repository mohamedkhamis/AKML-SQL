using System.Diagnostics;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Parser;
using AkmlSql.PostgreSql.Language;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T058, FR-010 to FR-017, research R7 to R9) - runs a script on a PostgreSQL session connection the way
/// the workspace shows it: statements in order, each its own command and its own batch, until the first one that fails.
///
/// <list type="bullet">
/// <item>There is no batch separator. The script is split on semicolons that are outside strings, comments, quoted names
/// and dollar-quoted bodies (<see cref="PgStatementSplitter"/>); a <c>GO</c> line is sent as it is and, if the server
/// rejects the statement, the error carries a hint that <c>GO</c> belongs to SQL Server.</item>
/// <item>A statement that returns rows is a result set; any other reports its command tag (<c>UPDATE 12</c>,
/// <c>CREATE TABLE</c>) as its row-count line. Notices and warnings arrive as information messages in the order the server
/// sent them and are never errors.</item>
/// <item>An error stops the script. Its SQLSTATE, detail, hint and position are kept; the position becomes a line and a
/// column of the whole script. The sets read so far stay, the statements after it are Skipped, and the reply says how many
/// ran and that they stay applied unless they were inside a transaction.</item>
/// <item>A psql meta-command or a <c>COPY</c> through the client is not sent. It stops the script as an error would,
/// because what comes after may depend on it (<c>\c</c> changes the database).</item>
/// <item>The transaction state is carried through the script by <see cref="PgTransactionTracker"/>: a driver exposes none.
/// While it is Aborted every statement fails until <c>ROLLBACK</c>, and the reply says so.</item>
/// <item>A timeout, the user's cancel or the script's own time limit stops the script; the session connection stays usable.
/// A connection that is gone is reported as Disconnected and the transaction is gone with it.</item>
/// </list>
/// </summary>
public sealed class PgExecuteRunner
{
    /// <summary>FR-015: what a stopped script leaves behind.</summary>
    public const string EarlierStatementsStayText = "Earlier statements stay applied unless they ran inside an explicit transaction.";

    /// <summary>FR-016: what an aborted transaction means.</summary>
    public const string AbortedTransactionText = "Every statement fails until ROLLBACK.";

    private const int ErrorSeverity = 16;
    private const int NoticeSeverity = 10;

    private readonly PgResultSetReader _reader = new();

    /// <param name="connection">The session's open connection; the caller holds the session gate.</param>
    /// <param name="sql">The script, exactly as the editor holds it.</param>
    /// <param name="transaction">The state the session was in before this run.</param>
    /// <param name="maxRows">Rows kept per result set (already clamped by the caller).</param>
    /// <param name="commandTimeoutSeconds">The timeout of each statement.</param>
    /// <param name="scriptLimit">How long the whole script may take; null is the statement timeout for each statement, as for SQL Server scripts.</param>
    /// <param name="cancel">The user's cancel.</param>
    /// <param name="schemaChanged">Called once, after the run, when a statement that changes the catalog succeeded (FR-043).
    /// A failure further on does not take it back: the earlier statement is applied.</param>
    /// <param name="includeProvenance">True when the client wants to edit results (FR-130): the result sets of statements that return
    /// a table's rows say where each column comes from and whether the set can be edited, or why it cannot.</param>
    /// <returns>The reply, with <see cref="ExecuteQueryResult.TransactionState"/> set; never throws for a failing statement.</returns>
    public async Task<ExecuteQueryResult> RunAsync(NpgsqlConnection connection, string sql, TransactionState transaction,
        int maxRows, int commandTimeoutSeconds, TimeSpan? scriptLimit, CancellationToken cancel, Action? schemaChanged = null,
        bool includeProvenance = false)
    {
        sql ??= string.Empty;
        var outcome = new ExecuteOutcome();
        var statements = PgStatementSplitter.Split(sql);
        var limitSpan = scriptLimit ?? ExecuteLimits.ScriptTimeLimit(commandTimeoutSeconds, statements.Count);

        if (statements.Count > ExecuteOutcome.MaxBatches)
        {
            outcome.RefuseScript(ExecuteOutcome.TooManyBatchesText(statements.Count));
            return Finish(outcome, connection, transaction);
        }
        outcome.ReserveBatches(statements.Count);

        var spans = new SqlBatchSpan[statements.Count];
        for (var i = 0; i < spans.Length; i++)
            spans[i] = new SqlBatchSpan(i, statements[i].Start, statements[i].End, statements[i].StartLine, 1, statements[i].Text);

        // The script's time limit starts once it holds the connection; reaching it stops the script as a timeout, the
        // user's cancel stays a cancellation.
        using var limit = new CancellationTokenSource(limitSpan);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, limit.Token);
        var ct = linked.Token;
        bool LimitReached() => limit.IsCancellationRequested && !cancel.IsCancellationRequested;

        void OnNotice(object sender, NpgsqlNoticeEventArgs e)
        {
            try
            {
                var notice = e.Notice;
                outcome.AddPostgresMessage(ExecuteMessageKind.Info, notice.InvariantSeverity + ": " + notice.MessageText,
                    NoticeSeverity, notice.SqlState, notice.Detail, notice.Hint);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "PgExecuteRunner: a notice could not be recorded.");
            }
        }

        var state = transaction;
        var ran = 0;
        var stopped = false;
        var changedSchema = false;
        connection.Notice += OnNotice;
        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                outcome.BeginBatch(spans[i], 1);
                var timer = Stopwatch.StartNew();
                var fate = await RunStatementAsync(connection, sql, statement, state, maxRows, commandTimeoutSeconds, outcome, includeProvenance, ct)
                    .ConfigureAwait(false);
                state = fate.State;
                outcome.EndBatch(timer.ElapsedMilliseconds);

                if (fate.Kind == Fate.Ran)
                {
                    ran++;
                    changedSchema |= PgCommandTags.ChangesSchema(fate.Tag);
                    continue;
                }

                stopped = true;
                var remaining = new ArraySegment<SqlBatchSpan>(spans, i + 1, spans.Length - i - 1).ToArray();
                switch (fate.Kind)
                {
                    case Fate.Disconnected:
                        outcome.Stop(ExecuteStatus.Disconnected);
                        outcome.SkipRemaining(remaining);
                        break;

                    case Fate.Cancelled when LimitReached():
                        outcome.AddEngineError(ExecuteOutcome.ScriptTimeLimitText(limitSpan));
                        outcome.Stop(ExecuteStatus.TimedOut);
                        outcome.SkipAfterStop(remaining);
                        break;

                    case Fate.Cancelled:
                        outcome.Stop(ExecuteStatus.Cancelled);
                        outcome.SkipAfterStop(remaining);
                        break;

                    case Fate.TimedOut:
                        outcome.AddEngineError(
                            $"The statement timed out after {commandTimeoutSeconds} seconds and was cancelled on the server. The session is still connected.");
                        outcome.Stop(ExecuteStatus.TimedOut);
                        outcome.SkipAfterStop(remaining);
                        break;

                    default:
                        // A failed or skipped statement: its message and its status are recorded; the rest do not run.
                        outcome.SkipAfterStop(remaining);
                        break;
                }
                break;
            }
        }
        finally
        {
            connection.Notice -= OnNotice;
        }

        if (changedSchema) NotifySchemaChanged(schemaChanged);

        if (stopped)
        {
            outcome.AddSystem($"{ran} of {statements.Count} statements ran.");
            if (ran > 0) outcome.AddSystem(EarlierStatementsStayText);
        }
        if (state == TransactionState.Aborted) outcome.AddSystem(AbortedTransactionText);

        return Finish(outcome, connection, state);
    }

    private enum Fate { Ran, Failed, Skipped, TimedOut, Cancelled, Disconnected }

    private readonly record struct StatementFate(Fate Kind, TransactionState State, string? Tag = null);

    // The reload runs in the background; a failure to start it must never turn a successful run into an error.
    private static void NotifySchemaChanged(Action? schemaChanged)
    {
        if (schemaChanged == null) return;
        try { schemaChanged(); }
        catch (Exception ex) { Log.Warning("PostgreSQL schema reload after a run could not start ({Type})", ex.GetType().Name); }
    }

    private async Task<StatementFate> RunStatementAsync(NpgsqlConnection connection, string script, PgStatement statement,
        TransactionState state, int maxRows, int timeoutSeconds, ExecuteOutcome outcome, bool includeProvenance, CancellationToken ct)
    {
        // Never sent: what is not SQL, or needs the client to stream data (FR-019).
        if (statement.IsMetaCommand || PgStatementInspector.UsesClientCopy(statement.Text))
        {
            var text = statement.IsMetaCommand ? PgErrorHints.MetaCommandSkipped(statement.Text) : PgErrorHints.ClientCopySkipped();
            outcome.AddPostgresMessage(ExecuteMessageKind.Error, text, severity: 0, line: statement.StartLine, column: statement.StartColumn);
            outcome.SetBatchStatus(ExecuteStatus.Skipped);
            return new StatementFate(Fate.Skipped, state);
        }

        try
        {
            await using var command = new NpgsqlCommand(statement.Text, connection)
            {
                CommandTimeout = timeoutSeconds,
                // The server formats every value as text, as psql prints it (FR-018).
                AllResultTypesAreUnknown = true,
            };

            // Where the columns come from is asked only for a statement that returns a table's rows, and only when the client
            // wants to edit: it costs one catalog round trip once the rows are read.
            var collector = includeProvenance && PgStatementInspector.ReturnsRowsOfATable(statement.Text) ? new PgProvenanceCollector() : null;
            string? tag;
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                do
                {
                    if (reader.FieldCount > 0) await _reader.ReadSetAsync(reader, maxRows, outcome, ct, collector).ConfigureAwait(false);
                }
                while (await reader.NextResultAsync(ct).ConfigureAwait(false));

                tag = ReportTag(reader, statement, outcome);
            }

            // The reader is closed, so the connection can answer the catalog question (primary key, kind of relation).
            if (collector != null) await collector.ResolveAsync(connection, ct).ConfigureAwait(false);
            return new StatementFate(Fate.Ran, PgTransactionTracker.Next(state, statement.Text, succeeded: true, sqlState: null), tag);
        }
        catch (PostgresException ex)
        {
            ReportServerError(script, statement, ex, outcome);
            var next = PgTransactionTracker.Next(state, statement.Text, succeeded: false, ex.SqlState);
            // A FATAL error ends the backend: the transaction went with it.
            return connection.State == System.Data.ConnectionState.Open
                ? new StatementFate(Fate.Failed, next)
                : new StatementFate(Fate.Disconnected, TransactionState.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new StatementFate(Fate.Cancelled, PgTransactionTracker.Next(state, statement.Text, succeeded: false, "57014"));
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            // The driver sent a cancel request to the server; the backend and the connection are still there.
            return new StatementFate(Fate.TimedOut, PgTransactionTracker.Next(state, statement.Text, succeeded: false, "57014"));
        }
        catch (Exception ex) when (ex is NpgsqlException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is gone, or was never usable. The server rolled any open transaction back.
            Log.Warning("PostgreSQL statement failed without a server error: {Type}", ex.GetType().Name);
            // No SQLSTATE and no server number: the text says it all, as for any error the engine raises itself.
            outcome.AddPostgresMessage(ExecuteMessageKind.Error, ex.Message, severity: 0, line: statement.StartLine, column: statement.StartColumn);
            return connection.State == System.Data.ConnectionState.Open
                ? new StatementFate(Fate.Failed, PgTransactionTracker.Next(state, statement.Text, succeeded: false, sqlState: null))
                : new StatementFate(Fate.Disconnected, TransactionState.None);
        }
    }

    // The statement's command tag as its row-count line (FR-012). The driver gives its kind and count, not the tag text.
#pragma warning disable CS0618 // Statements is the only per-statement kind and row count a command exposes; the batch API has no AllResultTypesAreUnknown.
    private static string? ReportTag(NpgsqlDataReader reader, PgStatement statement, ExecuteOutcome outcome)
    {
        if (reader.Statements.Count == 0) return null;

        var driverStatement = reader.Statements[0];
#pragma warning restore CS0618
        var type = PgCommandTag.TypeFromDriverName(driverStatement.StatementType.ToString());
        var rows = driverStatement.Rows > long.MaxValue ? long.MaxValue : (long)driverStatement.Rows;
        var tag = PgCommandTag.For(statement.Text, type, rows);
        if (tag.Tag.Length > 0) outcome.AddCommandTag(tag.Tag, tag.RowCount);
        return tag.Tag;
    }

    private static void ReportServerError(string script, PgStatement statement, PostgresException ex, ExecuteOutcome outcome)
    {
        // Errors with a position say where in the statement; the others point at the statement itself.
        int line = statement.StartLine, column = statement.StartColumn;
        if (ex.Position > 0)
        {
            var offset = PgPositions.ToDocumentOffset(statement, ex.Position);
            (line, column) = PgPositions.ToLineColumn(script, offset);
        }

        var hint = PgErrorHints.Combine(ex.Hint, PgErrorHints.GoSeparator(statement.Text));
        outcome.AddPostgresMessage(ExecuteMessageKind.Error, ex.MessageText, ErrorSeverity, ex.SqlState, ex.Detail, hint, line, column);
    }

    private static ExecuteQueryResult Finish(ExecuteOutcome outcome, NpgsqlConnection connection, TransactionState state)
    {
        var result = outcome.Build(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        result.TransactionState = (int)state;
        result.CurrentDatabase = DatabaseOf(connection);
        return result;
    }

    /// <summary>The connection's database, or null when it is closed (nothing to report).</summary>
    public static string? DatabaseOf(NpgsqlConnection connection)
    {
        try { return connection.State == System.Data.ConnectionState.Open ? connection.Database : null; }
        catch (InvalidOperationException) { return null; }
    }
}

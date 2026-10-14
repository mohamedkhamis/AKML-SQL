using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using Npgsql;
using NpgsqlTypes;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T140, FR-131 to FR-133, SC-014, research R11) - applies a grid's edits to a PostgreSQL table, the counterpart of
/// the SQL Server <c>ApplyChangesHandler</c> body.
///
/// <list type="bullet">
/// <item><b>All or nothing.</b> The edits run in order inside one transaction. The first failure undoes every edit before it, and the
/// results say which edit failed (with the server's own message, detail and hint) and that the others were rolled back.</item>
/// <item><b>The user's transaction is respected.</b> Outside a transaction the apply opens one and commits it. Inside one the apply runs
/// under a savepoint: a failure undoes only the apply and leaves the user's transaction open and usable; success releases the
/// savepoint and leaves the transaction open for the user to commit or roll back. An aborted transaction is refused, because every
/// statement would fail; the message says to roll back.</item>
/// <item><b>One row per edit.</b> An UPDATE or DELETE that touches anything but exactly one row fails the apply: the key did not
/// identify a single row (it changed or vanished since the grid read it).</item>
/// <item><b>The row comes back as stored.</b> An UPDATE or INSERT returns the columns the client asked for through <c>RETURNING</c>, in the
/// server's display form, so the grid shows what a trigger or a default made of the value without running the query again.</item>
/// </list>
/// Every value is a parameter cast to its column's type (<see cref="PgCrudWriteGenerator"/>); the server reads the text the grid shows.
/// </summary>
public sealed class PgApplyRunner
{
    /// <summary>What an aborted transaction means for an apply.</summary>
    public const string AbortedText =
        "The transaction is aborted, so nothing can be written until it ends. Run ROLLBACK, then apply the changes again.";

    private const string Savepoint = "akml_apply";
    private const int CommandTimeoutSeconds = 30;

    /// <param name="connection">The session's open connection; the caller holds the session gate.</param>
    /// <param name="request">The edits and the table they write to.</param>
    /// <param name="transaction">The state of the user's transaction on this session before the apply.</param>
    public async Task<ApplyChangesResult> RunAsync(NpgsqlConnection connection, ApplyChangesRequest request, TransactionState transaction,
        CancellationToken ct)
    {
        if (request.Edits.Length == 0)
            return new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = Array.Empty<CrudEditResult>() };

        if (transaction == TransactionState.Aborted)
            return new ApplyChangesResult { Status = ExecuteStatus.Error, ErrorMessage = AbortedText, Results = Array.Empty<CrudEditResult>() };

        var inUserTransaction = transaction == TransactionState.Open;
        var results = new CrudEditResult[request.Edits.Length];

        await ExecuteAsync(connection, inUserTransaction ? "SAVEPOINT " + Savepoint : "BEGIN", ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < request.Edits.Length; i++)
            {
                try
                {
                    results[i] = await ApplyOneAsync(connection, request, request.Edits[i], i, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // All or nothing: undo, then report every edit as not applied. The failed one carries the real error, the
                    // others a note, so the grid never clears a row's mark for a write that did not persist.
                    await UndoAsync(connection, inUserTransaction).ConfigureAwait(false);
                    var message = ErrorText(ex);
                    for (var j = 0; j < results.Length; j++)
                    {
                        results[j] = j == i
                            ? new CrudEditResult { Index = j, Ok = false, Error = message }
                            : new CrudEditResult { Index = j, Ok = false, Error = $"Rolled back — edit {i} failed." };
                    }
                    return new ApplyChangesResult
                    {
                        Status = ExecuteStatus.Error,
                        ErrorMessage = $"Edit {i} failed: {message}",
                        Results = results,
                    };
                }
            }

            await ExecuteAsync(connection, inUserTransaction ? "RELEASE SAVEPOINT " + Savepoint : "COMMIT", ct).ConfigureAwait(false);
            return new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = results };
        }
        catch
        {
            // A cancel, a lost connection or a failed commit: nothing was kept, and the connection must not be left inside our transaction.
            await UndoAsync(connection, inUserTransaction).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<CrudEditResult> ApplyOneAsync(NpgsqlConnection connection, ApplyChangesRequest request, CrudEditDto edit,
        int index, CancellationToken ct)
    {
        var statement = PgCrudWriteGenerator.Build(request, edit);

        await using var command = new NpgsqlCommand(statement.Sql, connection)
        {
            CommandTimeout = CommandTimeoutSeconds,
            // The returned row is read as the server prints it, the same text the grid shows for a select.
            AllResultTypesAreUnknown = true,
        };
        foreach (var parameter in statement.Parameters)
        {
            // Unknown: the server takes the type from the cast beside the placeholder, so the text is read by the column's own rules.
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = (object?)parameter.Value ?? DBNull.Value });
        }

        var affected = 0;
        string?[]? row = null;
        if (statement.ReturnsRow)
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                affected++;
                if (affected > 1) continue;     // only the first row is the answer; the count is what the one-row rule needs
                row = new string?[reader.FieldCount];
                for (var c = 0; c < row.Length; c++) row[c] = reader.IsDBNull(c) ? null : reader.GetString(c);
            }
        }
        else
        {
            affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // A keyed write must touch exactly one row. Any other count means the key did not identify a single row, and writing on
        // regardless would change several rows or silently none.
        if (affected != 1)
        {
            var verb = edit.Op switch { CrudOp.Delete => "DELETE", CrudOp.Insert => "INSERT", _ => "UPDATE" };
            throw new InvalidOperationException(
                $"This {verb} affected {affected} row(s); expected exactly 1 (the key did not identify a single row).");
        }

        return new CrudEditResult { Index = index, Ok = true, RowsAffected = 1, Row = row };
    }

    // The server's own wording: what went wrong, then which value and constraint (detail) and what to try (hint).
    private static string ErrorText(Exception ex)
    {
        if (ex is PostgresException pg)
        {
            var text = pg.MessageText;
            if (!string.IsNullOrWhiteSpace(pg.Detail)) text += " — " + pg.Detail;
            if (!string.IsNullOrWhiteSpace(pg.Hint)) text += " — " + pg.Hint;
            return text;
        }
        return ex.Message;
    }

    private static async Task UndoAsync(NpgsqlConnection connection, bool inUserTransaction)
    {
        // The token of the work that failed may be the one that is cancelled; the undo must still run.
        try
        {
            if (connection.State != System.Data.ConnectionState.Open) return;
            if (inUserTransaction)
            {
                await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT " + Savepoint, CancellationToken.None).ConfigureAwait(false);
                await ExecuteAsync(connection, "RELEASE SAVEPOINT " + Savepoint, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await ExecuteAsync(connection, "ROLLBACK", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "PgApplyRunner: the undo failed (non-fatal).");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

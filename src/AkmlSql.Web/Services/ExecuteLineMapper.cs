using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R24, FR-023) — which document line an error message points at. The server numbers
/// lines from the start of the batch it received; the engine sends each batch verbatim and says where it
/// starts (<see cref="ExecuteBatchDto.StartLine"/>), and the page knows where the selection it ran starts:
/// <c>documentLine = (selectionStartLine − 1) + (StartLine − 1) + Line</c>. An error without a line, or one
/// raised inside a procedure (its line is the procedure's own), points nowhere — clicking it does nothing.
///
/// <para>
/// Spec 042 (FR-014): a PostgreSQL error is different in one way - the engine has already turned the server's position
/// into a line and column of the whole script that was sent (it split the script itself and knows where each statement
/// starts), so <c>Line</c> is relative to the selection, not to a batch, and <c>Column</c> is set. Only the selection's
/// own start is left to add. There are no batches to add: each statement is a batch of its own.
/// </para>
/// </summary>
public static class ExecuteLineMapper
{
    /// <param name="selectionStartLine">The 1-based document line the executed text began on (1 for the
    /// whole document).</param>
    public static int? DocumentLine(int selectionStartLine, ExecuteQueryResult result, ExecuteMessageDto message)
    {
        if (message.Kind != ExecuteMessageKind.Error || message.Line <= 0)
            return null;

        int start = selectionStartLine < 1 ? 1 : selectionStartLine;

        // PostgreSQL: the line is already within the script that was sent.
        if (IsPostgres(message)) return (start - 1) + message.Line;

        if (!string.IsNullOrEmpty(message.Procedure)) return null;

        int batchStart;
        var batch = (result.Batches ?? System.Array.Empty<ExecuteBatchDto>()).FirstOrDefault(b => b.Index == message.BatchIndex);
        if (batch != null) batchStart = batch.StartLine;
        else if (message.BatchIndex == 0) batchStart = 1;   // an older engine sends no batches: one batch
        else return null;

        return (start - 1) + (batchStart - 1) + message.Line;
    }

    /// <summary>
    /// The 1-based column to put the caret at, or null for none. Only a PostgreSQL error has one. On the first line of the
    /// selection the column also moves by where the selection began on that line.
    /// </summary>
    public static int? DocumentColumn(int selectionStartColumn, ExecuteMessageDto message)
    {
        if (message.Kind != ExecuteMessageKind.Error || message.Line <= 0 || message.Column <= 0 || !IsPostgres(message))
            return null;

        int startColumn = selectionStartColumn < 1 ? 1 : selectionStartColumn;
        return message.Line == 1 ? (startColumn - 1) + message.Column : message.Column;
    }

    // Only the PostgreSQL runner sets a SQLSTATE or a column: every error it raises, its own (a statement it did not send) as
    // well as the server's, carries the position of its statement in both.
    private static bool IsPostgres(ExecuteMessageDto message) =>
        !string.IsNullOrEmpty(message.SqlState) || message.Column > 0;
}

/// <summary>Spec 042 (FR-014): a place in the document, 1-based line and column; where an error click puts the caret.</summary>
public readonly record struct DocumentPosition(int Line, int Column);

using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R24, FR-023) — which document line an error message points at. The server numbers
/// lines from the start of the batch it received; the engine sends each batch verbatim and says where it
/// starts (<see cref="ExecuteBatchDto.StartLine"/>), and the page knows where the selection it ran starts:
/// <c>documentLine = (selectionStartLine − 1) + (StartLine − 1) + Line</c>. An error without a line, or one
/// raised inside a procedure (its line is the procedure's own), points nowhere — clicking it does nothing.
/// </summary>
public static class ExecuteLineMapper
{
    /// <param name="selectionStartLine">The 1-based document line the executed text began on (1 for the
    /// whole document).</param>
    public static int? DocumentLine(int selectionStartLine, ExecuteQueryResult result, ExecuteMessageDto message)
    {
        if (message.Kind != ExecuteMessageKind.Error || message.Line <= 0 || !string.IsNullOrEmpty(message.Procedure))
            return null;

        int batchStart;
        var batch = (result.Batches ?? System.Array.Empty<ExecuteBatchDto>()).FirstOrDefault(b => b.Index == message.BatchIndex);
        if (batch != null) batchStart = batch.StartLine;
        else if (message.BatchIndex == 0) batchStart = 1;   // an older engine sends no batches: one batch
        else return null;

        int start = selectionStartLine < 1 ? 1 : selectionStartLine;
        return (start - 1) + (batchStart - 1) + message.Line;
    }
}

using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 041 (T083, research R24, FR-023) — an error message's batch-relative line, mapped back to the line of
/// the document the user ran: <c>(selectionStart − 1) + (Batches[b].StartLine − 1) + Line</c>. Errors without
/// a line, or raised inside a procedure (whose line is the procedure's own), map to nothing.
/// </summary>
public sealed class ExecuteLineMapperTests
{
    private static ExecuteQueryResult Result(params int[] batchStartLines) => new()
    {
        Batches = batchStartLines.Select((start, i) => new ExecuteBatchDto { Index = i, StartLine = start }).ToArray(),
    };

    private static ExecuteMessageDto Error(int batch, int line, string? procedure = null) =>
        new() { Kind = ExecuteMessageKind.Error, BatchIndex = batch, Line = line, Procedure = procedure };

    [Fact]
    public void A_whole_document_run_maps_the_first_batch_line_to_itself()
    {
        Assert.Equal(4, ExecuteLineMapper.DocumentLine(1, Result(1), Error(0, 4)));
    }

    [Fact]
    public void A_later_batch_adds_its_start_line()
    {
        Assert.Equal(7, ExecuteLineMapper.DocumentLine(1, Result(1, 5), Error(1, 3)));
    }

    [Fact]
    public void A_selection_that_starts_further_down_adds_its_start()
    {
        // (10 − 1) + (5 − 1) + 2 = 15
        Assert.Equal(15, ExecuteLineMapper.DocumentLine(10, Result(1, 5), Error(1, 2)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(3, "dbo.p")]
    public void No_line_or_a_procedure_maps_to_nothing(int line, string? procedure)
    {
        Assert.Null(ExecuteLineMapper.DocumentLine(1, Result(1), Error(0, line, procedure)));
    }

    [Fact]
    public void A_start_below_one_is_read_as_line_one()
    {
        Assert.Equal(2, ExecuteLineMapper.DocumentLine(0, Result(1), Error(0, 2)));
    }

    [Fact]
    public void An_older_engine_without_batches_maps_the_first_batch_only()
    {
        var older = new ExecuteQueryResult();
        Assert.Equal(3, ExecuteLineMapper.DocumentLine(1, older, Error(0, 3)));
        Assert.Null(ExecuteLineMapper.DocumentLine(1, older, Error(1, 3)));
    }

    [Fact]
    public void Only_errors_navigate()
    {
        var info = new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, Line = 3 };
        Assert.Null(ExecuteLineMapper.DocumentLine(1, Result(1), info));
    }
}

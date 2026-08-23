using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// PR #247 follow-up (live Playwright finding): the "Applied N change(s)" success message must
/// surface after a successful Apply. It used to live inside the apply-bar (<c>@if HasPendingEdits</c>),
/// which is removed the moment a successful apply clears the pending edits — so the success message
/// could never render (only an apply *failure*, which keeps the pending edits, ever showed it). The
/// message now lives in the always-present results footer.
/// </summary>
public sealed class Pr247_ResultsGridApplyMessageTests
{
    private sealed class OkApplyStub : IQueryExecutionService
    {
        public Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
            => Task.FromResult(new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Ok });
        public Task CancelAsync(string queryId, CancellationToken ct) => Task.CompletedTask;
        public Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct)
            => Task.FromResult(new ApplyChangesResult { Status = ExecuteStatus.Ok });
    }

    private static ExecuteQueryResult EditableResult()
    {
        var set = new ExecuteResultSet
        {
            ColumnNames = new[] { "id", "name" },
            ColumnSqlTypes = new[] { "int", "nvarchar" },
            Rows = new[] { new string?[] { "1", "alpha" } },
            IsEditable = true,
            BaseTable = "demo",
            Provenance = new[]
            {
                new ColumnProvenanceDto { BaseColumnName = "id", IsKey = true, IsTruePrimaryKey = true },
                new ColumnProvenanceDto { BaseColumnName = "name" },
            },
        };
        return new ExecuteQueryResult { Status = ExecuteStatus.Ok, ResultSets = new[] { set } };
    }

    [Fact]
    public void SuccessfulApply_SurfacesAppliedMessage_EvenAfterPendingEditsCleared()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IQueryExecutionService>(new OkApplyStub());
        var cut = ctx.Render<ResultsGridComponent>();

        cut.InvokeAsync(() => cut.Instance.ShowResult(EditableResult()));

        // Edit the 'name' cell (row 0, col 1): double-click opens the inline editor; change commits it.
        cut.Find("[data-testid=results-cell-0-1]").DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("ALPHA");

        // A pending edit now exists → Apply. The Ok stub clears the pending edits, so HasPendingEdits
        // becomes false and the apply-bar is removed.
        cut.Find("[data-testid=results-apply]").Click();

        // The success confirmation must still be visible (it now lives in the footer, not the apply-bar).
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Applied 1 change(s)", cut.Markup);
            Assert.NotNull(cut.Find("[data-testid=results-apply-message]"));
        });
    }
}

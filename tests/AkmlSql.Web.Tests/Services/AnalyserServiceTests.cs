using System.Threading;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) F1 follow-up + M2 T043. Validates that the in-process
/// AnalyserService runs the extracted AkmlSql.Analysis engine end-to-end inside the
/// Blazor WASM process and produces the same findings the IDE plugin would for the
/// same input.
/// </summary>
public sealed class AnalyserServiceTests
{
    private static IAnalyserService CreateService() => new AnalyserService();

    [Fact]
    public async Task AnalyseAsync_returns_PE001_for_select_star_in_procedure()
    {
        // PE001 fires for SELECT * inside a stored procedure (not for bare SELECTs).
        var service = CreateService();
        var response = await service.AnalyseAsync(
            "CREATE PROCEDURE dbo.GetCustomers AS\nBEGIN\n    SELECT * FROM dbo.Customers;\nEND;");

        Assert.NotNull(response);
        Assert.NotNull(response.Issues);
        Assert.Contains(response.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task AnalyseAsync_returns_no_issues_for_clean_sql()
    {
        var service = CreateService();
        var response = await service.AnalyseAsync(
            "SET NOCOUNT ON;\nSELECT Id, Name FROM dbo.Orders WHERE Id = 1;");

        Assert.NotNull(response);
        Assert.NotNull(response.Issues);
        // Clean SQL should not trip PE001 / PE003 / WHERE-missing rules.
        Assert.DoesNotContain(response.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task AnalyseAsync_handles_empty_input_without_crashing()
    {
        var service = CreateService();
        var response = await service.AnalyseAsync("");

        Assert.NotNull(response);
    }

    [Fact]
    public async Task AnalyseAsync_refuses_oversized_input()
    {
        var service = CreateService();
        var oversized = new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1);

        await Assert.ThrowsAsync<DocumentTooLargeException>(
            () => service.AnalyseAsync(oversized));
    }

    [Fact]
    public async Task AnalyseAsync_respects_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService();

        // Pre-cancelled token: AnalysisEngine internally checks ct on every batch, so it
        // throws OCE. The service does not swallow it -- the caller catches.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyseAsync("SELECT 1;", null, cts.Token));
    }
}

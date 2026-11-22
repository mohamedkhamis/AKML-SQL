using System.Threading.Tasks;
using AkmlSql.Engine.Schema;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 047 (WEB-011, contracts/workspace-ai.md §3) — with the connected database's cached columns, the browser's Expand
/// refactorings do their job: Expand INSERT columns writes the column list and Expand EXEC parameters names the arguments.
/// Before, the browser ran them with no schema, so they changed nothing and warned that the columns could not be resolved.
/// </summary>
public sealed class RefactorExpandWithSnapshotTests
{
    private static IRefactoringService Service() =>
        new RefactoringService(new FakeEngineBridge(), new TestFormatterService());

    private static DatabaseCache Northwind() =>
        RefactorSchema.From(RefactorSchemaTests.Snapshot("srv", "Northwind", withPhaseB: true)).Cache!;

    [Fact]
    public async Task Expand_INSERT_columns_lists_the_cached_columns()
    {
        const string sql = "insert into dbo.Region values (9,'x')";

        var preview = await Service().PreviewLightweightAsync(LightweightRefactorKind.ExpandInsertColumns, sql, schema: Northwind());

        Assert.True(preview.Changed, string.Join(" | ", preview.Warnings));
        Assert.Empty(preview.Warnings);
        var list = preview.After.Substring(0, preview.After.IndexOf("values", System.StringComparison.Ordinal));
        var id = list.IndexOf("RegionID", System.StringComparison.Ordinal);
        var description = list.IndexOf("RegionDescription", System.StringComparison.Ordinal);
        Assert.True(id > "insert into dbo.Region".Length && description > id, preview.After);
        Assert.EndsWith("values (9,'x')", preview.After);

        var applied = await Service().ApplyLightweightAsync(LightweightRefactorKind.ExpandInsertColumns, sql, schema: Northwind());
        Assert.Equal(preview.After, applied);
    }

    [Fact]
    public async Task Expand_EXEC_parameters_names_the_arguments()
    {
        var preview = await Service().PreviewLightweightAsync(
            LightweightRefactorKind.ExpandExecParameters, "exec dbo.CustOrderHist 'ALFKI'", schema: Northwind());

        Assert.Equal("exec dbo.CustOrderHist @CustomerID = 'ALFKI'", preview.After);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public async Task An_unknown_table_keeps_its_warning_for_the_preview()
    {
        var preview = await Service().PreviewLightweightAsync(
            LightweightRefactorKind.ExpandInsertColumns, "insert into dbo.Regio values (9,'x')", schema: Northwind());

        Assert.False(preview.Changed);
        Assert.Contains("Could not resolve columns for: dbo.Regio", preview.Warnings);
    }
}

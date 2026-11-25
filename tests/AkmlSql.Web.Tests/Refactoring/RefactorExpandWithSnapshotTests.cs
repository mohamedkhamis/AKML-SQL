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

    /// <summary>
    /// Review of PR #261: Northwind's dbo.Categories as the engine caches it, with the server-filled columns marked — an
    /// IDENTITY key and (for the test) a computed one.
    /// </summary>
    private static DatabaseCache Categories()
    {
        var phaseB = new AkmlSql.Core.Ipc.Messages.SchemaPhasePayload
        {
            DatabaseName = "Northwind", Phase = 2, Checksum = "PhaseB:2",
            Schemas = new[]
            {
                new AkmlSql.Core.Ipc.Messages.SchemaPhaseSchema
                {
                    Name = "dbo",
                    Objects = new[]
                    {
                        new AkmlSql.Core.Ipc.Messages.SchemaPhaseObject
                        {
                            SchemaName = "dbo", ObjectName = "Categories", ObjectType = 0,
                            Columns = new[]
                            {
                                new AkmlSql.Core.Ipc.Messages.SchemaPhaseColumn { Name = "CategoryID", TypeName = "int", IsPrimaryKey = true, IsIdentity = true },
                                new AkmlSql.Core.Ipc.Messages.SchemaPhaseColumn { Name = "CategoryName", TypeName = "nvarchar", MaxLength = 30 },
                                new AkmlSql.Core.Ipc.Messages.SchemaPhaseColumn { Name = "Description", TypeName = "ntext", IsNullable = true },
                                new AkmlSql.Core.Ipc.Messages.SchemaPhaseColumn { Name = "NameLength", TypeName = "int", IsComputed = true },
                            },
                        },
                    },
                },
            },
        };
        var snapshot = new SchemaSnapshot
        {
            ServerCanonicalIdentity = "srv",
            DatabaseName = "Northwind",
            PhaseA = MessagePack.MessagePackSerializer.Serialize(RefactorSchemaTests.PhaseAOf(phaseB)),
            PhaseB = MessagePack.MessagePackSerializer.Serialize(phaseB),
        };
        return RefactorSchema.From(snapshot).Cache!;
    }

    [Fact]
    public async Task Expand_INSERT_columns_leaves_out_the_columns_the_server_fills()
    {
        const string sql = "insert into dbo.Categories values (N'Beverages', N'Soft drinks')";

        var preview = await Service().PreviewLightweightAsync(LightweightRefactorKind.ExpandInsertColumns, sql, schema: Categories());

        Assert.True(preview.Changed, string.Join(" | ", preview.Warnings));
        var list = preview.After.Substring(0, preview.After.IndexOf("values", System.StringComparison.Ordinal));
        // The two columns the VALUES fill, and nothing else: the identity is named in a comment only, the computed one not at all.
        Assert.Contains("CategoryName,", list);
        Assert.Contains("Description ", list);
        Assert.Contains("-- CategoryID: IDENTITY", list);
        Assert.DoesNotContain("CategoryID,", list);
        Assert.DoesNotContain("NameLength", list);
    }

    [Fact]
    public async Task Expand_UPDATE_columns_never_sets_an_identity_or_computed_column()
    {
        const string sql = "update dbo.Categories set CategoryName = N'x' where CategoryID = 1";

        var preview = await Service().PreviewLightweightAsync(LightweightRefactorKind.ExpandUpdateColumns, sql, schema: Categories());

        Assert.True(preview.Changed, string.Join(" | ", preview.Warnings));
        Assert.Contains("Description = NULL", preview.After);
        Assert.DoesNotContain("CategoryID = NULL", preview.After);
        Assert.DoesNotContain("NameLength = NULL", preview.After);
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

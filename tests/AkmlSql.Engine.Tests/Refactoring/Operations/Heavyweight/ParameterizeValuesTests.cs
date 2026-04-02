using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// T067 — Unit tests for ParameterizeValuesOperation.
/// </summary>
public sealed class ParameterizeValuesTests
{
    private static ParameterizeValuesOperation Op => new();

    private static RefactorPreviewRequest MakeRequest()
    {
        return new RefactorPreviewRequest { OperationType = (int)RefactorOperationType.ParameterizeValues };
    }

    // ─── Integer literal ────────────────────────────────────────────────────

    [Fact]
    public async Task ParameterizeValues_IntegerLiteral_GeneratesDeclaration()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = 42";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        Assert.NotEmpty(response.Changes);

        // There should be a declaration change at offset 0 containing DECLARE @CustomerId int = 42
        var declChange = response.Changes.FirstOrDefault(c =>
            c.ChangeCategory == "declaration" && c.StartOffset == 0);
        Assert.NotNull(declChange);
        Assert.Contains("DECLARE @CustomerId int = 42", declChange.NewText);
    }

    // ─── String literal → nvarchar ──────────────────────────────────────────

    [Fact]
    public async Task ParameterizeValues_StringLiteral_GeneratesNvarchar()
    {
        const string sql = "SELECT * FROM dbo.Customers WHERE Name = 'Alice'";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        Assert.NotEmpty(response.Changes);

        var declChange = response.Changes.FirstOrDefault(c =>
            c.ChangeCategory == "declaration" && c.StartOffset == 0);
        Assert.NotNull(declChange);
        Assert.Contains("nvarchar(max)", declChange.NewText);
        Assert.Contains("'Alice'", declChange.NewText);
    }

    // ─── No literals → empty changes ────────────────────────────────────────

    [Fact]
    public async Task ParameterizeValues_NoLiterals_EmptyChanges()
    {
        // No WHERE / ON / HAVING clause literals
        const string sql = "SELECT OrderId, CustomerId FROM dbo.Orders";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply);
        Assert.Empty(response.Changes);
    }

    // ─── Date string literal → date ─────────────────────────────────────────

    [Fact]
    public async Task ParameterizeValues_DateStringLiteral_GeneratesDateType()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE OrderDate = '2024-01-15'";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));

        var declChange = response.Changes.FirstOrDefault(c =>
            c.ChangeCategory == "declaration" && c.StartOffset == 0);
        Assert.NotNull(declChange);
        Assert.Contains("date", declChange.NewText);
    }

    // ─── Replacement change replaces literal with variable ──────────────────

    [Fact]
    public async Task ParameterizeValues_LiteralChange_ReplacesWithVariableName()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = 42";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply);

        // There should be a rename change that replaces the literal "42" with "@CustomerId"
        var renameChange = response.Changes.FirstOrDefault(c =>
            c.ChangeCategory == "rename" && c.OldText == "42");
        Assert.NotNull(renameChange);
        Assert.StartsWith("@", renameChange.NewText);
    }
}

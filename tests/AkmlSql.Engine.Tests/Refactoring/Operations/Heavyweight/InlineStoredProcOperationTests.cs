using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 T063 — operation-level guards for InlineStoredProcOperation that fire BEFORE the live
/// definition fetch, so they need no DB. The substitution itself is covered by
/// <see cref="InlineStoredProcRewriterTests"/>.
/// </summary>
public sealed class InlineStoredProcOperationTests
{
    private static InlineStoredProcOperation Op => new();
    private static RefactorPreviewRequest MakeRequest() => new();

    [Fact]
    public async Task No_exec_statement_cannot_apply()
    {
        var ctx = LightweightOperationTestHelper.CreateContext("SELECT 1");
        var r = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.False(r.CanApply);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public async Task Dynamic_exec_string_is_not_a_procedure_call()
    {
        var ctx = LightweightOperationTestHelper.CreateContext("EXEC('SELECT 1')");
        var r = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.False(r.CanApply);
        Assert.Contains(r.Errors, e => e.Contains("stored-procedure call"));
    }

    [Fact]
    public async Task Sp_executesql_is_refused()
    {
        var ctx = LightweightOperationTestHelper.CreateContext("EXEC sp_executesql N'SELECT 1'");
        var r = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.False(r.CanApply);
        Assert.Contains(r.Errors, e => e.Contains("sp_executesql"));
    }

    [Fact]
    public async Task Return_code_capture_is_refused()
    {
        // EXEC @rc = dbo.P … captures the return code — refused before any DB fetch.
        var ctx = LightweightOperationTestHelper.CreateContext("DECLARE @rc int; EXEC @rc = dbo.usp_X @id = 1");
        ctx.SelectionStart = ctx.DocumentText.IndexOf("EXEC", System.StringComparison.Ordinal);
        var r = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.False(r.CanApply);
        Assert.Contains(r.Errors, e => e.Contains("return code"));
    }

    [Fact]
    public async Task Procedure_call_without_a_connection_is_refused()
    {
        // A real proc call, but the test context has no ConnectionString → cannot fetch the body.
        var ctx = LightweightOperationTestHelper.CreateContext("EXEC dbo.usp_X @id = 1");
        Assert.Null(ctx.ConnectionString);
        var r = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.False(r.CanApply);
        Assert.Contains(r.Errors, e => e.Contains("connection"));
    }
}

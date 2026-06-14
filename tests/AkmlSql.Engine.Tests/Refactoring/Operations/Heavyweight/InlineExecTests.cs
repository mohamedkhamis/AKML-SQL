using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// T064 — Unit tests for InlineExecOperation (FR-020).
/// Inlines a dynamic EXEC whose SQL is a string literal in the script.
/// No live DB — covers EXEC('...'), EXECUTE('...'), concatenated literals,
/// sp_executesql with literal param substitution, doubled-quote unescaping,
/// and the proc-name (non-dynamic) rejection path.
/// </summary>
public sealed class InlineExecTests
{
    private static InlineExecOperation Op => new();

    // NOTE: OperationType is intentionally left unset. PreviewAsync ignores it
    // (the enum value RefactorOperationType.InlineExec is added by the orchestrator).
    private static RefactorPreviewRequest MakeRequest() => new();

    private static RefactorChangeInfo SingleChange(RefactorPreviewResponse r)
    {
        Assert.True(r.CanApply, string.Join("; ", r.Errors));
        Assert.Single(r.Changes);
        return r.Changes[0];
    }

    // ─── EXEC('SELECT 1') -> SELECT 1 ────────────────────────────────────────

    [Fact]
    public async Task InlineExec_SimpleLiteral_UnwrapsToInnerSql()
    {
        const string sql = "EXEC('SELECT 1')";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 1", change.NewText);
        // The change span covers the whole EXEC statement.
        Assert.Equal(0, change.StartOffset);
        Assert.Equal(sql.Length, change.EndOffset);
    }

    [Fact]
    public async Task InlineExec_ExecuteKeyword_AlsoUnwraps()
    {
        const string sql = "EXECUTE ('SELECT 42 FROM dbo.T')";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 42 FROM dbo.T", change.NewText);
    }

    // ─── Concatenated literals ───────────────────────────────────────────────

    [Fact]
    public async Task InlineExec_ConcatenatedLiterals_AreJoined()
    {
        const string sql = "EXEC('SELECT ' + '1' + ' FROM dbo.T')";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 1 FROM dbo.T", change.NewText);
    }

    // ─── Doubled-quote unescaping ────────────────────────────────────────────

    [Fact]
    public async Task InlineExec_DoubledQuotes_AreUnescaped()
    {
        const string sql = "EXEC('SELECT ''x'' AS c')";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 'x' AS c", change.NewText);
    }

    // ─── sp_executesql with literal params substituted ───────────────────────

    [Fact]
    public async Task InlineExec_SpExecutesql_SubstitutesLiteralParams()
    {
        const string sql =
            "EXEC sp_executesql N'SELECT * FROM dbo.T WHERE id = @id AND nm = @name', " +
            "N'@id int, @name nvarchar(50)', @id = 5, @name = N'Alice'";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        // Inner SQL is unwrapped; @id -> 5, @name -> N'Alice' (raw value text, quotes preserved).
        Assert.Equal("SELECT * FROM dbo.T WHERE id = 5 AND nm = N'Alice'", change.NewText);
    }

    [Fact]
    public async Task InlineExec_SpExecutesql_LongAndShortParamNames_NoClobber()
    {
        // @id must not clobber @id2 — longest-name-first / word-boundary substitution.
        const string sql =
            "EXEC sp_executesql N'SELECT @id, @id2', N'@id int, @id2 int', @id = 1, @id2 = 2";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 1, 2", change.NewText);
    }

    [Fact]
    public async Task InlineExec_SpExecutesql_NoParams_JustUnwraps()
    {
        const string sql = "EXEC sp_executesql N'SELECT 1'";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        var change = SingleChange(response);
        Assert.Equal("SELECT 1", change.NewText);
    }

    [Fact]
    public async Task InlineExec_SpExecutesql_NonLiteralBinding_LeftAsParamWithWarning()
    {
        // @id binding value is a variable (non-literal) — leave @id in the inlined text and warn.
        const string sql =
            "DECLARE @v int = 7; " +
            "EXEC sp_executesql N'SELECT @id', N'@id int', @id = @v";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        // Place selection inside the EXEC statement (after the DECLARE).
        ctx.SelectionStart  = sql.IndexOf("EXEC", System.StringComparison.Ordinal);
        ctx.SelectionLength = 0;
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        Assert.Single(response.Changes);
        Assert.Equal("SELECT @id", response.Changes[0].NewText);
        Assert.NotEmpty(response.Warnings);
        Assert.Contains(response.Warnings, w => w.Contains("@id"));
    }

    // ─── Review regressions: token-aware substitution must not corrupt valid input ───

    [Fact]
    public async Task InlineExec_SpExecutesql_DoesNotReSubstituteAnInlinedValue()
    {
        // @a's inlined value contains the text "@b"; a later @b pass must NOT clobber it.
        const string sql =
            "EXEC sp_executesql N'SELECT @a, @b', N'@a nvarchar(50), @b int', @a = N'@b text', @b = 99";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.Equal("SELECT N'@b text', 99", SingleChange(response).NewText);
    }

    [Fact]
    public async Task InlineExec_SpExecutesql_DoesNotSubstituteInsideAStringLiteral()
    {
        // The template's own string literal '@id' must survive; only the bare @id is substituted.
        const string sql =
            "EXEC sp_executesql N'SELECT ''@id'' AS c, @id', N'@id int', @id = 5";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.Equal("SELECT '@id' AS c, 5", SingleChange(response).NewText);
    }

    // ─── Proc-name EXEC -> CanApply = false ──────────────────────────────────

    [Fact]
    public async Task InlineExec_StoredProcName_CannotApply()
    {
        const string sql = "EXEC dbo.usp_DoThing @a = 1";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
        Assert.Empty(response.Changes);
    }

    // ─── Non-literal dynamic SQL (EXEC(@sql)) -> CanApply = false ─────────────

    [Fact]
    public async Task InlineExec_VariableSql_CannotApply()
    {
        const string sql = "DECLARE @sql nvarchar(max) = N'SELECT 1'; EXEC(@sql)";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        ctx.SelectionStart  = sql.IndexOf("EXEC", System.StringComparison.Ordinal);
        ctx.SelectionLength = 0;
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    // ─── No EXEC statement -> CanApply = false ───────────────────────────────

    [Fact]
    public async Task InlineExec_NoExecStatement_CannotApply()
    {
        const string sql = "SELECT 1";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    // ─── Apply mirrors the sibling: rewrites the document ────────────────────

    [Fact]
    public async Task InlineExec_Apply_ReplacesExecSpan()
    {
        const string sql = "EXEC('SELECT 1')";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var preview  = await Op.PreviewAsync(MakeRequest(), ctx, default);
        Assert.True(preview.CanApply);

        var applyReq = new RefactorApplyRequest { ApprovedChanges = preview.Changes };
        // ApplyChanges in the base operates on a document string; pass the original.
        var applied  = await Op.ApplyAsync(applyReq, default);

        Assert.True(applied.Success);
        Assert.Equal(preview.Changes.Length, applied.AppliedCount);
    }
}

using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 041 (X3, N1) and PR #255 — CommandBehavior.KeyInfo makes SqlClient put <c>SET NO_BROWSETABLE ON</c> in
/// front of the batch, so a batch that must start with its CREATE or ALTER never gets it.
/// </summary>
public class WantsKeyInfoTests
{
    private static readonly ExecuteQueryHandler Handler = new(new SessionConnectionRegistry(), new SchemaCacheManager());

    [Theory]
    [InlineData("SELECT * FROM dbo.T;")]
    [InlineData("-- note\nSELECT 1;")]
    [InlineData("CREATE TABLE #t (n int); SELECT * FROM #t;")]
    [InlineData("ALTER TABLE dbo.T ADD c int; SELECT 1;")]
    public void A_batch_that_selects_gets_KeyInfo(string batch)
    {
        Assert.True(Handler.WantsKeyInfo(batch));
    }

    [Theory]
    [InlineData("CREATE VIEW dbo.v AS SELECT 1 AS n;")]
    [InlineData("ALTER VIEW dbo.v AS SELECT 1 AS n;")]
    [InlineData("CREATE OR ALTER VIEW dbo.v AS SELECT 1 AS n;")]
    [InlineData("create or alter procedure dbo.p AS SELECT 1;")]
    [InlineData("CREATE OR ALTER PROC dbo.p AS SELECT 1;")]
    [InlineData("/* header */\nCREATE OR ALTER FUNCTION dbo.f() RETURNS TABLE AS RETURN SELECT 1 AS n;")]
    [InlineData("CREATE OR ALTER TRIGGER dbo.tr ON dbo.T AFTER INSERT AS SELECT 1;")]
    [InlineData("UPDATE dbo.T SET n = 1;")]
    public void A_batch_that_must_start_with_its_CREATE_or_selects_nothing_does_not(string batch)
    {
        Assert.False(Handler.WantsKeyInfo(batch));
    }
}

using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Execution;

/// <summary>
/// Spec 030 parity closure — EX007 (unclosed cursor) and EX008 (transaction balance).
/// Each test parses real T-SQL and runs exactly one rule against it via the shared helper.
/// </summary>
public sealed class Parity_EX007_EX008Tests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // EX007 — Unclosed / undeallocated cursor
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EX007_FiresWhenCursorNeverClosed()
    {
        const string sql = """
            DECLARE myCur CURSOR FOR SELECT Id FROM dbo.T;
            OPEN myCur;
            FETCH NEXT FROM myCur;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Single(diags);
        Assert.Equal("EX007", diags[0].RuleId);
    }

    [Fact]
    public void EX007_FiresWhenCursorClosedButNotDeallocated()
    {
        const string sql = """
            DECLARE myCur CURSOR FOR SELECT Id FROM dbo.T;
            OPEN myCur;
            CLOSE myCur;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Single(diags);
        Assert.Equal("EX007", diags[0].RuleId);
    }

    [Fact]
    public void EX007_FiresWhenCursorDeallocatedButNotClosed()
    {
        const string sql = """
            DECLARE myCur CURSOR FOR SELECT Id FROM dbo.T;
            OPEN myCur;
            DEALLOCATE myCur;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Single(diags);
        Assert.Equal("EX007", diags[0].RuleId);
    }

    [Fact]
    public void EX007_DoesNotFire_WhenCursorClosedAndDeallocated()
    {
        const string sql = """
            DECLARE myCur CURSOR FOR SELECT Id FROM dbo.T;
            OPEN myCur;
            FETCH NEXT FROM myCur;
            CLOSE myCur;
            DEALLOCATE myCur;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX007_FiresForEachUnclosedCursor_WhenMultipleDeclared()
    {
        // curA is fully closed+deallocated; curB is never closed or deallocated.
        const string sql = """
            DECLARE curA CURSOR FOR SELECT 1;
            DECLARE curB CURSOR FOR SELECT 2;
            OPEN curA;
            CLOSE curA;
            DEALLOCATE curA;
            OPEN curB;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Single(diags);
        Assert.Equal("EX007", diags[0].RuleId);
    }

    [Fact]
    public void EX007_DoesNotFire_WhenNoCursorDeclared()
    {
        var diags = AnalysisEngineTestHelper.Analyze("SELECT 1 FROM dbo.T;", "EX007");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX007_IsCaseInsensitive_OnCursorName()
    {
        // Cursor declared as myCUR but closed/deallocated as mycur — should not fire.
        const string sql = """
            DECLARE myCUR CURSOR FOR SELECT Id FROM dbo.T;
            OPEN myCUR;
            CLOSE mycur;
            DEALLOCATE mycur;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX007");
        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EX008 — Transaction balance (BEGIN TRAN count != COMMIT/ROLLBACK count)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EX008_FiresWhenMoreBeginsThanCommits()
    {
        const string sql = """
            BEGIN TRANSACTION;
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            COMMIT TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Single(diags);
        Assert.Equal("EX008", diags[0].RuleId);
    }

    [Fact]
    public void EX008_FiresWhenBeginWithNoCommitOrRollback()
    {
        const string sql = """
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Single(diags);
        Assert.Equal("EX008", diags[0].RuleId);
    }

    [Fact]
    public void EX008_FiresWhenMoreCommitsThanBegins()
    {
        const string sql = """
            INSERT INTO dbo.T VALUES (1);
            COMMIT TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Single(diags);
        Assert.Equal("EX008", diags[0].RuleId);
    }

    [Fact]
    public void EX008_DoesNotFire_WhenBalanced_Commit()
    {
        const string sql = """
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            COMMIT TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX008_DoesNotFire_WhenBalanced_Rollback()
    {
        const string sql = """
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            ROLLBACK TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX008_DoesNotFire_WhenNoTransactions()
    {
        var diags = AnalysisEngineTestHelper.Analyze("SELECT 1;", "EX008");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX008_DoesNotFire_WhenTwoBeginsTwoCommits()
    {
        const string sql = """
            BEGIN TRANSACTION;
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            COMMIT TRANSACTION;
            COMMIT TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX008_DoesNotFire_WhenBeginCommitRollbackBalance()
    {
        // 2 begins, 1 commit + 1 rollback = balanced
        const string sql = """
            BEGIN TRANSACTION;
            BEGIN TRANSACTION;
            INSERT INTO dbo.T VALUES (1);
            COMMIT TRANSACTION;
            ROLLBACK TRANSACTION;
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Empty(diags);
    }

    [Fact]
    public void EX008_DoesNotFire_OnCanonicalTryCatchPattern()
    {
        // The idiomatic TRY/COMMIT + CATCH/ROLLBACK pattern must not fire.
        // The CATCH-block ROLLBACK is a safety guard, not a paired closer.
        const string sql = """
            BEGIN TRY
                BEGIN TRANSACTION;
                INSERT INTO dbo.Orders (CustomerId, OrderDate) VALUES (1, GETDATE());
                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;
                THROW;
            END CATCH
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX008");
        Assert.Empty(diags);
    }
}

using Xunit;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Tests.Schema;

public class ChangeDetectorTests
{
    // ── DetectDdlInQuery ───────────────────────────────────────────────────

    [Fact]
    public void DetectDdlInQuery_EmptyString_ReturnsFalse()
    {
        Assert.False(ChangeDetector.DetectDdlInQuery(""));
    }

    [Fact]
    public void DetectDdlInQuery_WhitespaceOnly_ReturnsFalse()
    {
        Assert.False(ChangeDetector.DetectDdlInQuery("   "));
    }

    [Fact]
    public void DetectDdlInQuery_SelectOnly_ReturnsFalse()
    {
        Assert.False(ChangeDetector.DetectDdlInQuery("SELECT * FROM dbo.Orders"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateTable_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE TABLE dbo.Orders (Id INT)"));
    }

    [Fact]
    public void DetectDdlInQuery_AlterTable_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("ALTER TABLE dbo.Orders ADD Col VARCHAR(50)"));
    }

    [Fact]
    public void DetectDdlInQuery_DropTable_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("DROP TABLE dbo.Temp"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateView_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE VIEW v AS SELECT 1"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateProcedure_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE PROCEDURE usp_Test AS BEGIN END"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateProc_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE PROC usp_Test AS BEGIN END"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateFunction_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE FUNCTION dbo.fn() RETURNS INT AS BEGIN RETURN 1 END"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateIndex_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE INDEX IX_T ON dbo.T(Col)"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateTrigger_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE TRIGGER tr ON dbo.T AFTER INSERT AS BEGIN END"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateSchema_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE SCHEMA Sales"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateType_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE TYPE dbo.MyType AS TABLE (Id INT)"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateSequence_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE SEQUENCE dbo.OrderSeq"));
    }

    [Fact]
    public void DetectDdlInQuery_CreateSynonym_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("CREATE SYNONYM dbo.T FOR other.T"));
    }

    [Fact]
    public void DetectDdlInQuery_CaseInsensitive()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("create table dbo.Orders (Id int)"));
    }

    [Fact]
    public void DetectDdlInQuery_MultilineWithDdl_ReturnsTrue()
    {
        var sql = "SELECT 1\nGO\nCREATE TABLE dbo.T (Id INT)";
        Assert.True(ChangeDetector.DetectDdlInQuery(sql));
    }

    [Fact]
    public void DetectDdlInQuery_DdlNotAtLineStart_ReturnsFalse()
    {
        // The regex requires the DDL keyword at start of line (with optional whitespace)
        // A DDL keyword buried in a comment or string should NOT match
        // But if it's at the start of a line (multiline mode) it matches
        // This tests a SELECT without DDL at line start
        Assert.False(ChangeDetector.DetectDdlInQuery("SELECT 'CREATE TABLE' FROM T"));
    }

    [Fact]
    public void DetectDdlInQuery_IndentedDdl_ReturnsTrue()
    {
        // The regex allows leading whitespace (^\s*)
        Assert.True(ChangeDetector.DetectDdlInQuery("    CREATE TABLE dbo.T (Id INT)"));
    }

    [Fact]
    public void DetectDdlInQuery_AlterProcedure_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("ALTER PROCEDURE usp_Test AS BEGIN END"));
    }

    [Fact]
    public void DetectDdlInQuery_DropView_ReturnsTrue()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("DROP VIEW dbo.MyView"));
    }

    [Fact]
    public void DetectDdlInQuery_InsertStatement_ReturnsFalse()
    {
        Assert.False(ChangeDetector.DetectDdlInQuery("INSERT INTO T VALUES (1)"));
    }

    [Fact]
    public void DetectDdlInQuery_UpdateStatement_ReturnsFalse()
    {
        Assert.False(ChangeDetector.DetectDdlInQuery("UPDATE T SET Col = 1"));
    }
}

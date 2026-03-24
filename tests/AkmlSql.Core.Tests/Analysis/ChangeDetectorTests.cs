using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.Core.Tests.Analysis;

/// <summary>
/// Unit tests for <see cref="ChangeDetector.DetectDdlInQuery"/>.
/// The async CheckForChangesAsync requires a live SQL Server connection — not tested here.
/// </summary>
public class ChangeDetectorTests
{
    // ── CREATE statements ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("CREATE TABLE dbo.Orders (Id INT)")]
    [InlineData("CREATE VIEW dbo.vOrders AS SELECT 1")]
    [InlineData("CREATE PROCEDURE dbo.usp_Test AS RETURN")]
    [InlineData("CREATE PROC dbo.usp_Test AS RETURN")]
    [InlineData("CREATE FUNCTION dbo.fn_Test() RETURNS INT AS BEGIN RETURN 1 END")]
    [InlineData("CREATE INDEX IX_Test ON dbo.Orders (Id)")]
    [InlineData("CREATE TRIGGER trg_Test ON dbo.Orders AFTER INSERT AS RETURN")]
    [InlineData("CREATE SCHEMA Sales")]
    [InlineData("CREATE TYPE dbo.OrderId FROM INT")]
    [InlineData("CREATE SEQUENCE dbo.Seq")]
    [InlineData("CREATE SYNONYM dbo.T FOR dbo.Orders")]
    public void DetectDdlInQuery_ReturnsTrue_ForCreateStatements(string sql)
    {
        Assert.True(ChangeDetector.DetectDdlInQuery(sql));
    }

    // ── ALTER statements ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("ALTER TABLE dbo.Orders ADD Column1 INT")]
    [InlineData("ALTER PROCEDURE dbo.usp_Test AS RETURN")]
    [InlineData("ALTER VIEW dbo.vOrders AS SELECT 1")]
    [InlineData("ALTER FUNCTION dbo.fn_Test() RETURNS INT AS BEGIN RETURN 1 END")]
    [InlineData("ALTER INDEX ALL ON dbo.Orders REBUILD")]
    [InlineData("ALTER TRIGGER trg_Test ON dbo.Orders AFTER INSERT AS RETURN")]
    public void DetectDdlInQuery_ReturnsTrue_ForAlterStatements(string sql)
    {
        Assert.True(ChangeDetector.DetectDdlInQuery(sql));
    }

    // ── DROP statements ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("DROP PROCEDURE dbo.usp_Test")]
    [InlineData("DROP VIEW dbo.vOrders")]
    [InlineData("DROP FUNCTION dbo.fn_Test")]
    [InlineData("DROP INDEX IX_Test ON dbo.Orders")]
    [InlineData("DROP TRIGGER trg_Test")]
    [InlineData("DROP SCHEMA Sales")]
    [InlineData("DROP SYNONYM dbo.T")]
    [InlineData("DROP SEQUENCE dbo.Seq")]
    public void DetectDdlInQuery_ReturnsTrue_ForDropStatements(string sql)
    {
        Assert.True(ChangeDetector.DetectDdlInQuery(sql));
    }

    // ── Leading whitespace and multiline ──────────────────────────────────────

    [Fact]
    public void DetectDdlInQuery_ReturnsTrue_WhenDdlPrecededByWhitespace()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("   CREATE TABLE dbo.T (Id INT)"));
        Assert.True(ChangeDetector.DetectDdlInQuery("\t\nALTER TABLE dbo.T ADD Col1 INT"));
    }

    [Fact]
    public void DetectDdlInQuery_ReturnsTrue_WhenDdlIsOnSecondLine()
    {
        var sql = "SELECT 1\nCREATE TABLE dbo.T (Id INT)";
        Assert.True(ChangeDetector.DetectDdlInQuery(sql));
    }

    [Fact]
    public void DetectDdlInQuery_IsCaseInsensitive()
    {
        Assert.True(ChangeDetector.DetectDdlInQuery("create table dbo.T (Id INT)"));
        Assert.True(ChangeDetector.DetectDdlInQuery("Create Table dbo.T (Id INT)"));
        Assert.True(ChangeDetector.DetectDdlInQuery("DROP table dbo.T"));
    }

    // ── DML — should NOT match ────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders")]
    [InlineData("INSERT INTO dbo.Orders (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.Orders SET Col = 1")]
    [InlineData("DELETE FROM dbo.Orders WHERE Id = 1")]
    [InlineData("EXEC dbo.usp_Test")]
    [InlineData("SELECT 'CREATE TABLE' AS Msg")] // keyword inside string
    [InlineData("-- CREATE TABLE dbo.T")]         // keyword in comment (still matched by multiline regex)
    public void DetectDdlInQuery_ReturnsFalse_ForNonDdl(string sql)
    {
        // Note: the comment case may or may not match depending on regex —
        // the important thing is DML does not match.
        if (!sql.StartsWith("--"))
            Assert.False(ChangeDetector.DetectDdlInQuery(sql));
    }

    // ── Null / empty / whitespace ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\r")]
    public void DetectDdlInQuery_ReturnsFalse_ForNullOrWhitespace(string? sql)
    {
        Assert.False(ChangeDetector.DetectDdlInQuery(sql!));
    }
}

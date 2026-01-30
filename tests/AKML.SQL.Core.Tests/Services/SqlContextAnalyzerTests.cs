using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for SqlContextAnalyzer.
/// Covers Story 5.1: Context-Aware IntelliSense.
/// </summary>
public class SqlContextAnalyzerTests
{
    private readonly SqlContextAnalyzer _analyzer;

    public SqlContextAnalyzerTests()
    {
        var loggerMock = new Mock<ILogger<SqlContextAnalyzer>>();
        _analyzer = new SqlContextAnalyzer(loggerMock.Object);
    }

    #region Context Detection Tests

    [Fact]
    public void AnalyzeContext_AfterSelect_ReturnsAfterSelectContext()
    {
        // Arrange
        var sql = "SELECT ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterSelect);
    }

    [Fact]
    public void AnalyzeContext_AfterFrom_ReturnsAfterFromContext()
    {
        // Arrange
        var sql = "SELECT * FROM ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterFrom);
    }

    [Fact]
    public void AnalyzeContext_AfterWhere_ReturnsAfterWhereContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers WHERE ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterWhere);
    }

    [Fact]
    public void AnalyzeContext_AfterJoin_ReturnsAfterJoinContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c INNER JOIN ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterJoin);
    }

    [Fact]
    public void AnalyzeContext_AfterDot_ReturnsAfterDotContext()
    {
        // Arrange
        var sql = "SELECT c.";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterDot);
    }

    [Fact]
    public void AnalyzeContext_AfterOn_ReturnsAfterOnContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c INNER JOIN Orders o ON ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterOn);
    }

    [Fact]
    public void AnalyzeContext_AfterOrderBy_ReturnsAfterOrderByContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers ORDER BY ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterOrderBy);
    }

    [Fact]
    public void AnalyzeContext_AfterGroupBy_ReturnsAfterGroupByContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers GROUP BY ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterGroupBy);
    }

    [Fact]
    public void AnalyzeContext_AfterAnd_ReturnsAfterConditionContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers WHERE Id = 1 AND ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterCondition);
    }

    [Fact]
    public void AnalyzeContext_AfterExec_ReturnsAfterExecContext()
    {
        // Arrange
        var sql = "EXEC ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterExec);
    }

    [Fact]
    public void AnalyzeContext_EmptyString_ReturnsGeneralContext()
    {
        // Arrange
        var sql = "";
        var offset = 0;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.General);
    }

    #endregion

    #region Filter Text Extraction Tests

    [Fact]
    public void AnalyzeContext_WithPartialWord_ExtractsFilterText()
    {
        // Arrange
        var sql = "SELECT Cust";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.FilterText.Should().Be("Cust");
    }

    [Fact]
    public void AnalyzeContext_AfterSpace_ReturnsEmptyFilterText()
    {
        // Arrange
        var sql = "SELECT ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.FilterText.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeContext_WithUnderscore_IncludesUnderscoreInFilterText()
    {
        // Arrange
        var sql = "SELECT Customer_Name";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.FilterText.Should().Be("Customer_Name");
    }

    #endregion

    #region Table Alias Extraction Tests

    [Fact]
    public void GetTableAliases_SimpleAlias_ReturnsAlias()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c";
        var offset = sql.Length;

        // Act
        var aliases = _analyzer.GetTableAliases(sql, offset);

        // Assert
        aliases.Should().ContainKey("c");
        aliases["c"].TableName.Should().Be("Customers");
    }

    [Fact]
    public void GetTableAliases_MultipleAliases_ReturnsAllAliases()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c INNER JOIN Orders o ON c.Id = o.CustomerId";
        var offset = sql.Length;

        // Act
        var aliases = _analyzer.GetTableAliases(sql, offset);

        // Assert
        aliases.Should().HaveCount(2);
        aliases.Should().ContainKey("c");
        aliases.Should().ContainKey("o");
        aliases["c"].TableName.Should().Be("Customers");
        aliases["o"].TableName.Should().Be("Orders");
    }

    [Fact]
    public void GetTableAliases_NoAlias_UsesTableNameAsAlias()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";
        var offset = sql.Length;

        // Act
        var aliases = _analyzer.GetTableAliases(sql, offset);

        // Assert
        aliases.Should().ContainKey("Customers");
        aliases["Customers"].TableName.Should().Be("Customers");
    }

    [Fact]
    public void GetTableAliases_SchemaQualified_ExtractsCorrectly()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Customers c";
        var offset = sql.Length;

        // Act
        var aliases = _analyzer.GetTableAliases(sql, offset);

        // Assert
        aliases.Should().ContainKey("c");
        aliases["c"].SchemaName.Should().Be("dbo");
        aliases["c"].TableName.Should().Be("Customers");
    }

    #endregion

    #region Dot Notation Tests

    [Fact]
    public void AnalyzeContext_AfterDot_ExtractsTablePrefix()
    {
        // Arrange
        var sql = "SELECT c.";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterDot);
        context.TableOrAliasPrefix.Should().Be("c");
    }

    [Fact]
    public void AnalyzeContext_AfterDotWithTableName_ExtractsTablePrefix()
    {
        // Arrange
        var sql = "SELECT Customers.";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterDot);
        context.TableOrAliasPrefix.Should().Be("Customers");
    }

    #endregion

    #region Statement Detection Tests

    [Fact]
    public void GetStatementAtOffset_SelectStatement_ReturnsSelectType()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";
        var offset = 5;

        // Act
        var statement = _analyzer.GetStatementAtOffset(sql, offset);

        // Assert
        statement.Should().NotBeNull();
        statement!.Type.Should().Be(StatementType.Select);
    }

    [Fact]
    public void GetStatementAtOffset_InsertStatement_ReturnsInsertType()
    {
        // Arrange
        var sql = "INSERT INTO Customers (Name) VALUES ('Test')";
        var offset = 10;

        // Act
        var statement = _analyzer.GetStatementAtOffset(sql, offset);

        // Assert
        statement.Should().NotBeNull();
        statement!.Type.Should().Be(StatementType.Insert);
    }

    [Fact]
    public void GetStatementAtOffset_UpdateStatement_ReturnsUpdateType()
    {
        // Arrange
        var sql = "UPDATE Customers SET Name = 'Test'";
        var offset = 10;

        // Act
        var statement = _analyzer.GetStatementAtOffset(sql, offset);

        // Assert
        statement.Should().NotBeNull();
        statement!.Type.Should().Be(StatementType.Update);
    }

    [Fact]
    public void GetStatementAtOffset_DeleteStatement_ReturnsDeleteType()
    {
        // Arrange
        var sql = "DELETE FROM Customers WHERE Id = 1";
        var offset = 10;

        // Act
        var statement = _analyzer.GetStatementAtOffset(sql, offset);

        // Assert
        statement.Should().NotBeNull();
        statement!.Type.Should().Be(StatementType.Delete);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void AnalyzeContext_NullSql_ReturnsGeneralContext()
    {
        // Act
        var context = _analyzer.AnalyzeContext(null!, 0);

        // Assert
        context.ContextType.Should().Be(SqlContextType.General);
    }

    [Fact]
    public void AnalyzeContext_NegativeOffset_ReturnsGeneralContext()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";

        // Act
        var context = _analyzer.AnalyzeContext(sql, -1);

        // Assert
        context.ContextType.Should().Be(SqlContextType.General);
    }

    [Fact]
    public void AnalyzeContext_OffsetBeyondLength_HandlesGracefully()
    {
        // Arrange
        var sql = "SELECT";
        var offset = 100;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeContext_CaseInsensitive_DetectsKeywords()
    {
        // Arrange
        var sql = "select * from ";
        var offset = sql.Length;

        // Act
        var context = _analyzer.AnalyzeContext(sql, offset);

        // Assert
        context.ContextType.Should().Be(SqlContextType.AfterFrom);
    }

    #endregion
}

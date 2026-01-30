using AKML.SQL.Core.Services;
using AKML.SQL.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests;

public class SqlParserServiceTests
{
    private readonly SqlParserService _sut;
    private readonly Mock<ILogger<SqlParserService>> _loggerMock;

    public SqlParserServiceTests()
    {
        _loggerMock = new Mock<ILogger<SqlParserService>>();
        _sut = new SqlParserService(_loggerMock.Object);
    }

    [Fact]
    public async Task ParseAsync_WithValidSelect_ReturnsSuccessWithStatement()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Statements.Should().ContainSingle();
        result.Statements[0].Type.Should().Be(SqlStatementType.Select);
    }

    [Fact]
    public async Task ParseAsync_WithInvalidSql_ReturnsErrors()
    {
        // Arrange
        var sql = "SELECT * FORM Users"; // Typo: FORM instead of FROM

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ParseAsync_WithMultipleStatements_ReturnsAllStatements()
    {
        // Arrange
        var sql = @"
            SELECT * FROM Users;
            INSERT INTO Logs (Message) VALUES ('Test');
            UPDATE Users SET Name = 'John' WHERE Id = 1;
        ";

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Statements.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyString_ReturnsSuccess()
    {
        // Arrange
        var sql = "";

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_WithNull_ReturnsSuccess()
    {
        // Arrange
        string? sql = null;

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_ExtractsTableReferences()
    {
        // Arrange
        var sql = "SELECT u.Name, o.Total FROM dbo.Users u INNER JOIN dbo.Orders o ON u.Id = o.UserId";

        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Success.Should().BeTrue();
        result.Statements[0].Tables.Should().HaveCount(2);
        result.Statements[0].Tables.Should().Contain(t => t.TableName == "Users" && t.Alias == "u");
        result.Statements[0].Tables.Should().Contain(t => t.TableName == "Orders" && t.Alias == "o");
    }

    [Fact]
    public async Task FormatAsync_WithValidSql_ReturnsFormattedSql()
    {
        // Arrange
        var sql = "select * from users where id=1";

        // Act
        var result = await _sut.FormatAsync(sql, null);

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("FROM");
        result.Should().Contain("WHERE");
    }

    [Fact]
    public async Task FormatAsync_WithUppercaseCasing_ReturnsUppercaseKeywords()
    {
        // Arrange
        var sql = "select name from users";
        var options = new FormatOptions
        {
            KeywordCasing = KeywordCasing.Uppercase
        };

        // Act
        var result = await _sut.FormatAsync(sql, options);

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("FROM");
    }

    [Theory]
    [InlineData("SELECT 1", SqlStatementType.Select)]  // SELECT needs at least a column
    [InlineData("INSERT INTO t (c) VALUES (1)", SqlStatementType.Insert)]
    [InlineData("UPDATE t SET c = 1", SqlStatementType.Update)]
    [InlineData("DELETE FROM t", SqlStatementType.Delete)]
    [InlineData("CREATE TABLE t (c INT)", SqlStatementType.Create)]
    public async Task ParseAsync_DetectsStatementType(string sql, SqlStatementType expectedType)
    {
        // Act
        var result = await _sut.ParseAsync(sql);

        // Assert
        result.Statements.Should().ContainSingle();
        result.Statements[0].Type.Should().Be(expectedType);
    }
}

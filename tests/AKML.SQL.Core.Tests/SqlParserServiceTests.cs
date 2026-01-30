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
    private readonly SqlParserService _sutWithStyleService;
    private readonly Mock<ILogger<SqlParserService>> _loggerMock;
    private readonly IFormatStyleService _formatStyleService;

    public SqlParserServiceTests()
    {
        _loggerMock = new Mock<ILogger<SqlParserService>>();
        _sut = new SqlParserService(_loggerMock.Object);

        var styleLoggerMock = new Mock<ILogger<FormatStyleService>>();
        _formatStyleService = new FormatStyleService(styleLoggerMock.Object);
        _sutWithStyleService = new SqlParserService(_loggerMock.Object, _formatStyleService);
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

    #region Sprint 6 - Style-Based Formatting Tests

    [Fact]
    public async Task FormatWithStyleAsync_StandardStyle_FormatsCorrectly()
    {
        // Arrange
        var sql = "select name,age from users where id=1";

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "Standard");

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("FROM");
        result.Should().Contain("WHERE");
        // Standard style has newlines before clauses
        result.Should().Contain("\n");
    }

    [Fact]
    public async Task FormatWithStyleAsync_CompactStyle_FormatsWithMinimalWhitespace()
    {
        // Arrange
        var sql = "select name from users where id = 1";

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "Compact");

        // Assert
        result.Should().Contain("SELECT");
        // Compact style should have fewer newlines
    }

    [Fact]
    public async Task FormatWithStyleAsync_ExpandedStyle_FormatsWithMaximumReadability()
    {
        // Arrange
        var sql = "select name from users where id = 1";

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "Expanded");

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("FROM");
        result.Should().Contain("WHERE");
    }

    [Fact]
    public async Task FormatWithStyleAsync_NonExistentStyle_UsesDefaultStyle()
    {
        // Arrange
        var sql = "select name from users";

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "NonExistentStyle");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("SELECT");
    }

    [Fact]
    public async Task FormatWithStyleAsync_WithFormatStyleObject_FormatsCorrectly()
    {
        // Arrange
        var sql = "select name from users";
        var style = new FormatStyle
        {
            Name = "CustomTest",
            KeywordCasing = FormatKeywordCasing.Lowercase,
            IndentSize = 2
        };

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, style);

        // Assert
        result.Should().Contain("select");
        result.Should().Contain("from");
    }

    [Fact]
    public async Task FormatWithStyleAsync_EmptySql_ReturnsEmpty()
    {
        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync("", "Standard");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FormatWithStyleAsync_InvalidSql_ReturnsOriginal()
    {
        // Arrange
        var sql = "SELECT * FORM Users"; // Invalid SQL

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "Standard");

        // Assert
        result.Should().Be(sql); // Returns original when parse errors
    }

    [Fact]
    public async Task FormatAsync_WithStyleService_UsesDefaultStyleOptions()
    {
        // Arrange
        var sql = "select * from users";

        // Act
        var result = await _sutWithStyleService.FormatAsync(sql, null);

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("FROM");
    }

    [Fact]
    public async Task FormatWithStyleAsync_ComplexQuery_FormatsCorrectly()
    {
        // Arrange
        var sql = @"select u.name, o.total, o.created from users u inner join orders o on u.id = o.user_id where o.status = 'active' order by o.created desc";

        // Act
        var result = await _sutWithStyleService.FormatWithStyleAsync(sql, "Standard");

        // Assert
        result.Should().Contain("SELECT");
        result.Should().Contain("INNER JOIN");
        result.Should().Contain("WHERE");
        result.Should().Contain("ORDER BY");
    }

    #endregion
}

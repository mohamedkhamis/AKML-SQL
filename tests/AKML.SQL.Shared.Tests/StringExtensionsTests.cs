using AKML.SQL.Shared.Extensions;
using FluentAssertions;
using Xunit;

namespace AKML.SQL.Shared.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("Hello World", 6, "World")]
    [InlineData("SELECT * FROM Users", 7, "*")]
    [InlineData("dbo.Users", 4, "Users")]
    [InlineData("@ParameterName", 5, "@ParameterName")]
    public void GetWordAt_ReturnsCorrectWord(string text, int offset, string expected)
    {
        // Act
        var result = text.GetWordAt(offset);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("SELECT ", 7, "SELECT")]
    [InlineData("FROM Users", 5, "FROM")]
    [InlineData("dbo.Users", 9, "Users")]
    public void GetWordBefore_ReturnsCorrectWord(string text, int offset, string expected)
    {
        // Act
        var result = text.GetWordBefore(offset);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Line1\nLine2", 6, 1, 0)]
    [InlineData("Line1\nLine2", 0, 0, 0)]
    [InlineData("Line1\nLine2\nLine3", 12, 2, 0)]
    public void OffsetToLineColumn_ReturnsCorrectPosition(string text, int offset, int expectedLine, int expectedColumn)
    {
        // Act
        var (line, column) = text.OffsetToLineColumn(offset);

        // Assert
        line.Should().Be(expectedLine);
        column.Should().Be(expectedColumn);
    }

    [Theory]
    [InlineData("Line1\nLine2", 0, 0, 0)]
    [InlineData("Line1\nLine2", 1, 0, 6)]
    [InlineData("Line1\nLine2\nLine3", 2, 0, 12)]
    public void LineColumnToOffset_ReturnsCorrectOffset(string text, int line, int column, int expectedOffset)
    {
        // Act
        var result = text.LineColumnToOffset(line, column);

        // Assert
        result.Should().Be(expectedOffset);
    }

    [Theory]
    [InlineData("SELECT", "SELECT", 100)] // Exact match
    [InlineData("SELECT", "SEL", 90)]     // Starts with (high score)
    [InlineData("SELECT", "ECT", 70)]     // Contains (medium score)
    [InlineData("SELECT", "XYZ", 0)]      // No match
    public void FuzzyMatchScore_ReturnsExpectedScoreRange(string text, string pattern, int minExpectedScore)
    {
        // Act
        var result = text.FuzzyMatchScore(pattern);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(minExpectedScore - 10); // Allow some tolerance
    }

    [Fact]
    public void FuzzyMatchScore_IsCaseInsensitiveByDefault()
    {
        // Arrange
        var text = "SELECT";

        // Act
        var lowerScore = text.FuzzyMatchScore("select");
        var upperScore = text.FuzzyMatchScore("SELECT");

        // Assert
        lowerScore.Should().Be(upperScore);
    }

    [Fact]
    public void FuzzyMatchScore_CaseSensitiveWhenSpecified()
    {
        // Arrange
        var text = "SELECT";

        // Act
        var lowerScore = text.FuzzyMatchScore("select", caseSensitive: true);
        var upperScore = text.FuzzyMatchScore("SELECT", caseSensitive: true);

        // Assert
        upperScore.Should().BeGreaterThan(lowerScore);
    }

    [Theory]
    [InlineData("Hello World", 5, "Hello")]
    [InlineData("Hello World", 20, "Hello World")]
    [InlineData("Hi", 10, "Hi")]
    public void Truncate_ReturnsCorrectLength(string text, int maxLength, string expected)
    {
        // Act
        var result = text.Truncate(maxLength);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeSqlWhitespace_RemovesExtraSpaces()
    {
        // Arrange
        var sql = "SELECT  *   FROM    Users";

        // Act
        var result = sql.NormalizeSqlWhitespace();

        // Assert
        result.Should().Be("SELECT * FROM Users");
    }

    [Fact]
    public void NormalizeSqlWhitespace_HandlesNewlines()
    {
        // Arrange
        var sql = "SELECT *\n\nFROM\n\tUsers";

        // Act
        var result = sql.NormalizeSqlWhitespace();

        // Assert
        result.Should().Be("SELECT * FROM Users");
    }
}

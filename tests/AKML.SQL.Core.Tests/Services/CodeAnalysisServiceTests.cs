using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for CodeAnalysisService.
/// Covers Story 9.2: Code Analysis.
/// </summary>
public class CodeAnalysisServiceTests
{
    private readonly CodeAnalysisService _sut;

    public CodeAnalysisServiceTests()
    {
        var loggerMock = new Mock<ILogger<CodeAnalysisService>>();
        _sut = new CodeAnalysisService(loggerMock.Object);
    }

    #region GetAvailableRules Tests

    [Fact]
    public void GetAvailableRules_ReturnsAllRules()
    {
        // Act
        var rules = _sut.GetAvailableRules();

        // Assert
        rules.Should().NotBeEmpty();
        rules.Should().Contain(r => r.RuleId == "PERF001");
        rules.Should().Contain(r => r.RuleId == "SEC001");
        rules.Should().Contain(r => r.RuleId == "BP001");
    }

    [Fact]
    public void GetAvailableRules_HasExpectedRuleCount()
    {
        // Act
        var rules = _sut.GetAvailableRules();

        // Assert
        rules.Count.Should().Be(10);
    }

    [Fact]
    public void GetAvailableRules_RulesHaveRequiredProperties()
    {
        // Act
        var rules = _sut.GetAvailableRules();

        // Assert
        rules.Should().AllSatisfy(r =>
        {
            r.RuleId.Should().NotBeNullOrEmpty();
            r.Name.Should().NotBeNullOrEmpty();
            r.Description.Should().NotBeNullOrEmpty();
        });
    }

    #endregion

    #region Analyze Empty/Null Input Tests

    [Fact]
    public void Analyze_NullSql_ReturnsEmptyResult()
    {
        // Act
        var result = _sut.Analyze(null!);

        // Assert
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_EmptySql_ReturnsEmptyResult()
    {
        // Act
        var result = _sut.Analyze("");

        // Assert
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_WhitespaceSql_ReturnsEmptyResult()
    {
        // Act
        var result = _sut.Analyze("   ");

        // Assert
        result.Issues.Should().BeEmpty();
    }

    #endregion

    #region PERF001 - SELECT * Tests

    [Fact]
    public void Analyze_SelectStar_ReturnsWarning()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PERF001");
        result.Issues.First(i => i.RuleId == "PERF001").Severity.Should().Be(IssueSeverity.Warning);
        result.Issues.First(i => i.RuleId == "PERF001").Category.Should().Be(IssueCategory.Performance);
    }

    [Fact]
    public void Analyze_SelectSpecificColumns_NoSelectStarWarning()
    {
        // Arrange
        var sql = "SELECT Id, Name FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "PERF001");
    }

    [Fact]
    public void Analyze_SelectStarWithAlias_ReturnsWarning()
    {
        // Arrange
        var sql = "SELECT u.* FROM Users u";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PERF001");
    }

    #endregion

    #region SEC001 - Missing WHERE Clause Tests

    [Fact]
    public void Analyze_UpdateWithoutWhere_ReturnsWarning()
    {
        // Arrange
        var sql = "UPDATE Users SET Status = 'Inactive'";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "SEC001");
        result.Issues.First(i => i.RuleId == "SEC001").Message.Should().Contain("UPDATE");
    }

    [Fact]
    public void Analyze_DeleteWithoutWhere_ReturnsWarning()
    {
        // Arrange
        var sql = "DELETE FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "SEC001");
        result.Issues.First(i => i.RuleId == "SEC001").Message.Should().Contain("DELETE");
    }

    [Fact]
    public void Analyze_UpdateWithWhere_NoWarning()
    {
        // Arrange
        var sql = "UPDATE Users SET Status = 'Inactive' WHERE Id = 1";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "SEC001");
    }

    [Fact]
    public void Analyze_DeleteWithWhere_NoWarning()
    {
        // Arrange
        var sql = "DELETE FROM Users WHERE Status = 'Deleted'";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "SEC001");
    }

    #endregion

    #region BP001 - TOP Without ORDER BY Tests

    [Fact]
    public void Analyze_TopWithoutOrderBy_ReturnsWarning()
    {
        // Arrange
        var sql = "SELECT TOP 10 * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "BP001");
        result.Issues.First(i => i.RuleId == "BP001").Category.Should().Be(IssueCategory.BestPractice);
    }

    [Fact]
    public void Analyze_TopWithOrderBy_NoWarning()
    {
        // Arrange
        var sql = "SELECT TOP 10 * FROM Users ORDER BY CreatedDate DESC";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "BP001");
    }

    #endregion

    #region BP002 - NOLOCK Hint Tests

    [Fact]
    public void Analyze_NoLockHint_ReturnsInfo()
    {
        // Arrange
        var sql = "SELECT * FROM Users WITH (NOLOCK)";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "BP002");
        result.Issues.First(i => i.RuleId == "BP002").Severity.Should().Be(IssueSeverity.Info);
    }

    [Fact]
    public void Analyze_NoHint_NoNolockWarning()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "BP002");
    }

    #endregion

    #region PERF004 - Large IN Clause Tests

    [Fact]
    public void Analyze_LargeInClause_ReturnsWarning()
    {
        // Arrange - IN clause with more than 20 values
        var values = string.Join(", ", Enumerable.Range(1, 25));
        var sql = $"SELECT * FROM Users WHERE Id IN ({values})";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PERF004");
        result.Issues.First(i => i.RuleId == "PERF004").Message.Should().Contain("25");
    }

    [Fact]
    public void Analyze_SmallInClause_NoWarning()
    {
        // Arrange - IN clause with 5 values
        var sql = "SELECT * FROM Users WHERE Id IN (1, 2, 3, 4, 5)";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "PERF004");
    }

    #endregion

    #region PERF006 - Cartesian Join Tests

    [Fact]
    public void Analyze_CrossJoin_ReturnsWarning()
    {
        // Arrange
        var sql = "SELECT * FROM Users CROSS JOIN Roles";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PERF006");
        result.Issues.First(i => i.RuleId == "PERF006").Message.Should().Contain("CROSS JOIN");
    }

    [Fact]
    public void Analyze_InnerJoin_NoCrossJoinWarning()
    {
        // Arrange
        var sql = "SELECT * FROM Users u INNER JOIN Roles r ON u.RoleId = r.Id";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "PERF006");
    }

    #endregion

    #region Parse Error Tests

    [Fact]
    public void Analyze_InvalidSql_ReturnsParseError()
    {
        // Arrange
        var sql = "SELEC * FRM Users"; // Invalid syntax

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PARSE001");
        result.Issues.First(i => i.RuleId == "PARSE001").Severity.Should().Be(IssueSeverity.Error);
        result.Issues.First(i => i.RuleId == "PARSE001").Category.Should().Be(IssueCategory.Syntax);
    }

    [Fact]
    public void Analyze_ValidSql_NoParseError()
    {
        // Arrange
        var sql = "SELECT 1";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "PARSE001");
    }

    #endregion

    #region Analysis Options Tests

    [Fact]
    public void Analyze_DisabledRule_DoesNotReportIssue()
    {
        // Arrange
        var sql = "SELECT * FROM Users";
        var options = new AnalysisOptions
        {
            DisabledRules = new HashSet<string> { "PERF001" }
        };

        // Act
        var result = _sut.Analyze(sql, options);

        // Assert
        result.Issues.Should().NotContain(i => i.RuleId == "PERF001");
    }

    [Fact]
    public void Analyze_EnabledCategoriesOnly_FiltersRules()
    {
        // Arrange
        var sql = @"
            SELECT * FROM Users WITH (NOLOCK)
            UPDATE Users SET Status = 'Inactive'";
        var options = new AnalysisOptions
        {
            EnabledCategories = new HashSet<IssueCategory> { IssueCategory.Security }
        };

        // Act
        var result = _sut.Analyze(sql, options);

        // Assert
        result.Issues.Should().OnlyContain(i => i.Category == IssueCategory.Security || i.Category == IssueCategory.Syntax);
    }

    #endregion

    #region Result Counts Tests

    [Fact]
    public void Analyze_CountsErrors()
    {
        // Arrange
        var sql = "SELEC * FROM Users"; // Parse error

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.ErrorCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Analyze_CountsWarnings()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.WarningCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Analyze_CountsInfo()
    {
        // Arrange
        var sql = "SELECT * FROM Users WITH (NOLOCK)";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.InfoCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region Issue Properties Tests

    [Fact]
    public void Analyze_IssuesHaveLineAndColumn()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        var issue = result.Issues.First(i => i.RuleId == "PERF001");
        issue.Line.Should().BeGreaterThanOrEqualTo(0);
        issue.Column.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Analyze_IssuesHaveOffsets()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        var issue = result.Issues.First(i => i.RuleId == "PERF001");
        issue.StartOffset.Should().BeGreaterThanOrEqualTo(0);
        issue.EndOffset.Should().BeGreaterThan(issue.StartOffset);
    }

    [Fact]
    public void Analyze_IssuesHaveSuggestion()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        var issue = result.Issues.First(i => i.RuleId == "PERF001");
        issue.Suggestion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Analyze_IssuesOrderedByLine()
    {
        // Arrange
        var sql = @"
            DELETE FROM Users
            SELECT * FROM Products WITH (NOLOCK)
            UPDATE Orders SET Status = 'Shipped'";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().BeInAscendingOrder(i => i.Line);
    }

    #endregion

    #region Multiple Issues Tests

    [Fact]
    public void Analyze_MultipleIssues_ReportsAll()
    {
        // Arrange - SQL with multiple issues
        var sql = @"
            SELECT * FROM Users WITH (NOLOCK)
            DELETE FROM Orders";

        // Act
        var result = _sut.Analyze(sql);

        // Assert
        result.Issues.Should().Contain(i => i.RuleId == "PERF001"); // SELECT *
        result.Issues.Should().Contain(i => i.RuleId == "BP002");   // NOLOCK
        result.Issues.Should().Contain(i => i.RuleId == "SEC001");  // DELETE without WHERE
    }

    #endregion
}

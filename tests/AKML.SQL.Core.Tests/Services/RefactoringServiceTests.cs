using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for RefactoringService.
/// Covers Story 7.1: SQL Refactoring Tools.
/// </summary>
public class RefactoringServiceTests
{
    private readonly RefactoringService _sut;

    public RefactoringServiceTests()
    {
        var loggerMock = new Mock<ILogger<RefactoringService>>();
        _sut = new RefactoringService(loggerMock.Object);
    }

    #region RenameAlias Tests

    [Fact]
    public void RenameAlias_ValidAlias_RenamesAllOccurrences()
    {
        // Arrange
        var sql = "SELECT c.Name, c.Age FROM Customers c WHERE c.Id = 1";

        // Act
        var result = _sut.RenameAlias(sql, "c", "cust");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("cust.Name");
        result.RefactoredSql.Should().Contain("cust.Age");
        result.RefactoredSql.Should().Contain("Customers cust");
        result.RefactoredSql.Should().Contain("cust.Id");
        result.ChangesApplied.Should().Be(4); // alias definition + 3 column references
    }

    [Fact]
    public void RenameAlias_MultipleTableAliases_RenamesOnlySpecified()
    {
        // Arrange
        var sql = "SELECT c.Name, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId";

        // Act
        var result = _sut.RenameAlias(sql, "c", "cust");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("cust.Name");
        result.RefactoredSql.Should().Contain("o.Total"); // Unchanged
        result.RefactoredSql.Should().Contain("cust.Id");
        result.RefactoredSql.Should().Contain("o.CustomerId"); // Unchanged
    }

    [Fact]
    public void RenameAlias_EmptySql_ReturnsFailure()
    {
        // Act
        var result = _sut.RenameAlias("", "c", "cust");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void RenameAlias_EmptyOldAlias_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c";

        // Act
        var result = _sut.RenameAlias(sql, "", "cust");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Old alias");
    }

    [Fact]
    public void RenameAlias_SameOldAndNew_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c";

        // Act
        var result = _sut.RenameAlias(sql, "c", "c");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("different");
    }

    [Fact]
    public void RenameAlias_AliasNotFound_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers c";

        // Act
        var result = _sut.RenameAlias(sql, "x", "cust");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public void RenameAlias_InvalidSql_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FORM Customers"; // Invalid SQL

        // Act
        var result = _sut.RenameAlias(sql, "c", "cust");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("parse");
    }

    #endregion

    #region ExtractToCte Tests

    [Fact]
    public void ExtractToCte_ValidSubquery_ExtractsCorrectly()
    {
        // Arrange
        var sql = "SELECT * FROM Customers WHERE Id IN (SELECT CustomerId FROM Orders WHERE Total > 100)";
        var subqueryStart = sql.IndexOf("(SELECT");
        var subqueryEnd = sql.LastIndexOf(")") + 1;

        // Act
        var result = _sut.ExtractToCte(sql, subqueryStart, subqueryEnd, "HighValueCustomers");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("WITH HighValueCustomers AS");
        result.RefactoredSql.Should().Contain("SELECT CustomerId FROM Orders WHERE Total > 100");
    }

    [Fact]
    public void ExtractToCte_EmptySql_ReturnsFailure()
    {
        // Act
        var result = _sut.ExtractToCte("", 0, 10, "MyCte");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void ExtractToCte_EmptyCteName_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";

        // Act
        var result = _sut.ExtractToCte(sql, 0, 10, "");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("CTE name");
    }

    [Fact]
    public void ExtractToCte_InvalidOffsets_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";

        // Act
        var result = _sut.ExtractToCte(sql, 100, 10, "MyCte"); // start > end

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("offset");
    }

    #endregion

    #region QualifyColumnNames Tests

    [Fact]
    public void QualifyColumnNames_SingleTable_QualifiesColumns()
    {
        // Arrange
        var sql = "SELECT Name, Age FROM Customers c";

        // Act
        var result = _sut.QualifyColumnNames(sql);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("c.Name");
        result.RefactoredSql.Should().Contain("c.Age");
    }

    [Fact]
    public void QualifyColumnNames_AlreadyQualified_NoChanges()
    {
        // Arrange
        var sql = "SELECT c.Name, c.Age FROM Customers c";

        // Act
        var result = _sut.QualifyColumnNames(sql);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ChangesApplied.Should().Be(0);
    }

    [Fact]
    public void QualifyColumnNames_EmptySql_ReturnsFailure()
    {
        // Act
        var result = _sut.QualifyColumnNames("");

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    #endregion

    #region ExpandSelectStar Tests

    [Fact]
    public void ExpandSelectStar_ValidStar_ExpandsColumns()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";
        var columns = new[] { "Id", "Name", "Email", "Phone" };
        var starOffset = sql.IndexOf('*');

        // Act
        var result = _sut.ExpandSelectStar(sql, columns, starOffset);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("Id,");
        result.RefactoredSql.Should().Contain("Name,");
        result.RefactoredSql.Should().Contain("Email,");
        result.RefactoredSql.Should().Contain("Phone");
        result.RefactoredSql.Should().NotContain("*");
    }

    [Fact]
    public void ExpandSelectStar_TableQualifiedStar_ExpandsColumns()
    {
        // Arrange
        var sql = "SELECT c.* FROM Customers c";
        var columns = new[] { "Id", "Name" };
        var starOffset = sql.IndexOf('*');

        // Act
        var result = _sut.ExpandSelectStar(sql, columns, starOffset);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("Id,");
        result.RefactoredSql.Should().NotContain("c.*");
    }

    [Fact]
    public void ExpandSelectStar_EmptyColumns_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";

        // Act
        var result = _sut.ExpandSelectStar(sql, Array.Empty<string>(), 7);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Column names");
    }

    [Fact]
    public void ExpandSelectStar_InvalidOffset_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";

        // Act
        var result = _sut.ExpandSelectStar(sql, new[] { "Id" }, 1000);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("offset");
    }

    #endregion

    #region AddTableAlias Tests

    [Fact]
    public void AddTableAlias_TableWithoutAlias_AddsAlias()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers";
        var tableOffset = sql.IndexOf("Customers");

        // Act
        var result = _sut.AddTableAlias(sql, tableOffset, "c");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("Customers c");
    }

    [Fact]
    public void AddTableAlias_TableAlreadyHasAlias_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers c";
        var tableOffset = sql.IndexOf("Customers");

        // Act
        var result = _sut.AddTableAlias(sql, tableOffset, "cust");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already has alias");
    }

    [Fact]
    public void AddTableAlias_EmptyAlias_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers";

        // Act
        var result = _sut.AddTableAlias(sql, 17, "");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void AddTableAlias_InvalidOffset_ReturnsFailure()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers";

        // Act
        var result = _sut.AddTableAlias(sql, 5, "c"); // Offset points to "ct" in "SELECT"

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No table");
    }

    #endregion

    #region GetSuggestions Tests

    [Fact]
    public void GetSuggestions_AtSelectStar_SuggestsExpand()
    {
        // Arrange
        var sql = "SELECT * FROM Customers";
        var starOffset = sql.IndexOf('*');

        // Act
        var suggestions = _sut.GetSuggestions(sql, starOffset);

        // Assert
        suggestions.Should().Contain(s => s.Type == RefactoringType.ExpandSelectStar);
    }

    [Fact]
    public void GetSuggestions_AtTableWithoutAlias_SuggestsAddAlias()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers";
        var tableOffset = sql.IndexOf("Customers");

        // Act
        var suggestions = _sut.GetSuggestions(sql, tableOffset);

        // Assert
        suggestions.Should().Contain(s => s.Type == RefactoringType.AddAlias);
    }

    [Fact]
    public void GetSuggestions_AtTableWithAlias_SuggestsRename()
    {
        // Arrange
        var sql = "SELECT Name FROM Customers c";
        var aliasOffset = sql.IndexOf(" c") + 1; // Position of alias

        // Act
        var suggestions = _sut.GetSuggestions(sql, aliasOffset);

        // Assert
        suggestions.Should().Contain(s => s.Type == RefactoringType.RenameAlias);
    }

    [Fact]
    public void GetSuggestions_EmptySql_ReturnsEmptyList()
    {
        // Act
        var suggestions = _sut.GetSuggestions("", 0);

        // Assert
        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void GetSuggestions_InvalidSql_ReturnsEmptyList()
    {
        // Arrange
        var sql = "SELECT * FORM Customers"; // Invalid SQL

        // Act
        var suggestions = _sut.GetSuggestions(sql, 0);

        // Assert
        suggestions.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RenameAlias_CaseInsensitive_RenamesAlias()
    {
        // Arrange
        var sql = "SELECT C.Name FROM Customers c WHERE c.Id = 1";

        // Act
        var result = _sut.RenameAlias(sql, "C", "cust"); // Different case

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("cust.Name");
        result.RefactoredSql.Should().Contain("cust.Id");
    }

    [Fact]
    public void RenameAlias_ComplexQuery_RenamesCorrectly()
    {
        // Arrange
        var sql = @"
            SELECT
                c.Name,
                c.Email,
                (SELECT COUNT(*) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount
            FROM Customers c
            WHERE c.Active = 1";

        // Act
        var result = _sut.RenameAlias(sql, "c", "customer");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefactoredSql.Should().Contain("customer.Name");
        result.RefactoredSql.Should().Contain("customer.Email");
        result.RefactoredSql.Should().Contain("customer.Id");
        result.RefactoredSql.Should().Contain("customer.Active");
        result.RefactoredSql.Should().Contain("Customers customer");
        result.RefactoredSql.Should().Contain("o.CustomerId"); // Inner alias unchanged
    }

    #endregion
}

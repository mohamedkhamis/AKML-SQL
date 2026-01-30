using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for SnippetService.
/// Covers Story 8.2: Code Snippet Management.
/// </summary>
public class SnippetServiceTests
{
    private readonly SnippetService _sut;

    public SnippetServiceTests()
    {
        var loggerMock = new Mock<ILogger<SnippetService>>();
        _sut = new SnippetService(loggerMock.Object);
    }

    #region Built-in Snippets Tests

    [Fact]
    public void GetAll_ReturnsBuiltInSnippets()
    {
        // Act
        var snippets = _sut.GetAll();

        // Assert
        snippets.Should().NotBeEmpty();
        snippets.Should().Contain(s => s.Shortcut == "sel");
        snippets.Should().Contain(s => s.Shortcut == "ins");
        snippets.Should().Contain(s => s.Shortcut == "upd");
    }

    [Fact]
    public void GetByShortcut_BuiltIn_ReturnsSnippet()
    {
        // Act
        var snippet = _sut.GetByShortcut("sel");

        // Assert
        snippet.Should().NotBeNull();
        snippet!.Name.Should().Be("SELECT");
        snippet.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void GetCategories_ReturnsUniqueCategories()
    {
        // Act
        var categories = _sut.GetCategories();

        // Assert
        categories.Should().Contain("Query");
        categories.Should().Contain("DML");
        categories.Should().Contain("DDL");
        categories.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetByCategory_FiltersCorrectly()
    {
        // Act
        var dmlSnippets = _sut.GetByCategory("DML");

        // Assert
        dmlSnippets.Should().OnlyContain(s => s.Category == "DML");
        dmlSnippets.Should().Contain(s => s.Shortcut == "ins");
    }

    #endregion

    #region Placeholder Tests

    [Fact]
    public void ExpandSnippetCode_WithPlaceholders_Expands()
    {
        // Arrange
        var code = "SELECT ${columns:*} FROM ${table:Users}";
        var values = new Dictionary<string, string>
        {
            ["columns"] = "Id, Name",
            ["table"] = "Customers"
        };

        // Act
        var result = _sut.ExpandSnippetCode(code, values);

        // Assert
        result.Should().Be("SELECT Id, Name FROM Customers");
    }

    [Fact]
    public void ExpandSnippetCode_MissingValue_UsesDefault()
    {
        // Arrange
        var code = "SELECT ${columns:*} FROM ${table:Users}";

        // Act
        var result = _sut.ExpandSnippetCode(code, null);

        // Assert
        result.Should().Be("SELECT * FROM Users");
    }

    [Fact]
    public void ExpandSnippetCode_NoDefault_UsesPlaceholderName()
    {
        // Arrange
        var code = "SELECT * FROM ${table}";

        // Act
        var result = _sut.ExpandSnippetCode(code, null);

        // Assert
        result.Should().Be("SELECT * FROM table");
    }

    [Fact]
    public void ExpandSnippet_ById_ExpandsCorrectly()
    {
        // Arrange
        var values = new Dictionary<string, string>
        {
            ["columns"] = "Id, Name",
            ["table"] = "Employees",
            ["condition"] = "Active = 1"
        };

        // Act
        var result = _sut.ExpandSnippet("sel", values);

        // Assert
        result.Should().Contain("Id, Name");
        result.Should().Contain("Employees");
        result.Should().Contain("Active = 1");
    }

    [Fact]
    public void ExpandSnippet_InvalidId_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.ExpandSnippet("nonexistent");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*not found*");
    }

    #endregion

    #region Custom Snippet Tests

    [Fact]
    public void SaveSnippet_ValidSnippet_Saves()
    {
        // Arrange
        var uniqueShortcut = $"customtest{Guid.NewGuid():N}".Substring(0, 20);
        var snippet = new CodeSnippet
        {
            Name = "Custom Test",
            Shortcut = uniqueShortcut,
            Code = "SELECT 'Custom'",
            Category = "Custom"
        };

        // Act
        _sut.SaveSnippet(snippet);
        var retrieved = _sut.GetByShortcut(uniqueShortcut);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Custom Test");
        retrieved.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void SaveSnippet_EmptyName_ThrowsException()
    {
        // Arrange
        var snippet = new CodeSnippet { Name = "", Shortcut = "test", Code = "SELECT 1" };

        // Act & Assert
        var act = () => _sut.SaveSnippet(snippet);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*name*");
    }

    [Fact]
    public void SaveSnippet_EmptyShortcut_ThrowsException()
    {
        // Arrange
        var snippet = new CodeSnippet { Name = "Test", Shortcut = "", Code = "SELECT 1" };

        // Act & Assert
        var act = () => _sut.SaveSnippet(snippet);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*shortcut*");
    }

    [Fact]
    public void SaveSnippet_EmptyCode_ThrowsException()
    {
        // Arrange
        var snippet = new CodeSnippet { Name = "Test", Shortcut = "test", Code = "" };

        // Act & Assert
        var act = () => _sut.SaveSnippet(snippet);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*code*");
    }

    [Fact]
    public void SaveSnippet_DuplicateShortcut_ThrowsException()
    {
        // Arrange
        var snippet = new CodeSnippet
        {
            Name = "Duplicate",
            Shortcut = "sel", // Already exists
            Code = "SELECT 1"
        };

        // Act & Assert
        var act = () => _sut.SaveSnippet(snippet);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already used*");
    }

    [Fact]
    public void DeleteSnippet_CustomSnippet_Deletes()
    {
        // Arrange
        var uniqueShortcut = $"del{Guid.NewGuid():N}".Substring(0, 15);
        var snippet = new CodeSnippet
        {
            Name = "To Delete",
            Shortcut = uniqueShortcut,
            Code = "SELECT 1"
        };
        _sut.SaveSnippet(snippet);

        // Act
        var result = _sut.DeleteSnippet(snippet.Id);

        // Assert
        result.Should().BeTrue();
        _sut.GetByShortcut(uniqueShortcut).Should().BeNull();
    }

    [Fact]
    public void DeleteSnippet_BuiltIn_ThrowsException()
    {
        // Arrange
        var builtIn = _sut.GetByShortcut("sel");

        // Act & Assert
        var act = () => _sut.DeleteSnippet(builtIn!.Id);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*built-in*");
    }

    [Fact]
    public void DeleteSnippet_NonExistent_ReturnsFalse()
    {
        // Act
        var result = _sut.DeleteSnippet("nonexistent-id");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Search Tests

    [Fact]
    public void Search_ByName_FindsSnippets()
    {
        // Act
        var results = _sut.Search("SELECT");

        // Assert
        results.Should().Contain(s => s.Name.Contains("SELECT"));
    }

    [Fact]
    public void Search_ByShortcut_FindsSnippets()
    {
        // Act
        var results = _sut.Search("sel");

        // Assert
        results.Should().Contain(s => s.Shortcut == "sel");
    }

    [Fact]
    public void Search_CaseInsensitive_FindsSnippets()
    {
        // Act
        var results = _sut.Search("SELECT");
        var resultsLower = _sut.Search("select");

        // Assert
        results.Should().BeEquivalentTo(resultsLower);
    }

    #endregion

    #region Export/Import Tests

    [Fact]
    public void ExportSnippets_CustomSnippets_ExportsJson()
    {
        // Arrange
        var uniqueShortcut = $"exp{Guid.NewGuid():N}".Substring(0, 15);
        var snippet = new CodeSnippet
        {
            Name = "Export Test " + uniqueShortcut,
            Shortcut = uniqueShortcut,
            Code = "SELECT 'Export'"
        };
        _sut.SaveSnippet(snippet);

        // Act
        var json = _sut.ExportSnippets();

        // Assert
        json.Should().Contain("Export Test");
        json.Should().Contain(uniqueShortcut);
    }

    [Fact]
    public void ImportSnippets_ValidJson_ImportsSnippets()
    {
        // Arrange
        var json = @"[{
            ""name"": ""Imported"",
            ""shortcut"": ""imported"",
            ""code"": ""SELECT 'Imported'""
        }]";

        // Act
        var count = _sut.ImportSnippets(json);

        // Assert
        count.Should().Be(1);
        _sut.GetByShortcut("imported").Should().NotBeNull();
    }

    [Fact]
    public void ImportSnippets_ShortcutConflict_ModifiesShortcut()
    {
        // Arrange
        var uniqueShortcut = $"conf{Guid.NewGuid():N}".Substring(0, 12);
        var snippet = new CodeSnippet
        {
            Name = "Original",
            Shortcut = uniqueShortcut,
            Code = "SELECT 1"
        };
        _sut.SaveSnippet(snippet);

        var json = $@"[{{
            ""id"": ""different-id"",
            ""name"": ""Conflicting{uniqueShortcut}"",
            ""shortcut"": ""{uniqueShortcut}"",
            ""code"": ""SELECT 2""
        }}]";

        // Act
        var count = _sut.ImportSnippets(json);

        // Assert
        count.Should().Be(1);
        var imported = _sut.Search($"Conflicting{uniqueShortcut}").FirstOrDefault();
        imported.Should().NotBeNull();
        imported!.Shortcut.Should().StartWith(uniqueShortcut);
        imported.Shortcut.Should().NotBe(uniqueShortcut);
    }

    [Fact]
    public void ImportSnippets_EmptyJson_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.ImportSnippets("");
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Placeholder Extraction Tests

    [Fact]
    public void SaveSnippet_ExtractsPlaceholders()
    {
        // Arrange
        var uniqueShortcut = $"withph{Guid.NewGuid():N}".Substring(0, 15);
        var snippet = new CodeSnippet
        {
            Name = "With Placeholders",
            Shortcut = uniqueShortcut,
            Code = "SELECT ${col1:*}, ${col2:Name} FROM ${table:Users}"
        };

        // Act
        _sut.SaveSnippet(snippet);
        var saved = _sut.GetByShortcut(uniqueShortcut);

        // Assert
        saved!.Placeholders.Should().HaveCount(3);
        saved.Placeholders.Should().Contain(p => p.Name == "col1" && p.DefaultValue == "*");
        saved.Placeholders.Should().Contain(p => p.Name == "col2" && p.DefaultValue == "Name");
        saved.Placeholders.Should().Contain(p => p.Name == "table" && p.DefaultValue == "Users");
    }

    #endregion
}

using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for FormatStyleService.
/// Covers Story 6.1: Formatting Style Management.
/// </summary>
public class FormatStyleServiceTests
{
    private readonly FormatStyleService _sut;

    public FormatStyleServiceTests()
    {
        var loggerMock = new Mock<ILogger<FormatStyleService>>();
        _sut = new FormatStyleService(loggerMock.Object);
    }

    #region Built-in Styles Tests

    [Fact]
    public void GetAllStyles_ReturnsBuiltInStyles()
    {
        // Act
        var styles = _sut.GetAllStyles();

        // Assert
        styles.Should().HaveCountGreaterThanOrEqualTo(3);
        styles.Should().Contain(s => s.Name == "Compact");
        styles.Should().Contain(s => s.Name == "Standard");
        styles.Should().Contain(s => s.Name == "Expanded");
    }

    [Fact]
    public void GetStyle_Compact_ReturnsCompactStyle()
    {
        // Act
        var style = _sut.GetStyle("Compact");

        // Assert
        style.Should().NotBeNull();
        style!.Name.Should().Be("Compact");
        style.IsBuiltIn.Should().BeTrue();
        style.IndentSize.Should().Be(2);
        style.MultilineSelectElementsList.Should().BeFalse();
        style.NewLineBeforeFromClause.Should().BeFalse();
    }

    [Fact]
    public void GetStyle_Standard_ReturnsStandardStyle()
    {
        // Act
        var style = _sut.GetStyle("Standard");

        // Assert
        style.Should().NotBeNull();
        style!.Name.Should().Be("Standard");
        style.IsBuiltIn.Should().BeTrue();
        style.IndentSize.Should().Be(4);
        style.MultilineSelectElementsList.Should().BeTrue();
        style.NewLineBeforeFromClause.Should().BeTrue();
        style.AsKeywordOnOwnLine.Should().BeFalse();
    }

    [Fact]
    public void GetStyle_Expanded_ReturnsExpandedStyle()
    {
        // Act
        var style = _sut.GetStyle("Expanded");

        // Assert
        style.Should().NotBeNull();
        style!.Name.Should().Be("Expanded");
        style.IsBuiltIn.Should().BeTrue();
        style.IndentSize.Should().Be(4);
        style.MultilineSelectElementsList.Should().BeTrue();
        style.NewLineBeforeFromClause.Should().BeTrue();
        style.AsKeywordOnOwnLine.Should().BeTrue();
        style.NewLineBeforeOpenParenthesis.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultStyle_ReturnsStandardStyle()
    {
        // Act
        var style = _sut.GetDefaultStyle();

        // Assert
        style.Should().NotBeNull();
        style.Name.Should().Be("Standard");
    }

    [Fact]
    public void GetStyle_CaseInsensitive_ReturnsStyle()
    {
        // Act
        var style1 = _sut.GetStyle("COMPACT");
        var style2 = _sut.GetStyle("compact");
        var style3 = _sut.GetStyle("Compact");

        // Assert
        style1.Should().NotBeNull();
        style2.Should().NotBeNull();
        style3.Should().NotBeNull();
        style1!.Name.Should().Be("Compact");
    }

    [Fact]
    public void GetStyle_NonExistent_ReturnsNull()
    {
        // Act
        var style = _sut.GetStyle("NonExistentStyle");

        // Assert
        style.Should().BeNull();
    }

    #endregion

    #region Custom Styles Tests

    [Fact]
    public void SaveCustomStyle_ValidStyle_AddsStyle()
    {
        // Arrange
        var customStyle = new FormatStyle
        {
            Name = "MyCustomStyle",
            Description = "My custom formatting style",
            IndentSize = 3,
            KeywordCasing = FormatKeywordCasing.Lowercase
        };

        // Act
        _sut.SaveCustomStyle(customStyle);
        var retrieved = _sut.GetStyle("MyCustomStyle");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("MyCustomStyle");
        retrieved.IndentSize.Should().Be(3);
        retrieved.KeywordCasing.Should().Be(FormatKeywordCasing.Lowercase);
        retrieved.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void SaveCustomStyle_UpdateExisting_UpdatesStyle()
    {
        // Arrange
        var customStyle1 = new FormatStyle { Name = "UpdateTest", IndentSize = 2 };
        var customStyle2 = new FormatStyle { Name = "UpdateTest", IndentSize = 6 };

        // Act
        _sut.SaveCustomStyle(customStyle1);
        _sut.SaveCustomStyle(customStyle2);
        var retrieved = _sut.GetStyle("UpdateTest");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.IndentSize.Should().Be(6);
    }

    [Fact]
    public void SaveCustomStyle_EmptyName_ThrowsException()
    {
        // Arrange
        var style = new FormatStyle { Name = "" };

        // Act & Assert
        var act = () => _sut.SaveCustomStyle(style);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SaveCustomStyle_OverwriteBuiltIn_ThrowsException()
    {
        // Arrange
        var style = new FormatStyle { Name = "Standard" };

        // Act & Assert
        var act = () => _sut.SaveCustomStyle(style);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*built-in*");
    }

    [Fact]
    public void DeleteCustomStyle_ExistingStyle_RemovesStyle()
    {
        // Arrange
        var customStyle = new FormatStyle { Name = "ToDelete" };
        _sut.SaveCustomStyle(customStyle);

        // Act
        var result = _sut.DeleteCustomStyle("ToDelete");

        // Assert
        result.Should().BeTrue();
        _sut.GetStyle("ToDelete").Should().BeNull();
    }

    [Fact]
    public void DeleteCustomStyle_NonExistent_ReturnsFalse()
    {
        // Act
        var result = _sut.DeleteCustomStyle("NonExistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DeleteCustomStyle_BuiltIn_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.DeleteCustomStyle("Standard");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*built-in*");
    }

    #endregion

    #region Export/Import Tests

    [Fact]
    public void ExportStyle_ValidStyle_ReturnsJson()
    {
        // Act
        var json = _sut.ExportStyle("Standard");

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"Standard\"");
        json.Should().Contain("\"indentSize\"");
    }

    [Fact]
    public void ExportStyle_NonExistent_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.ExportStyle("NonExistent");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportStyle_ValidJson_ReturnsStyle()
    {
        // Arrange
        var json = @"{
            ""name"": ""ImportedStyle"",
            ""indentSize"": 8,
            ""keywordCasing"": ""lowercase""
        }";

        // Act
        var style = _sut.ImportStyle(json);

        // Assert
        style.Should().NotBeNull();
        style.Name.Should().Be("ImportedStyle");
        style.IndentSize.Should().Be(8);
        style.KeywordCasing.Should().Be(FormatKeywordCasing.Lowercase);
        style.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void ImportStyle_EmptyJson_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.ImportStyle("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportAndSaveStyle_ValidJson_SavesStyle()
    {
        // Arrange
        var json = @"{
            ""name"": ""TempName"",
            ""indentSize"": 5
        }";

        // Act
        var style = _sut.ImportAndSaveStyle(json, "FinalName");

        // Assert
        style.Name.Should().Be("FinalName");
        _sut.GetStyle("FinalName").Should().NotBeNull();
    }

    [Fact]
    public void RoundTrip_ExportImport_PreservesStyle()
    {
        // Arrange
        var original = new FormatStyle
        {
            Name = "RoundTripTest",
            IndentSize = 6,
            KeywordCasing = FormatKeywordCasing.PascalCase,
            MultilineSelectElementsList = true,
            NewLineBeforeFromClause = false
        };
        _sut.SaveCustomStyle(original);

        // Act
        var json = _sut.ExportStyle("RoundTripTest");
        var imported = _sut.ImportStyle(json);

        // Assert
        imported.IndentSize.Should().Be(6);
        imported.KeywordCasing.Should().Be(FormatKeywordCasing.PascalCase);
        imported.MultilineSelectElementsList.Should().BeTrue();
        imported.NewLineBeforeFromClause.Should().BeFalse();
    }

    #endregion

    #region Generator Options Conversion Tests

    [Fact]
    public void ToGeneratorOptions_StandardStyle_ReturnsValidOptions()
    {
        // Arrange
        var style = _sut.GetStyle("Standard")!;

        // Act
        var options = _sut.ToGeneratorOptions(style);

        // Assert
        options.Should().NotBeNull();
        options.IndentationSize.Should().Be(4);
        options.KeywordCasing.Should().Be(KeywordCasing.Uppercase);
        options.AlignClauseBodies.Should().BeTrue();
        options.NewLineBeforeFromClause.Should().BeTrue();
    }

    [Fact]
    public void ToGeneratorOptions_CompactStyle_ReturnsCompactOptions()
    {
        // Arrange
        var style = _sut.GetStyle("Compact")!;

        // Act
        var options = _sut.ToGeneratorOptions(style);

        // Assert
        options.IndentationSize.Should().Be(2);
        options.AlignClauseBodies.Should().BeFalse();
        options.NewLineBeforeFromClause.Should().BeFalse();
        options.MultilineSelectElementsList.Should().BeFalse();
    }

    [Fact]
    public void ToGeneratorOptions_LowercaseKeywords_SetsCorrectCasing()
    {
        // Arrange
        var style = new FormatStyle
        {
            Name = "TestLowercase",
            KeywordCasing = FormatKeywordCasing.Lowercase
        };

        // Act
        var options = _sut.ToGeneratorOptions(style);

        // Assert
        options.KeywordCasing.Should().Be(KeywordCasing.Lowercase);
    }

    [Fact]
    public void FromGeneratorOptions_ValidOptions_CreatesStyle()
    {
        // Arrange
        var options = new SqlScriptGeneratorOptions
        {
            IndentationSize = 3,
            KeywordCasing = KeywordCasing.PascalCase,
            AlignClauseBodies = true,
            NewLineBeforeFromClause = true
        };

        // Act
        var style = _sut.FromGeneratorOptions(options, "FromOptionsTest");

        // Assert
        style.Name.Should().Be("FromOptionsTest");
        style.IndentSize.Should().Be(3);
        style.KeywordCasing.Should().Be(FormatKeywordCasing.PascalCase);
        style.AlignClauseBodies.Should().BeTrue();
        style.NewLineBeforeFromClause.Should().BeTrue();
        style.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_ToFromGeneratorOptions_PreservesSettings()
    {
        // Arrange
        var original = _sut.GetStyle("Expanded")!;

        // Act
        var options = _sut.ToGeneratorOptions(original);
        var converted = _sut.FromGeneratorOptions(options, "Converted");

        // Assert
        converted.IndentSize.Should().Be(original.IndentSize);
        converted.KeywordCasing.Should().Be(original.KeywordCasing);
        converted.AlignClauseBodies.Should().Be(original.AlignClauseBodies);
        converted.NewLineBeforeFromClause.Should().Be(original.NewLineBeforeFromClause);
        converted.NewLineBeforeOpenParenthesis.Should().Be(original.NewLineBeforeOpenParenthesis);
    }

    #endregion

    #region FormatStyle Clone Tests

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new FormatStyle
        {
            Name = "Original",
            IndentSize = 4,
            KeywordCasing = FormatKeywordCasing.Uppercase
        };

        // Act
        var clone = original.Clone();
        clone.Name = "Cloned";
        clone.IndentSize = 8;

        // Assert
        original.Name.Should().Be("Original");
        original.IndentSize.Should().Be(4);
        clone.Name.Should().Be("Cloned");
        clone.IndentSize.Should().Be(8);
        clone.IsBuiltIn.Should().BeFalse();
    }

    #endregion
}

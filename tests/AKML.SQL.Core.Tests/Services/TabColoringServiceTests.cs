using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for TabColoringService.
/// Covers Story 9.1: Tab Management.
/// </summary>
public class TabColoringServiceTests
{
    private readonly TabColoringService _sut;

    public TabColoringServiceTests()
    {
        var loggerMock = new Mock<ILogger<TabColoringService>>();
        _sut = new TabColoringService(loggerMock.Object);
    }

    #region Built-in Rules Tests

    [Fact]
    public void GetAllRules_ReturnsBuiltInRules()
    {
        // Act
        var rules = _sut.GetAllRules();

        // Assert
        rules.Should().NotBeEmpty();
        rules.Should().Contain(r => r.Name == "Production");
        rules.Should().Contain(r => r.Name == "UAT");
        rules.Should().Contain(r => r.Name == "Development");
        rules.Should().Contain(r => r.Name == "Local");
    }

    [Fact]
    public void GetAllRules_BuiltInRulesHaveCorrectProperties()
    {
        // Act
        var rules = _sut.GetAllRules();
        var prodRule = rules.First(r => r.Name == "Production");

        // Assert
        prodRule.IsBuiltIn.Should().BeTrue();
        prodRule.IsEnabled.Should().BeTrue();
        prodRule.Color.Should().Be("#E74C3C");
        prodRule.Pattern.Should().Be("*prod*");
        prodRule.MatchType.Should().Be(PatternMatchType.Wildcard);
    }

    [Fact]
    public void GetAllRules_HasExpectedRuleCount()
    {
        // Act
        var rules = _sut.GetAllRules();

        // Assert
        rules.Count(r => r.IsBuiltIn).Should().Be(7);
    }

    #endregion

    #region GetColor Tests

    [Fact]
    public void GetColor_NullServerAndDatabase_ReturnsNoConnectionColor()
    {
        // Act
        var result = _sut.GetColor(null, null);

        // Assert
        result.Color.Should().Be("#808080");
        result.Label.Should().Be("No Connection");
        result.MatchedRule.Should().BeNull();
    }

    [Fact]
    public void GetColor_EmptyServerAndDatabase_ReturnsNoConnectionColor()
    {
        // Act
        var result = _sut.GetColor("", "");

        // Assert
        result.Color.Should().Be("#808080");
        result.Label.Should().Be("No Connection");
    }

    [Fact]
    public void GetColor_ProductionServer_ReturnsRedColor()
    {
        // Act
        var result = _sut.GetColor("sql-prod-01", "MyDatabase");

        // Assert
        result.Color.Should().Be("#E74C3C");
        result.Label.Should().Be("PRODUCTION");
        result.MatchedRule.Should().Be("Production");
    }

    [Fact]
    public void GetColor_UatServer_ReturnsOrangeColor()
    {
        // Act
        var result = _sut.GetColor("sql-uat-server", "TestDB");

        // Assert
        result.Color.Should().Be("#F39C12");
        result.Label.Should().Be("UAT");
        result.MatchedRule.Should().Be("UAT");
    }

    [Fact]
    public void GetColor_DevServer_ReturnsGreenColor()
    {
        // Act
        var result = _sut.GetColor("dev-sql-01", "DevDB");

        // Assert
        result.Color.Should().Be("#2ECC71");
        result.Label.Should().Be("Dev");
        result.MatchedRule.Should().Be("Development");
    }

    [Fact]
    public void GetColor_LocalhostServer_ReturnsTealColor()
    {
        // Act
        var result = _sut.GetColor("localhost", "master");

        // Assert
        result.Color.Should().Be("#1ABC9C");
        result.Label.Should().Be("Local");
        result.MatchedRule.Should().Be("Local");
    }

    [Fact]
    public void GetColor_LocalDbServer_ReturnsTealColor()
    {
        // Act
        var result = _sut.GetColor("(localdb)\\MSSQLLocalDB", "SomeDatabase");

        // Assert
        result.Color.Should().Be("#1ABC9C");
        result.Label.Should().Be("LocalDB");
        result.MatchedRule.Should().Be("Local Instance");
    }

    [Fact]
    public void GetColor_UnmatchedServer_ReturnsDefaultColor()
    {
        // Act
        var result = _sut.GetColor("unknown-server", "SomeDB");

        // Assert
        result.Color.Should().Be("#4A90D9");
        result.MatchedRule.Should().BeNull();
    }

    [Fact]
    public void GetColor_UnmatchedServer_ReturnsDbAsLabel()
    {
        // Act
        var result = _sut.GetColor("unknown-server", "MyDatabase");

        // Assert
        result.Label.Should().Be("MyDatabase");
    }

    [Fact]
    public void GetColor_RulesPrioritizedCorrectly()
    {
        // A server that matches both "production" and "dev" patterns
        // Production has lower priority number so it should win
        var result = _sut.GetColor("production-dev-server", "DB");

        // Assert - Production (priority 0) should match before Dev (priority 4)
        result.MatchedRule.Should().Be("Production");
    }

    #endregion

    #region Pattern Matching Tests

    [Theory]
    [InlineData("test", PatternMatchType.Contains, "this is a test string", true)]
    [InlineData("test", PatternMatchType.Contains, "no match here", false)]
    [InlineData("TEST", PatternMatchType.Contains, "test string", true)]
    public void TestPattern_Contains_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("prefix", PatternMatchType.StartsWith, "prefix-value", true)]
    [InlineData("prefix", PatternMatchType.StartsWith, "value-prefix", false)]
    public void TestPattern_StartsWith_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("suffix", PatternMatchType.EndsWith, "value-suffix", true)]
    [InlineData("suffix", PatternMatchType.EndsWith, "suffix-value", false)]
    public void TestPattern_EndsWith_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("exact", PatternMatchType.Exact, "exact", true)]
    [InlineData("exact", PatternMatchType.Exact, "EXACT", true)]
    [InlineData("exact", PatternMatchType.Exact, "exact-not", false)]
    public void TestPattern_Exact_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("*prod*", PatternMatchType.Wildcard, "sql-prod-01", true)]
    [InlineData("sql-??-*", PatternMatchType.Wildcard, "sql-01-server", true)]
    [InlineData("*.db", PatternMatchType.Wildcard, "test.db", true)]
    [InlineData("*.db", PatternMatchType.Wildcard, "test.txt", false)]
    public void TestPattern_Wildcard_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("sql-\\d+", PatternMatchType.Regex, "sql-001", true)]
    [InlineData("^prod", PatternMatchType.Regex, "prod-server", true)]
    [InlineData("server$", PatternMatchType.Regex, "my-server", true)]
    [InlineData("^prod$", PatternMatchType.Regex, "prod-server", false)]
    public void TestPattern_Regex_MatchesCorrectly(string pattern, PatternMatchType matchType, string value, bool expected)
    {
        // Act
        var result = _sut.TestPattern(pattern, matchType, value);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void TestPattern_InvalidRegex_ReturnsFalse()
    {
        // Act
        var result = _sut.TestPattern("[invalid(regex", PatternMatchType.Regex, "test");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TestPattern_EmptyPattern_ReturnsFalse()
    {
        // Act
        var result = _sut.TestPattern("", PatternMatchType.Contains, "test");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TestPattern_EmptyValue_ReturnsFalse()
    {
        // Act
        var result = _sut.TestPattern("test", PatternMatchType.Contains, "");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Custom Rule Management Tests

    [Fact]
    public void SaveRule_ValidRule_AddsRule()
    {
        // Arrange
        var uniqueName = $"CustomRule{Guid.NewGuid():N}".Substring(0, 20);
        var rule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*custom*",
            Color = "#FF5733",
            Label = "Custom",
            MatchType = PatternMatchType.Wildcard,
            TargetField = RuleTargetField.Server,
            IsEnabled = true
        };

        // Act
        _sut.SaveRule(rule);

        // Assert
        var rules = _sut.GetAllRules();
        rules.Should().Contain(r => r.Name == uniqueName);
        rules.First(r => r.Name == uniqueName).IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void SaveRule_NullRule_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.SaveRule(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveRule_EmptyName_ThrowsException()
    {
        // Arrange
        var rule = new TabColorRule { Name = "", Pattern = "test", Color = "#FF5733" };

        // Act & Assert
        var act = () => _sut.SaveRule(rule);
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void SaveRule_EmptyPattern_ThrowsException()
    {
        // Arrange
        var rule = new TabColorRule { Name = "Test", Pattern = "", Color = "#FF5733" };

        // Act & Assert
        var act = () => _sut.SaveRule(rule);
        act.Should().Throw<ArgumentException>().WithMessage("*pattern*");
    }

    [Fact]
    public void SaveRule_ExistingName_UpdatesRule()
    {
        // Arrange
        var uniqueName = $"UpdateRule{Guid.NewGuid():N}".Substring(0, 20);
        var rule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*original*",
            Color = "#FF5733"
        };
        _sut.SaveRule(rule);

        var updatedRule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*updated*",
            Color = "#00FF00"
        };

        // Act
        _sut.SaveRule(updatedRule);

        // Assert
        var rules = _sut.GetAllRules();
        var savedRule = rules.First(r => r.Name == uniqueName);
        savedRule.Pattern.Should().Be("*updated*");
        savedRule.Color.Should().Be("#00FF00");
    }

    [Fact]
    public void DeleteRule_CustomRule_RemovesRule()
    {
        // Arrange
        var uniqueName = $"DeleteRule{Guid.NewGuid():N}".Substring(0, 20);
        var rule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*delete*",
            Color = "#FF5733"
        };
        _sut.SaveRule(rule);

        // Act
        var result = _sut.DeleteRule(uniqueName);

        // Assert
        result.Should().BeTrue();
        _sut.GetAllRules().Should().NotContain(r => r.Name == uniqueName);
    }

    [Fact]
    public void DeleteRule_BuiltInRule_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.DeleteRule("Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*built-in*");
    }

    [Fact]
    public void DeleteRule_NonExistent_ReturnsFalse()
    {
        // Act
        var result = _sut.DeleteRule("NonExistentRule");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Rule Reordering Tests

    [Fact]
    public void ReorderRules_ChangesRulePriorities()
    {
        // Arrange
        var uniqueName1 = $"Order1{Guid.NewGuid():N}".Substring(0, 15);
        var uniqueName2 = $"Order2{Guid.NewGuid():N}".Substring(0, 15);

        _sut.SaveRule(new TabColorRule { Name = uniqueName1, Pattern = "*a*", Color = "#FF0000", Priority = 100 });
        _sut.SaveRule(new TabColorRule { Name = uniqueName2, Pattern = "*b*", Color = "#00FF00", Priority = 101 });

        // Act
        _sut.ReorderRules(new[] { uniqueName2, uniqueName1 });

        // Assert
        var rules = _sut.GetAllRules();
        var rule1 = rules.First(r => r.Name == uniqueName1);
        var rule2 = rules.First(r => r.Name == uniqueName2);
        rule2.Priority.Should().BeLessThan(rule1.Priority);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void ResetToDefaults_RemovesCustomRules()
    {
        // Arrange
        var uniqueName = $"ResetTest{Guid.NewGuid():N}".Substring(0, 20);
        _sut.SaveRule(new TabColorRule { Name = uniqueName, Pattern = "*test*", Color = "#FF5733" });

        // Act
        _sut.ResetToDefaults();

        // Assert
        var rules = _sut.GetAllRules();
        rules.Should().NotContain(r => r.Name == uniqueName);
        rules.All(r => r.IsBuiltIn).Should().BeTrue();
    }

    [Fact]
    public void ResetToDefaults_RestoresBuiltInRules()
    {
        // Act
        _sut.ResetToDefaults();

        // Assert
        var rules = _sut.GetAllRules();
        rules.Should().Contain(r => r.Name == "Production");
        rules.Should().Contain(r => r.Name == "UAT");
        rules.Should().Contain(r => r.Name == "Development");
    }

    #endregion

    #region Target Field Tests

    [Fact]
    public void GetColor_DatabaseTarget_MatchesDatabase()
    {
        // Arrange
        var uniqueName = $"DbRule{Guid.NewGuid():N}".Substring(0, 15);
        var rule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*UniqueTestDB*",
            Color = "#AA00FF",
            TargetField = RuleTargetField.Database,
            MatchType = PatternMatchType.Wildcard,
            Priority = -1 // Higher priority than built-in
        };
        _sut.SaveRule(rule);

        // Act
        var result = _sut.GetColor("unknown-server", "UniqueTestDB");

        // Assert
        result.Color.Should().Be("#AA00FF");
        result.MatchedRule.Should().Be(uniqueName);
    }

    [Fact]
    public void GetColor_BothTarget_MatchesCombined()
    {
        // Arrange
        var uniqueName = $"BothRule{Guid.NewGuid():N}".Substring(0, 15);
        var rule = new TabColorRule
        {
            Name = uniqueName,
            Pattern = "*uniquesql*.uniquemaster*",
            Color = "#FFAA00",
            TargetField = RuleTargetField.Both,
            MatchType = PatternMatchType.Wildcard,
            Priority = -1
        };
        _sut.SaveRule(rule);

        // Act
        var result = _sut.GetColor("uniquesql-server", "uniquemaster");

        // Assert
        result.Color.Should().Be("#FFAA00");
        result.MatchedRule.Should().Be(uniqueName);
    }

    #endregion
}

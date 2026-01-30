using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for AiService.
/// Covers Story 10.1: AI Integration.
/// </summary>
public class AiServiceTests
{
    private readonly AiService _sut;

    public AiServiceTests()
    {
        var loggerMock = new Mock<ILogger<AiService>>();
        _sut = new AiService(loggerMock.Object);
    }

    #region Configuration Tests

    [Fact]
    public void Configure_ValidSettings_SetsProvider()
    {
        // Arrange
        var settings = new AiProviderSettings
        {
            Provider = AiProvider.Anthropic,
            ApiKey = "test-key",
            Endpoint = "https://api.anthropic.com/v1/messages",
            Model = "claude-3-sonnet-20240229"
        };

        // Act
        _sut.Configure(settings);
        var result = _sut.GetSettings();

        // Assert
        result.Provider.Should().Be(AiProvider.Anthropic);
        result.ApiKey.Should().Be("test-key");
        result.Model.Should().Be("claude-3-sonnet-20240229");
    }

    [Fact]
    public void Configure_NullSettings_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.Configure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetSettings_DefaultValues_ReturnsDefaults()
    {
        // Act
        var settings = _sut.GetSettings();

        // Assert
        settings.Provider.Should().Be(AiProvider.OpenAI);
        settings.MaxTokens.Should().Be(2048);
        settings.Temperature.Should().Be(0.3);
    }

    [Fact]
    public void IsConfigured_NoApiKey_ReturnsFalse()
    {
        // Act
        var result = _sut.IsConfigured();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WithApiKey_ReturnsTrue()
    {
        // Arrange
        _sut.Configure(new AiProviderSettings
        {
            ApiKey = "test-key",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        });

        // Act
        var result = _sut.IsConfigured();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_EmptyEndpoint_ReturnsFalse()
    {
        // Arrange
        _sut.Configure(new AiProviderSettings
        {
            ApiKey = "test-key",
            Endpoint = ""
        });

        // Act
        var result = _sut.IsConfigured();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region NaturalLanguageToSql Tests

    [Fact]
    public async Task NaturalLanguageToSqlAsync_EmptyQuery_ReturnsError()
    {
        // Act
        var result = await _sut.NaturalLanguageToSqlAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task NaturalLanguageToSqlAsync_NullQuery_ReturnsError()
    {
        // Act
        var result = await _sut.NaturalLanguageToSqlAsync(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task NaturalLanguageToSqlAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.NaturalLanguageToSqlAsync("Select all users");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task NaturalLanguageToSqlAsync_WithAiSchemaContext_AcceptsContext()
    {
        // Arrange
        var schema = new AiSchemaContext
        {
            DatabaseName = "TestDB",
            Tables = new List<AiTableInfo>
            {
                new()
                {
                    Schema = "dbo",
                    Name = "Users",
                    Columns = new List<AiColumnInfo>
                    {
                        new() { Name = "Id", DataType = "int", IsPrimaryKey = true },
                        new() { Name = "Name", DataType = "nvarchar(100)" }
                    }
                }
            }
        };

        // Act - Should not throw
        var result = await _sut.NaturalLanguageToSqlAsync("Get all users", schema);

        // Assert - Will fail due to no config, but validates input handling
        result.Success.Should().BeFalse();
    }

    #endregion

    #region ExplainSql Tests

    [Fact]
    public async Task ExplainSqlAsync_EmptySql_ReturnsError()
    {
        // Act
        var result = await _sut.ExplainSqlAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExplainSqlAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.ExplainSqlAsync("SELECT * FROM Users");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Theory]
    [InlineData(ExplanationLevel.Brief)]
    [InlineData(ExplanationLevel.Detailed)]
    [InlineData(ExplanationLevel.Educational)]
    public async Task ExplainSqlAsync_AcceptsAllExplanationLevels(ExplanationLevel level)
    {
        // Act
        var result = await _sut.ExplainSqlAsync("SELECT 1", level);

        // Assert - validates level is accepted
        result.Success.Should().BeFalse(); // Not configured, but didn't throw
    }

    #endregion

    #region SuggestOptimizations Tests

    [Fact]
    public async Task SuggestOptimizationsAsync_EmptySql_ReturnsError()
    {
        // Act
        var result = await _sut.SuggestOptimizationsAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task SuggestOptimizationsAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.SuggestOptimizationsAsync("SELECT * FROM Users WHERE Name LIKE '%test%'");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    #endregion

    #region FixSqlError Tests

    [Fact]
    public async Task FixSqlErrorAsync_EmptySql_ReturnsError()
    {
        // Act
        var result = await _sut.FixSqlErrorAsync("", "Some error");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SQL query cannot be empty");
    }

    [Fact]
    public async Task FixSqlErrorAsync_EmptyErrorMessage_ReturnsError()
    {
        // Act
        var result = await _sut.FixSqlErrorAsync("SELECT * FROM", "");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Error message cannot be empty");
    }

    [Fact]
    public async Task FixSqlErrorAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.FixSqlErrorAsync("SELECT * FROM", "Syntax error near FROM");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    #endregion

    #region GenerateSql Tests

    [Fact]
    public async Task GenerateSqlAsync_EmptyTemplate_ReturnsError()
    {
        // Act
        var result = await _sut.GenerateSqlAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task GenerateSqlAsync_WithParameters_AcceptsParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string>
        {
            ["table"] = "Users",
            ["column"] = "Name"
        };

        // Act
        var result = await _sut.GenerateSqlAsync("Select from {table} where {column} is not null", parameters);

        // Assert
        result.Success.Should().BeFalse(); // Not configured
    }

    #endregion

    #region Chat Tests

    [Fact]
    public async Task ChatAsync_EmptyMessage_ReturnsError()
    {
        // Act
        var result = await _sut.ChatAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ChatAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.ChatAsync("How do I write a JOIN?");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public void ClearConversation_DoesNotThrow()
    {
        // Act & Assert - should not throw
        var act = () => _sut.ClearConversation();
        act.Should().NotThrow();
    }

    #endregion

    #region TestConnection Tests

    [Fact]
    public async Task TestConnectionAsync_NotConfigured_ReturnsError()
    {
        // Act
        var result = await _sut.TestConnectionAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    #endregion

    #region AiResponse Tests

    [Fact]
    public void AiResponse_Ok_SetsSuccess()
    {
        // Act
        var response = AiResponse.Ok("Test content");

        // Assert
        response.Success.Should().BeTrue();
        response.Content.Should().Be("Test content");
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void AiResponse_Error_SetsErrorMessage()
    {
        // Act
        var response = AiResponse.Error("Test error");

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("Test error");
    }

    #endregion

    #region AiProviderSettings Tests

    [Fact]
    public void AiProviderSettings_DefaultEndpoint_IsOpenAI()
    {
        // Arrange
        var settings = new AiProviderSettings();

        // Assert
        settings.Endpoint.Should().Contain("openai.com");
    }

    [Fact]
    public void AiProviderSettings_DefaultModel_IsSet()
    {
        // Arrange
        var settings = new AiProviderSettings();

        // Assert
        settings.Model.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region AiSchemaContext Tests

    [Fact]
    public void AiSchemaContext_CanContainTables()
    {
        // Arrange
        var schema = new AiSchemaContext
        {
            DatabaseName = "TestDB",
            Tables = new List<AiTableInfo>
            {
                new() { Name = "Users", Schema = "dbo" },
                new() { Name = "Orders", Schema = "dbo" }
            }
        };

        // Assert
        schema.Tables.Should().HaveCount(2);
        schema.DatabaseName.Should().Be("TestDB");
    }

    [Fact]
    public void AiTableInfo_CanContainColumns()
    {
        // Arrange
        var table = new AiTableInfo
        {
            Name = "Users",
            Schema = "dbo",
            Columns = new List<AiColumnInfo>
            {
                new() { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new() { Name = "Email", DataType = "nvarchar(255)", IsNullable = false }
            }
        };

        // Assert
        table.Columns.Should().HaveCount(2);
        table.Columns.Should().Contain(c => c.IsPrimaryKey);
    }

    #endregion

    #region Provider Enum Tests

    [Fact]
    public void AiProvider_HasAllProviders()
    {
        // Assert
        Enum.GetValues<AiProvider>().Should().Contain(AiProvider.OpenAI);
        Enum.GetValues<AiProvider>().Should().Contain(AiProvider.Anthropic);
        Enum.GetValues<AiProvider>().Should().Contain(AiProvider.AzureOpenAI);
    }

    #endregion

    #region ExplanationLevel Enum Tests

    [Fact]
    public void ExplanationLevel_HasAllLevels()
    {
        // Assert
        Enum.GetValues<ExplanationLevel>().Should().Contain(ExplanationLevel.Brief);
        Enum.GetValues<ExplanationLevel>().Should().Contain(ExplanationLevel.Detailed);
        Enum.GetValues<ExplanationLevel>().Should().Contain(ExplanationLevel.Educational);
    }

    #endregion
}

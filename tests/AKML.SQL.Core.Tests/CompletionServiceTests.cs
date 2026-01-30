using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests;

public class CompletionServiceTests
{
    private readonly CompletionService _sut;
    private readonly Mock<ILogger<CompletionService>> _loggerMock;
    private readonly Mock<IDocumentManager> _documentManagerMock;
    private readonly Mock<IMetadataService> _metadataServiceMock;
    private readonly Mock<ISqlParserService> _parserServiceMock;

    public CompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<CompletionService>>();
        _documentManagerMock = new Mock<IDocumentManager>();
        _metadataServiceMock = new Mock<IMetadataService>();
        _parserServiceMock = new Mock<ISqlParserService>();

        _sut = new CompletionService(
            _loggerMock.Object,
            _documentManagerMock.Object,
            _metadataServiceMock.Object,
            _parserServiceMock.Object);
    }

    [Fact]
    public async Task GetCompletionsAsync_WithSelectKeyword_ReturnsKeywordCompletions()
    {
        // Arrange
        var text = "SEL";
        
        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 3, null, null, 50);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.Label == "SELECT");
    }

    [Fact]
    public async Task GetCompletionsAsync_WithEmptyText_ReturnsAllKeywords()
    {
        // Arrange
        var text = "";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 0, null, null, 100);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.Label == "SELECT");
        result.Should().Contain(c => c.Label == "FROM");
        result.Should().Contain(c => c.Label == "WHERE");
    }

    [Fact]
    public async Task GetCompletionsAsync_WithFunctionPrefix_ReturnsFunctions()
    {
        // Arrange
        var text = "SELECT COUNT";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 12, null, null, 50);

        // Assert
        result.Should().Contain(c => c.Label == "COUNT" && 
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_RespectsMaxItems()
    {
        // Arrange
        var text = "";
        var maxItems = 10;

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 0, null, null, maxItems);

        // Assert
        result.Should().HaveCountLessThanOrEqualTo(maxItems);
    }

    [Fact]
    public async Task GetCompletionsAsync_SortsByRelevance()
    {
        // Arrange
        var text = "SEL";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 3, null, null, 50);

        // Assert
        var scores = result.Select(c => c.RelevanceScore).ToList();
        scores.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetCompletionsAsync_WithPartialMatch_UsesFuzzyMatching()
    {
        // Arrange - "SLCT" should still match "SELECT" with fuzzy matching
        var text = "SLCT";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 4, null, null, 50);

        // Assert
        result.Should().Contain(c => c.Label == "SELECT");
    }
}

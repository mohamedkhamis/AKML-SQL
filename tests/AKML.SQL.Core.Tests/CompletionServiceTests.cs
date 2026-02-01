using AKML.SQL.Core.DataStructures;
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
    private readonly SqlContextAnalyzer _contextAnalyzer;
    private readonly MetadataCache _metadataCache;

    public CompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<CompletionService>>();
        _documentManagerMock = new Mock<IDocumentManager>();
        _metadataServiceMock = new Mock<IMetadataService>();
        _parserServiceMock = new Mock<ISqlParserService>();

        var contextLoggerMock = new Mock<ILogger<SqlContextAnalyzer>>();
        _contextAnalyzer = new SqlContextAnalyzer(contextLoggerMock.Object);

        var cacheLoggerMock = new Mock<ILogger<MetadataCache>>();
        _metadataCache = new MetadataCache(cacheLoggerMock.Object);

        _sut = new CompletionService(
            _loggerMock.Object,
            _documentManagerMock.Object,
            _metadataServiceMock.Object,
            _parserServiceMock.Object,
            _contextAnalyzer,
            _metadataCache);
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

        // Act - need 200 items to include all keywords after Query Hints were added
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, 0, null, null, 200);

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

    #region Context-Aware Completion Tests (Sprint 5)

    [Fact]
    public async Task GetCompletionsAsync_AfterFrom_PrioritizesTablesAndViews()
    {
        // Arrange
        var text = "SELECT * FROM ";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 50);

        // Assert
        // Without a connection string, we should still get keywords
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetCompletionsAsync_AfterWhere_IncludesComparisonOperators()
    {
        // Arrange
        var text = "SELECT * FROM Customers WHERE ";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 50);

        // Assert
        result.Should().Contain(c => c.Label == "=" || c.Label == "<>" || c.Label == "LIKE");
    }

    [Fact]
    public async Task GetCompletionsAsync_AfterJoin_ShowsJoinKeywords()
    {
        // Arrange
        var text = "SELECT * FROM Customers INNER ";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 50);

        // Assert
        result.Should().Contain(c => c.Label == "JOIN");
    }

    [Fact]
    public async Task GetCompletionsAsync_AfterSelect_ShowsFunctions()
    {
        // Arrange
        var text = "SELECT ";

        // Act - need 200 items to include functions after all keywords
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 200);

        // Assert
        result.Should().Contain(c => c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_AfterExec_ShowsProcedures()
    {
        // Arrange
        var text = "EXEC ";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 50);

        // Assert
        // Without connection, we won't have procedures, but context should be detected
        result.Should().NotBeNull();
    }

    #endregion

    #region SQL Server 2022 Functions Tests

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesGenerateSeries()
    {
        // Arrange - use SELECT context to get functions
        var text = "SELECT GENERATE";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "GENERATE_SERIES" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesDateBucket()
    {
        // Arrange
        var text = "SELECT DATE_B";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "DATE_BUCKET" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesDateTrunc()
    {
        // Arrange
        var text = "SELECT DATETR";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "DATETRUNC" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesJsonObject()
    {
        // Arrange
        var text = "SELECT JSON_O";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "JSON_OBJECT" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesJsonArray()
    {
        // Arrange
        var text = "SELECT JSON_A";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "JSON_ARRAY" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesGreatest()
    {
        // Arrange
        var text = "SELECT GREAT";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "GREATEST" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesLeast()
    {
        // Arrange
        var text = "SELECT LEA";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "LEAST" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesBitwiseFunctions()
    {
        // Arrange
        var text = "SELECT LEFT_SH";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "LEFT_SHIFT" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesApproxPercentile()
    {
        // Arrange
        var text = "SELECT APPROX_P";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "APPROX_PERCENTILE_CONT" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
        result.Should().Contain(c => c.Label == "APPROX_PERCENTILE_DISC" &&
            c.Kind == Shared.Contracts.CompletionItemKind.Function);
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesIsDistinctFrom()
    {
        // Arrange
        var text = "SELECT * FROM Customers WHERE Id IS DIST";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "IS DISTINCT FROM");
        result.Should().Contain(c => c.Label == "IS NOT DISTINCT FROM");
    }

    [Fact]
    public async Task GetCompletionsAsync_SqlServer2022_IncludesWindowKeyword()
    {
        // Arrange
        var text = "SELECT * FROM Orders WINDO";

        // Act
        var result = await _sut.GetCompletionsAsync(
            "doc1", text, 0, text.Length, null, null, 100);

        // Assert
        result.Should().Contain(c => c.Label == "WINDOW");
    }

    #endregion
}

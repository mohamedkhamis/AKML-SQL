using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for QueryHistoryService.
/// Covers Story 8.1: Query History Management.
/// </summary>
public class QueryHistoryServiceTests
{
    private readonly QueryHistoryService _sut;

    public QueryHistoryServiceTests()
    {
        var loggerMock = new Mock<ILogger<QueryHistoryService>>();
        _sut = new QueryHistoryService(loggerMock.Object, maxEntries: 100);
    }

    #region AddEntry Tests

    [Fact]
    public void AddEntry_ValidEntry_AddsAndReturnsEntry()
    {
        // Arrange
        var entry = new QueryHistoryEntry
        {
            Query = "SELECT * FROM Users",
            ServerName = "localhost",
            DatabaseName = "TestDB",
            WasSuccessful = true
        };

        // Act
        var result = _sut.AddEntry(entry);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.ExecutedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AddEntry_NullEntry_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.AddEntry(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddEntry_EmptyQuery_ThrowsException()
    {
        // Arrange
        var entry = new QueryHistoryEntry { Query = "" };

        // Act & Assert
        var act = () => _sut.AddEntry(entry);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public void GetAll_ReturnsEntriesOrderedByDate()
    {
        // Arrange
        AddTestEntries(5);

        // Act
        var entries = _sut.GetAll();

        // Assert
        entries.Should().HaveCountGreaterThanOrEqualTo(5);
        entries.Should().BeInDescendingOrder(e => e.ExecutedAt);
    }

    [Fact]
    public void GetAll_WithLimit_RespectsLimit()
    {
        // Arrange
        AddTestEntries(10);

        // Act
        var entries = _sut.GetAll(limit: 5);

        // Assert
        entries.Should().HaveCount(5);
    }

    [Fact]
    public void GetAll_WithSkip_SkipsEntries()
    {
        // Arrange
        AddTestEntries(10);

        // Act
        var allEntries = _sut.GetAll();
        var skippedEntries = _sut.GetAll(skip: 5);

        // Assert
        skippedEntries.Should().HaveCount(allEntries.Count - 5);
    }

    #endregion

    #region Search Tests

    [Fact]
    public void Search_MatchingQuery_ReturnsResults()
    {
        // Arrange
        _sut.AddEntry(new QueryHistoryEntry { Query = "SELECT * FROM Users" });
        _sut.AddEntry(new QueryHistoryEntry { Query = "INSERT INTO Orders" });

        // Act
        var results = _sut.Search("Users");

        // Assert
        results.Should().Contain(e => e.Query.Contains("Users"));
        results.Should().NotContain(e => e.Query.Contains("Orders"));
    }

    [Fact]
    public void Search_CaseInsensitive_ReturnsResults()
    {
        // Arrange
        _sut.AddEntry(new QueryHistoryEntry { Query = "SELECT * FROM USERS" });

        // Act
        var results = _sut.Search("users");

        // Assert
        results.Should().ContainSingle();
    }

    [Fact]
    public void Search_EmptyText_ReturnsAll()
    {
        // Arrange
        AddTestEntries(5);

        // Act
        var results = _sut.Search("");

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(5);
    }

    #endregion

    #region GetByConnection Tests

    [Fact]
    public void GetByConnection_ByServer_FiltersCorrectly()
    {
        // Arrange
        _sut.AddEntry(new QueryHistoryEntry { Query = "Q1", ServerName = "Server1", DatabaseName = "DB1" });
        _sut.AddEntry(new QueryHistoryEntry { Query = "Q2", ServerName = "Server2", DatabaseName = "DB1" });

        // Act
        var results = _sut.GetByConnection("Server1", null);

        // Assert
        results.Should().OnlyContain(e => e.ServerName == "Server1");
    }

    [Fact]
    public void GetByConnection_ByDatabase_FiltersCorrectly()
    {
        // Arrange
        _sut.AddEntry(new QueryHistoryEntry { Query = "Q1", ServerName = "S1", DatabaseName = "Database1" });
        _sut.AddEntry(new QueryHistoryEntry { Query = "Q2", ServerName = "S1", DatabaseName = "Database2" });

        // Act
        var results = _sut.GetByConnection(null, "Database1");

        // Assert
        results.Should().OnlyContain(e => e.DatabaseName == "Database1");
    }

    #endregion

    #region Favorites Tests

    [Fact]
    public void ToggleFavorite_SetsAndUnsets()
    {
        // Arrange
        var entry = _sut.AddEntry(new QueryHistoryEntry { Query = "SELECT 1" });

        // Act & Assert
        var isFavorite = _sut.ToggleFavorite(entry.Id);
        isFavorite.Should().BeTrue();
        _sut.GetById(entry.Id)!.IsFavorite.Should().BeTrue();

        isFavorite = _sut.ToggleFavorite(entry.Id);
        isFavorite.Should().BeFalse();
        _sut.GetById(entry.Id)!.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public void GetFavorites_ReturnsOnlyFavorites()
    {
        // Arrange
        var entry1 = _sut.AddEntry(new QueryHistoryEntry { Query = "Q1" });
        var entry2 = _sut.AddEntry(new QueryHistoryEntry { Query = "Q2" });
        _sut.ToggleFavorite(entry1.Id);

        // Act
        var favorites = _sut.GetFavorites();

        // Assert
        favorites.Should().Contain(e => e.Id == entry1.Id);
        favorites.Should().NotContain(e => e.Id == entry2.Id);
    }

    #endregion

    #region Delete and Clear Tests

    [Fact]
    public void Delete_ExistingEntry_Removes()
    {
        // Arrange
        var entry = _sut.AddEntry(new QueryHistoryEntry { Query = "To Delete" });

        // Act
        var result = _sut.Delete(entry.Id);

        // Assert
        result.Should().BeTrue();
        _sut.GetById(entry.Id).Should().BeNull();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        // Act
        var result = _sut.Delete(999999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Clear_KeepFavorites_PreservesFavorites()
    {
        // Arrange
        var fav = _sut.AddEntry(new QueryHistoryEntry { Query = "Favorite" });
        _sut.ToggleFavorite(fav.Id);
        var notFav = _sut.AddEntry(new QueryHistoryEntry { Query = "Not Favorite" });

        // Act
        _sut.Clear(keepFavorites: true);

        // Assert
        _sut.GetById(fav.Id).Should().NotBeNull();
        _sut.GetById(notFav.Id).Should().BeNull();
    }

    [Fact]
    public void Clear_NoKeepFavorites_ClearsAll()
    {
        // Arrange
        var fav = _sut.AddEntry(new QueryHistoryEntry { Query = "Favorite" });
        _sut.ToggleFavorite(fav.Id);

        // Act
        _sut.Clear(keepFavorites: false);

        // Assert
        _sut.Count.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private void AddTestEntries(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _sut.AddEntry(new QueryHistoryEntry
            {
                Query = $"SELECT {i} FROM Test",
                ServerName = "TestServer",
                DatabaseName = "TestDB"
            });
        }
    }

    #endregion
}

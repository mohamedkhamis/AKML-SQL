using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for PerformanceService.
/// Covers Story 11.1: Performance Monitoring.
/// </summary>
public class PerformanceServiceTests
{
    private readonly PerformanceService _sut;

    public PerformanceServiceTests()
    {
        var loggerMock = new Mock<ILogger<PerformanceService>>();
        _sut = new PerformanceService(loggerMock.Object);
    }

    #region Enable/Disable Tests

    [Fact]
    public void IsEnabled_Default_ReturnsTrue()
    {
        // Assert
        _sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SetEnabled_False_DisablesMonitoring()
    {
        // Act
        _sut.SetEnabled(false);

        // Assert
        _sut.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_True_EnablesMonitoring()
    {
        // Arrange
        _sut.SetEnabled(false);

        // Act
        _sut.SetEnabled(true);

        // Assert
        _sut.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region StartOperation Tests

    [Fact]
    public void StartOperation_ReturnsDisposable()
    {
        // Act
        using var operation = _sut.StartOperation("TestOp");

        // Assert
        operation.Should().NotBeNull();
    }

    [Fact]
    public void StartOperation_WhenDisabled_ReturnsNoOp()
    {
        // Arrange
        _sut.SetEnabled(false);

        // Act
        using var operation = _sut.StartOperation("TestOp");

        // Assert - should not record metrics
        Thread.Sleep(10);
        var metric = _sut.GetMetric("TestOp");
        metric.Should().BeNull();
    }

    [Fact]
    public void StartOperation_RecordsTimingOnDispose()
    {
        // Act
        using (_sut.StartOperation("TestOperation"))
        {
            Thread.Sleep(5);
        }

        // Assert
        var metric = _sut.GetMetric("TestOperation");
        metric.Should().NotBeNull();
        metric!.TotalCount.Should().Be(1);
    }

    #endregion

    #region RecordTiming Tests

    [Fact]
    public void RecordTiming_CreatesMetric()
    {
        // Act
        _sut.RecordTiming("ManualOp", TimeSpan.FromMilliseconds(50));

        // Assert
        var metric = _sut.GetMetric("ManualOp");
        metric.Should().NotBeNull();
        metric!.TotalCount.Should().Be(1);
    }

    [Fact]
    public void RecordTiming_UpdatesExistingMetric()
    {
        // Act
        _sut.RecordTiming("RepeatedOp", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("RepeatedOp", TimeSpan.FromMilliseconds(20));
        _sut.RecordTiming("RepeatedOp", TimeSpan.FromMilliseconds(30));

        // Assert
        var metric = _sut.GetMetric("RepeatedOp");
        metric!.TotalCount.Should().Be(3);
        metric.MinDuration.TotalMilliseconds.Should().Be(10);
        metric.MaxDuration.TotalMilliseconds.Should().Be(30);
    }

    [Fact]
    public void RecordTiming_WhenDisabled_DoesNotRecord()
    {
        // Arrange
        _sut.SetEnabled(false);

        // Act
        _sut.RecordTiming("DisabledOp", TimeSpan.FromMilliseconds(50));

        // Assert
        var metric = _sut.GetMetric("DisabledOp");
        metric.Should().BeNull();
    }

    #endregion

    #region GetMetric Tests

    [Fact]
    public void GetMetric_NonExistent_ReturnsNull()
    {
        // Act
        var metric = _sut.GetMetric("NonExistent");

        // Assert
        metric.Should().BeNull();
    }

    [Fact]
    public void GetMetric_CalculatesAverage()
    {
        // Arrange
        _sut.RecordTiming("AvgOp", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("AvgOp", TimeSpan.FromMilliseconds(30));

        // Act
        var metric = _sut.GetMetric("AvgOp");

        // Assert
        metric!.AverageDuration.TotalMilliseconds.Should().Be(20);
    }

    #endregion

    #region GetAllMetrics Tests

    [Fact]
    public void GetAllMetrics_ReturnsAllRecorded()
    {
        // Arrange
        _sut.RecordTiming("Op1", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Op2", TimeSpan.FromMilliseconds(20));
        _sut.RecordTiming("Op3", TimeSpan.FromMilliseconds(30));

        // Act
        var metrics = _sut.GetAllMetrics();

        // Assert
        metrics.Should().HaveCount(3);
    }

    [Fact]
    public void GetAllMetrics_OrderedByCount()
    {
        // Arrange
        _sut.RecordTiming("LessFrequent", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("MoreFrequent", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("MoreFrequent", TimeSpan.FromMilliseconds(10));

        // Act
        var metrics = _sut.GetAllMetrics();

        // Assert
        metrics[0].OperationName.Should().Be("MoreFrequent");
    }

    #endregion

    #region GetRecentTimings Tests

    [Fact]
    public void GetRecentTimings_ReturnsRecent()
    {
        // Arrange
        _sut.RecordTiming("Recent1", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Recent2", TimeSpan.FromMilliseconds(20));

        // Act
        var timings = _sut.GetRecentTimings();

        // Assert
        timings.Should().HaveCount(2);
    }

    [Fact]
    public void GetRecentTimings_RespectsCount()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _sut.RecordTiming($"Op{i}", TimeSpan.FromMilliseconds(i));
        }

        // Act
        var timings = _sut.GetRecentTimings(5);

        // Assert
        timings.Should().HaveCount(5);
    }

    #endregion

    #region GetSlowOperations Tests

    [Fact]
    public void GetSlowOperations_FiltersAboveThreshold()
    {
        // Arrange
        _sut.RecordTiming("Fast", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Slow", TimeSpan.FromMilliseconds(200));
        _sut.RecordTiming("VerySlow", TimeSpan.FromMilliseconds(500));

        // Act
        var slowOps = _sut.GetSlowOperations(TimeSpan.FromMilliseconds(100));

        // Assert
        slowOps.Should().HaveCount(2);
        slowOps.Should().Contain(t => t.OperationName == "Slow");
        slowOps.Should().Contain(t => t.OperationName == "VerySlow");
    }

    [Fact]
    public void GetSlowOperations_OrderedByDuration()
    {
        // Arrange
        _sut.RecordTiming("Slow", TimeSpan.FromMilliseconds(200));
        _sut.RecordTiming("VerySlow", TimeSpan.FromMilliseconds(500));

        // Act
        var slowOps = _sut.GetSlowOperations(TimeSpan.FromMilliseconds(100));

        // Assert
        slowOps[0].OperationName.Should().Be("VerySlow");
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        // Arrange
        _sut.RecordTiming("ToBeCleared", TimeSpan.FromMilliseconds(10));

        // Act
        _sut.Reset();

        // Assert
        _sut.GetAllMetrics().Should().BeEmpty();
    }

    [Fact]
    public void Reset_ClearsRecentTimings()
    {
        // Arrange
        _sut.RecordTiming("ToBeCleared", TimeSpan.FromMilliseconds(10));

        // Act
        _sut.Reset();

        // Assert
        _sut.GetRecentTimings().Should().BeEmpty();
    }

    #endregion

    #region GetSummary Tests

    [Fact]
    public void GetSummary_CalculatesTotals()
    {
        // Arrange
        _sut.RecordTiming("Op1", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Op2", TimeSpan.FromMilliseconds(20));

        // Act
        var summary = _sut.GetSummary();

        // Assert
        summary.TotalOperations.Should().Be(2);
        summary.UniqueOperations.Should().Be(2);
    }

    [Fact]
    public void GetSummary_IdentifiesSlowest()
    {
        // Arrange
        _sut.RecordTiming("Fast", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Slowest", TimeSpan.FromMilliseconds(1000));

        // Act
        var summary = _sut.GetSummary();

        // Assert
        summary.SlowestOperation.Should().Be("Slowest");
    }

    [Fact]
    public void GetSummary_IdentifiesMostFrequent()
    {
        // Arrange
        _sut.RecordTiming("Frequent", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Frequent", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Frequent", TimeSpan.FromMilliseconds(10));
        _sut.RecordTiming("Less", TimeSpan.FromMilliseconds(10));

        // Act
        var summary = _sut.GetSummary();

        // Assert
        summary.MostFrequentOperation.Should().Be("Frequent");
    }

    #endregion

    #region PerformanceMetric Tests

    [Fact]
    public void PerformanceMetric_AverageDuration_CalculatesCorrectly()
    {
        // Arrange
        var metric = new PerformanceMetric
        {
            TotalCount = 4,
            TotalDuration = TimeSpan.FromMilliseconds(100)
        };

        // Assert
        metric.AverageDuration.TotalMilliseconds.Should().Be(25);
    }

    [Fact]
    public void PerformanceMetric_AverageDuration_ZeroCount_ReturnsZero()
    {
        // Arrange
        var metric = new PerformanceMetric { TotalCount = 0 };

        // Assert
        metric.AverageDuration.Should().Be(TimeSpan.Zero);
    }

    #endregion
}

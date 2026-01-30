using AKML.SQL.Core.Services;
using AKML.SQL.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for ReleaseInfoService.
/// Covers Story 13.1: Release Management.
/// </summary>
public class ReleaseInfoServiceTests
{
    private readonly ReleaseInfoService _sut;

    public ReleaseInfoServiceTests()
    {
        var loggerMock = new Mock<ILogger<ReleaseInfoService>>();
        _sut = new ReleaseInfoService(loggerMock.Object);
    }

    #region GetVersion Tests

    [Fact]
    public void GetVersion_ReturnsProductVersion()
    {
        // Act
        var version = _sut.GetVersion();

        // Assert
        version.Should().Be(AkmlConstants.ProductVersion);
    }

    [Fact]
    public void GetVersion_ReturnsValidSemanticVersion()
    {
        // Act
        var version = _sut.GetVersion();

        // Assert
        var parts = version.Split('.');
        parts.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    #endregion

    #region GetProductName Tests

    [Fact]
    public void GetProductName_ReturnsAkmlSql()
    {
        // Act
        var name = _sut.GetProductName();

        // Assert
        name.Should().Be(AkmlConstants.ProductName);
    }

    #endregion

    #region GetReleaseInfo Tests

    [Fact]
    public void GetReleaseInfo_ReturnsValidInfo()
    {
        // Act
        var info = _sut.GetReleaseInfo();

        // Assert
        info.Should().NotBeNull();
        info.Version.Should().NotBeNullOrEmpty();
        info.ProductName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetReleaseInfo_HasBuildNumber()
    {
        // Act
        var info = _sut.GetReleaseInfo();

        // Assert
        info.BuildNumber.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetReleaseInfo_HasSupportedSsmsVersions()
    {
        // Act
        var info = _sut.GetReleaseInfo();

        // Assert
        info.SupportedSsmsVersions.Should().NotBeEmpty();
        info.SupportedSsmsVersions.Should().Contain("SSMS 19");
    }

    [Fact]
    public void GetReleaseInfo_HasFeatures()
    {
        // Act
        var info = _sut.GetReleaseInfo();

        // Assert
        info.Features.Should().NotBeEmpty();
    }

    #endregion

    #region GetFeatureList Tests

    [Fact]
    public void GetFeatureList_ReturnsAllFeatures()
    {
        // Act
        var features = _sut.GetFeatureList();

        // Assert
        features.Should().NotBeEmpty();
        features.Count.Should().BeGreaterThan(20);
    }

    [Fact]
    public void GetFeatureList_HasIntelliSenseFeatures()
    {
        // Act
        var features = _sut.GetFeatureList();

        // Assert
        features.Should().Contain(f => f.Category == FeatureCategory.IntelliSense);
    }

    [Fact]
    public void GetFeatureList_HasFormattingFeatures()
    {
        // Act
        var features = _sut.GetFeatureList();

        // Assert
        features.Should().Contain(f => f.Category == FeatureCategory.Formatting);
    }

    [Fact]
    public void GetFeatureList_HasAiFeatures()
    {
        // Act
        var features = _sut.GetFeatureList();

        // Assert
        features.Should().Contain(f => f.Category == FeatureCategory.AI);
    }

    [Fact]
    public void GetFeatureList_HasCoreFeatures()
    {
        // Act
        var features = _sut.GetFeatureList();

        // Assert
        features.Should().Contain(f => f.IsCore);
    }

    [Fact]
    public void GetFeatureList_AiFeaturesRequirePro()
    {
        // Act
        var features = _sut.GetFeatureList();
        var aiFeatures = features.Where(f => f.Category == FeatureCategory.AI);

        // Assert
        aiFeatures.Should().OnlyContain(f => f.RequiresLicense == LicenseType.Professional);
    }

    #endregion

    #region GetFeaturesByCategory Tests

    [Fact]
    public void GetFeaturesByCategory_ReturnsOnlyMatchingCategory()
    {
        // Act
        var features = _sut.GetFeaturesByCategory(FeatureCategory.Refactoring);

        // Assert
        features.Should().NotBeEmpty();
        features.Should().OnlyContain(f => f.Category == FeatureCategory.Refactoring);
    }

    [Fact]
    public void GetFeaturesByCategory_IntelliSense_ReturnsMultiple()
    {
        // Act
        var features = _sut.GetFeaturesByCategory(FeatureCategory.IntelliSense);

        // Assert
        features.Count.Should().BeGreaterThan(1);
    }

    #endregion

    #region GetFeaturesBySprint Tests

    [Fact]
    public void GetFeaturesBySprint_Sprint5_ReturnsIntelliSense()
    {
        // Act
        var features = _sut.GetFeaturesBySprint(5);

        // Assert
        features.Should().Contain(f => f.Name.Contains("IntelliSense"));
    }

    [Fact]
    public void GetFeaturesBySprint_Sprint10_ReturnsAi()
    {
        // Act
        var features = _sut.GetFeaturesBySprint(10);

        // Assert
        features.Should().Contain(f => f.Category == FeatureCategory.AI);
    }

    [Fact]
    public void GetFeaturesBySprint_InvalidSprint_ReturnsEmpty()
    {
        // Act
        var features = _sut.GetFeaturesBySprint(999);

        // Assert
        features.Should().BeEmpty();
    }

    #endregion

    #region GetProductStats Tests

    [Fact]
    public void GetProductStats_ReturnsValidStats()
    {
        // Act
        var stats = _sut.GetProductStats();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalFeatures.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetProductStats_HasCategories()
    {
        // Act
        var stats = _sut.GetProductStats();

        // Assert
        stats.Categories.Should().BeGreaterThan(5);
    }

    [Fact]
    public void GetProductStats_HasAnalysisRules()
    {
        // Act
        var stats = _sut.GetProductStats();

        // Assert
        stats.AnalysisRules.Should().Be(10);
    }

    [Fact]
    public void GetProductStats_HasBuiltInSnippets()
    {
        // Act
        var stats = _sut.GetProductStats();

        // Assert
        stats.BuiltInSnippets.Should().Be(20);
    }

    [Fact]
    public void GetProductStats_HasColorRules()
    {
        // Act
        var stats = _sut.GetProductStats();

        // Assert
        stats.ColorRules.Should().Be(7);
    }

    #endregion

    #region CheckHealth Tests

    [Fact]
    public void CheckHealth_ReturnsResult()
    {
        // Act
        var result = _sut.CheckHealth();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void CheckHealth_HasTimestamp()
    {
        // Act
        var result = _sut.CheckHealth();

        // Assert
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CheckHealth_HasVersion()
    {
        // Act
        var result = _sut.CheckHealth();

        // Assert
        result.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CheckHealth_HasChecks()
    {
        // Act
        var result = _sut.CheckHealth();

        // Assert
        result.Checks.Should().NotBeEmpty();
    }

    [Fact]
    public void CheckHealth_CoreServiceIsHealthy()
    {
        // Act
        var result = _sut.CheckHealth();

        // Assert
        result.Checks.Should().Contain(c => c.Name == "Core Service" && c.Status == HealthStatus.Healthy);
    }

    #endregion

    #region GetCopyright Tests

    [Fact]
    public void GetCopyright_IncludesYear()
    {
        // Act
        var copyright = _sut.GetCopyright();

        // Assert
        copyright.Should().Contain(DateTime.UtcNow.Year.ToString());
    }

    [Fact]
    public void GetCopyright_IncludesCompanyName()
    {
        // Act
        var copyright = _sut.GetCopyright();

        // Assert
        copyright.Should().Contain("Azka Innovation");
    }

    #endregion

    #region GetSupportInfo Tests

    [Fact]
    public void GetSupportInfo_HasEmail()
    {
        // Act
        var info = _sut.GetSupportInfo();

        // Assert
        info.Email.Should().NotBeNullOrEmpty();
        info.Email.Should().Contain("@");
    }

    [Fact]
    public void GetSupportInfo_HasWebsite()
    {
        // Act
        var info = _sut.GetSupportInfo();

        // Assert
        info.Website.Should().StartWith("https://");
    }

    [Fact]
    public void GetSupportInfo_HasDocumentation()
    {
        // Act
        var info = _sut.GetSupportInfo();

        // Assert
        info.Documentation.Should().StartWith("https://");
    }

    #endregion

    #region Model Tests

    [Fact]
    public void FeatureInfo_DefaultValues()
    {
        // Arrange
        var feature = new FeatureInfo();

        // Assert
        feature.Name.Should().Be(string.Empty);
        feature.IsCore.Should().BeFalse();
    }

    [Fact]
    public void ReleaseInfo_DefaultValues()
    {
        // Arrange
        var info = new ReleaseInfo();

        // Assert
        info.Version.Should().Be(string.Empty);
        info.SupportedSsmsVersions.Should().BeEmpty();
    }

    [Fact]
    public void HealthCheckResult_DefaultValues()
    {
        // Arrange
        var result = new HealthCheckResult();

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.Checks.Should().NotBeNull();
    }

    #endregion

    #region Enum Tests

    [Fact]
    public void FeatureCategory_HasAllCategories()
    {
        // Assert
        Enum.GetValues<FeatureCategory>().Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public void HealthStatus_HasAllStatuses()
    {
        // Assert
        Enum.GetValues<HealthStatus>().Should().Contain(HealthStatus.Healthy);
        Enum.GetValues<HealthStatus>().Should().Contain(HealthStatus.Degraded);
        Enum.GetValues<HealthStatus>().Should().Contain(HealthStatus.Unhealthy);
    }

    #endregion
}

using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for LicenseService.
/// Covers Story 12.1: Licensing.
/// </summary>
public class LicenseServiceTests
{
    private readonly LicenseService _sut;

    public LicenseServiceTests()
    {
        var loggerMock = new Mock<ILogger<LicenseService>>();
        _sut = new LicenseService(loggerMock.Object);
    }

    #region GetCurrentLicense Tests

    [Fact]
    public void GetCurrentLicense_NoLicenseActivated_ReturnsTrial()
    {
        // Act
        var license = _sut.GetCurrentLicense();

        // Assert
        license.Should().NotBeNull();
        license.LicenseType.Should().Be(LicenseType.Trial);
    }

    [Fact]
    public void GetCurrentLicense_TrialLicense_HasExpirationDate()
    {
        // Act
        var license = _sut.GetCurrentLicense();

        // Assert
        license.ExpirationDate.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void GetCurrentLicense_TrialLicense_HasStatus()
    {
        // Act
        var license = _sut.GetCurrentLicense();

        // Assert
        license.Status.Should().Be(LicenseStatus.Active);
    }

    #endregion

    #region ActivateLicense Tests

    [Fact]
    public void ActivateLicense_EmptyKey_ReturnsError()
    {
        // Act
        var result = _sut.ActivateLicense("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void ActivateLicense_NullKey_ReturnsError()
    {
        // Act
        var result = _sut.ActivateLicense(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void ActivateLicense_InvalidFormat_ReturnsError()
    {
        // Act
        var result = _sut.ActivateLicense("invalid-key");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid");
    }

    [Fact]
    public void ActivateLicense_ValidKey_ReturnsSuccess()
    {
        // Arrange
        var key = _sut.GenerateTrialLicenseKey();

        // Act
        var result = _sut.ActivateLicense(key);

        // Assert
        result.Success.Should().BeTrue();
        result.License.Should().NotBeNull();
    }

    [Fact]
    public void ActivateLicense_ValidKey_SetsActivatedAt()
    {
        // Arrange
        var key = _sut.GenerateTrialLicenseKey();

        // Act
        var result = _sut.ActivateLicense(key);

        // Assert
        result.License!.ActivatedAt.Should().NotBeNull();
        result.License.ActivatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region DeactivateLicense Tests

    [Fact]
    public void DeactivateLicense_NoLicense_ReturnsFalse()
    {
        // Act
        var result = _sut.DeactivateLicense();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DeactivateLicense_TrialLicense_ReturnsFalse()
    {
        // Arrange - default is trial
        _sut.GetCurrentLicense();

        // Act
        var result = _sut.DeactivateLicense();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsLicenseValid Tests

    [Fact]
    public void IsLicenseValid_ActiveTrial_ReturnsTrue()
    {
        // Act
        var isValid = _sut.IsLicenseValid();

        // Assert
        isValid.Should().BeTrue();
    }

    #endregion

    #region IsFeatureAvailable Tests

    [Fact]
    public void IsFeatureAvailable_IntelliSense_TrialLicense_ReturnsTrue()
    {
        // Act
        var available = _sut.IsFeatureAvailable(LicenseFeature.IntelliSense);

        // Assert
        available.Should().BeTrue();
    }

    [Fact]
    public void IsFeatureAvailable_AiAssistance_TrialLicense_ReturnsFalse()
    {
        // Act
        var available = _sut.IsFeatureAvailable(LicenseFeature.AiAssistance);

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    public void IsFeatureAvailable_BasicFormatting_TrialLicense_ReturnsTrue()
    {
        // Act
        var available = _sut.IsFeatureAvailable(LicenseFeature.BasicFormatting);

        // Assert
        available.Should().BeTrue();
    }

    #endregion

    #region GetDaysRemaining Tests

    [Fact]
    public void GetDaysRemaining_NewTrial_ReturnsPositive()
    {
        // Act
        var days = _sut.GetDaysRemaining();

        // Assert
        days.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetDaysRemaining_NewTrial_ReturnsAbout14Days()
    {
        // Act
        var days = _sut.GetDaysRemaining();

        // Assert
        days.Should().BeInRange(13, 15); // Allow for timing differences
    }

    #endregion

    #region GenerateTrialLicenseKey Tests

    [Fact]
    public void GenerateTrialLicenseKey_ReturnsNonEmptyString()
    {
        // Act
        var key = _sut.GenerateTrialLicenseKey();

        // Assert
        key.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateTrialLicenseKey_ReturnsBase64String()
    {
        // Act
        var key = _sut.GenerateTrialLicenseKey();

        // Assert - should be valid base64
        var act = () => Convert.FromBase64String(key);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateTrialLicenseKey_CustomDays_SetsExpiration()
    {
        // Arrange
        var key = _sut.GenerateTrialLicenseKey(30);

        // Act
        var result = _sut.ActivateLicense(key);

        // Assert
        var expectedExpiration = DateTime.UtcNow.AddDays(30);
        result.License!.ExpirationDate.Should().BeCloseTo(expectedExpiration, TimeSpan.FromMinutes(1));
    }

    #endregion

    #region GetTrialFeatures Tests

    [Fact]
    public void GetTrialFeatures_IncludesIntelliSense()
    {
        // Act
        var features = _sut.GetTrialFeatures();

        // Assert
        features.Should().Contain(LicenseFeature.IntelliSense);
    }

    [Fact]
    public void GetTrialFeatures_DoesNotIncludeAi()
    {
        // Act
        var features = _sut.GetTrialFeatures();

        // Assert
        features.Should().NotContain(LicenseFeature.AiAssistance);
    }

    #endregion

    #region GetPersonalFeatures Tests

    [Fact]
    public void GetPersonalFeatures_IncludesQueryHistory()
    {
        // Act
        var features = _sut.GetPersonalFeatures();

        // Assert
        features.Should().Contain(LicenseFeature.QueryHistory);
    }

    [Fact]
    public void GetPersonalFeatures_IncludesTabColoring()
    {
        // Act
        var features = _sut.GetPersonalFeatures();

        // Assert
        features.Should().Contain(LicenseFeature.TabColoring);
    }

    #endregion

    #region GetProfessionalFeatures Tests

    [Fact]
    public void GetProfessionalFeatures_IncludesAi()
    {
        // Act
        var features = _sut.GetProfessionalFeatures();

        // Assert
        features.Should().Contain(LicenseFeature.AiAssistance);
    }

    [Fact]
    public void GetProfessionalFeatures_ExcludesEnterpriseSupport()
    {
        // Act
        var features = _sut.GetProfessionalFeatures();

        // Assert
        features.Should().NotContain(LicenseFeature.EnterpriseSupport);
    }

    #endregion

    #region LicenseInfo Tests

    [Fact]
    public void LicenseInfo_DefaultValues_AreSet()
    {
        // Arrange
        var info = new LicenseInfo();

        // Assert
        info.LicenseId.Should().Be(string.Empty);
        info.LicenseType.Should().Be(LicenseType.Trial);
        info.Status.Should().Be(LicenseStatus.Inactive);
        info.MaxDevices.Should().Be(1);
    }

    #endregion

    #region LicenseActivationResult Tests

    [Fact]
    public void LicenseActivationResult_DefaultSuccess_IsFalse()
    {
        // Arrange
        var result = new LicenseActivationResult();

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region Enum Tests

    [Fact]
    public void LicenseType_HasAllTypes()
    {
        // Assert
        Enum.GetValues<LicenseType>().Should().Contain(LicenseType.Trial);
        Enum.GetValues<LicenseType>().Should().Contain(LicenseType.Personal);
        Enum.GetValues<LicenseType>().Should().Contain(LicenseType.Professional);
        Enum.GetValues<LicenseType>().Should().Contain(LicenseType.Enterprise);
    }

    [Fact]
    public void LicenseStatus_HasAllStatuses()
    {
        // Assert
        Enum.GetValues<LicenseStatus>().Should().Contain(LicenseStatus.Active);
        Enum.GetValues<LicenseStatus>().Should().Contain(LicenseStatus.Expired);
        Enum.GetValues<LicenseStatus>().Should().Contain(LicenseStatus.Revoked);
    }

    [Fact]
    public void LicenseFeature_HasAllFeatures()
    {
        // Assert
        Enum.GetValues<LicenseFeature>().Should().HaveCountGreaterThan(5);
    }

    #endregion
}

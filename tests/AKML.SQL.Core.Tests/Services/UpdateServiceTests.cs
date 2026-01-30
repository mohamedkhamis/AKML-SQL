using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for UpdateService.
/// Covers Story 12.2: Auto-Update.
/// </summary>
public class UpdateServiceTests
{
    private readonly UpdateService _sut;

    public UpdateServiceTests()
    {
        var loggerMock = new Mock<ILogger<UpdateService>>();
        _sut = new UpdateService(loggerMock.Object);
    }

    #region Configure Tests

    [Fact]
    public void Configure_ValidSettings_SetsSettings()
    {
        // Arrange
        var settings = new UpdateSettings
        {
            UpdateChannel = UpdateChannel.Beta,
            AutoCheckEnabled = false
        };

        // Act
        _sut.Configure(settings);
        var result = _sut.GetSettings();

        // Assert
        result.UpdateChannel.Should().Be(UpdateChannel.Beta);
        result.AutoCheckEnabled.Should().BeFalse();
    }

    [Fact]
    public void Configure_NullSettings_ThrowsException()
    {
        // Act & Assert
        var act = () => _sut.Configure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetSettings_Default_ReturnsDefaults()
    {
        // Act
        var settings = _sut.GetSettings();

        // Assert
        settings.UpdateChannel.Should().Be(UpdateChannel.Stable);
        settings.AutoCheckEnabled.Should().BeTrue();
        settings.CheckIntervalHours.Should().Be(24);
    }

    #endregion

    #region GetCurrentVersion Tests

    [Fact]
    public void GetCurrentVersion_ReturnsVersion()
    {
        // Act
        var version = _sut.GetCurrentVersion();

        // Assert
        version.Should().NotBeNull();
        version.Major.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsParseableVersion()
    {
        // Act
        var version = _sut.GetCurrentVersion();

        // Assert
        version.ToString().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region CheckForUpdatesAsync Tests

    [Fact]
    public async Task CheckForUpdatesAsync_NoNetwork_ReturnsError()
    {
        // Arrange
        _sut.Configure(new UpdateSettings
        {
            UpdateServerUrl = "https://invalid.nonexistent.domain.test"
        });

        // Act
        var result = await _sut.CheckForUpdatesAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_CanBeCancelled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _sut.CheckForUpdatesAsync(cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
    }

    #endregion

    #region DownloadUpdateAsync Tests

    [Fact]
    public async Task DownloadUpdateAsync_NullInfo_ReturnsError()
    {
        // Act
        var result = await _sut.DownloadUpdateAsync(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public async Task DownloadUpdateAsync_NoDownloadUrl_ReturnsError()
    {
        // Arrange
        var updateInfo = new UpdateInfo { Version = "2.0.0" };

        // Act
        var result = await _sut.DownloadUpdateAsync(updateInfo);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("download URL");
    }

    [Fact]
    public async Task DownloadUpdateAsync_CanBeCancelled()
    {
        // Arrange
        var updateInfo = new UpdateInfo
        {
            Version = "2.0.0",
            DownloadUrl = "https://example.com/update.exe"
        };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _sut.DownloadUpdateAsync(updateInfo, cancellationToken: cts.Token);

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region DismissUpdate Tests

    [Fact]
    public void DismissUpdate_AddsToList()
    {
        // Act
        _sut.DismissUpdate("2.0.0");

        // Assert
        _sut.IsUpdateDismissed("2.0.0").Should().BeTrue();
    }

    [Fact]
    public void IsUpdateDismissed_NotDismissed_ReturnsFalse()
    {
        // Act
        var isDismissed = _sut.IsUpdateDismissed("3.0.0");

        // Assert
        isDismissed.Should().BeFalse();
    }

    [Fact]
    public void DismissUpdate_MultipleDismissals_AllTracked()
    {
        // Act
        _sut.DismissUpdate("2.0.0");
        _sut.DismissUpdate("2.1.0");

        // Assert
        _sut.IsUpdateDismissed("2.0.0").Should().BeTrue();
        _sut.IsUpdateDismissed("2.1.0").Should().BeTrue();
    }

    #endregion

    #region ClearCache Tests

    [Fact]
    public void ClearCache_ClearsLastCheckTime()
    {
        // Act
        _sut.ClearCache();

        // Assert
        _sut.GetLastCheckTime().Should().BeNull();
    }

    #endregion

    #region GetLastCheckTime Tests

    [Fact]
    public void GetLastCheckTime_NoCheck_ReturnsNull()
    {
        // Act
        var lastCheck = _sut.GetLastCheckTime();

        // Assert
        lastCheck.Should().BeNull();
    }

    #endregion

    #region UpdateSettings Tests

    [Fact]
    public void UpdateSettings_DefaultUrl_IsSet()
    {
        // Arrange
        var settings = new UpdateSettings();

        // Assert
        settings.UpdateServerUrl.Should().NotBeNullOrEmpty();
        settings.UpdateServerUrl.Should().StartWith("https://");
    }

    [Fact]
    public void UpdateSettings_DismissedVersions_IsInitialized()
    {
        // Arrange
        var settings = new UpdateSettings();

        // Assert
        settings.DismissedVersions.Should().NotBeNull();
        settings.DismissedVersions.Should().BeEmpty();
    }

    #endregion

    #region UpdateChannel Enum Tests

    [Fact]
    public void UpdateChannel_HasAllChannels()
    {
        // Assert
        Enum.GetValues<UpdateChannel>().Should().Contain(UpdateChannel.Stable);
        Enum.GetValues<UpdateChannel>().Should().Contain(UpdateChannel.Beta);
        Enum.GetValues<UpdateChannel>().Should().Contain(UpdateChannel.Preview);
    }

    #endregion

    #region UpdateInfo Tests

    [Fact]
    public void UpdateInfo_DefaultValues_AreSet()
    {
        // Arrange
        var info = new UpdateInfo();

        // Assert
        info.Version.Should().Be(string.Empty);
        info.IsMandatory.Should().BeFalse();
    }

    #endregion

    #region UpdateCheckResult Tests

    [Fact]
    public void UpdateCheckResult_DefaultValues()
    {
        // Arrange
        var result = new UpdateCheckResult();

        // Assert
        result.Success.Should().BeFalse();
        result.UpdateAvailable.Should().BeFalse();
        result.FromCache.Should().BeFalse();
    }

    #endregion

    #region UpdateDownloadResult Tests

    [Fact]
    public void UpdateDownloadResult_DefaultSuccess_IsFalse()
    {
        // Arrange
        var result = new UpdateDownloadResult();

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region UpdateDownloadProgress Tests

    [Fact]
    public void UpdateDownloadProgress_CanSetProperties()
    {
        // Arrange
        var progress = new UpdateDownloadProgress
        {
            BytesDownloaded = 1000,
            TotalBytes = 5000,
            PercentComplete = 20
        };

        // Assert
        progress.BytesDownloaded.Should().Be(1000);
        progress.TotalBytes.Should().Be(5000);
        progress.PercentComplete.Should().Be(20);
    }

    #endregion
}

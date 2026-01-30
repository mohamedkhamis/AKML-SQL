using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for checking and managing updates.
/// Implements Story 12.2: Auto-Update.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _httpClient;
    private UpdateSettings _settings;
    private UpdateInfo? _cachedUpdateInfo;
    private DateTime _lastCheckTime;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _settings = new UpdateSettings();
        _lastCheckTime = DateTime.MinValue;
    }

    /// <summary>
    /// Configures update settings.
    /// </summary>
    public void Configure(UpdateSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger.LogInformation("Update service configured: Channel={Channel}, AutoCheck={AutoCheck}",
            settings.UpdateChannel, settings.AutoCheckEnabled);
    }

    /// <summary>
    /// Gets current settings.
    /// </summary>
    public UpdateSettings GetSettings() => _settings;

    /// <summary>
    /// Gets the current version.
    /// </summary>
    public Version GetCurrentVersion()
    {
        return Version.Parse(AkmlConstants.ProductVersion);
    }

    /// <summary>
    /// Checks for available updates.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // Return cached result if within check interval
        if (_cachedUpdateInfo != null &&
            DateTime.UtcNow - _lastCheckTime < TimeSpan.FromHours(_settings.CheckIntervalHours))
        {
            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = IsUpdateAvailable(_cachedUpdateInfo),
                UpdateInfo = _cachedUpdateInfo,
                FromCache = true
            };
        }

        try
        {
            var url = GetUpdateUrl();
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update check failed: {StatusCode}", response.StatusCode);
                return new UpdateCheckResult
                {
                    Success = false,
                    ErrorMessage = $"Server returned {response.StatusCode}"
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, _jsonOptions);

            if (updateInfo == null)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    ErrorMessage = "Invalid update response"
                };
            }

            _cachedUpdateInfo = updateInfo;
            _lastCheckTime = DateTime.UtcNow;

            var isUpdateAvailable = IsUpdateAvailable(updateInfo);

            _logger.LogInformation("Update check completed: Current={Current}, Latest={Latest}, Available={Available}",
                GetCurrentVersion(), updateInfo.Version, isUpdateAvailable);

            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = isUpdateAvailable,
                UpdateInfo = updateInfo,
                FromCache = false
            };
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = "Update check was cancelled"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error checking for updates");
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Downloads an update.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadUpdateAsync(
        UpdateInfo updateInfo,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (updateInfo == null)
        {
            return new UpdateDownloadResult
            {
                Success = false,
                ErrorMessage = "Update info is required"
            };
        }

        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            return new UpdateDownloadResult
            {
                Success = false,
                ErrorMessage = "No download URL available"
            };
        }

        try
        {
            var downloadPath = Path.Combine(
                AkmlConstants.Paths.CacheFolder,
                $"akml-sql-{updateInfo.Version}.exe");

            Directory.CreateDirectory(AkmlConstants.Paths.CacheFolder);

            using var response = await _httpClient.GetAsync(
                updateInfo.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                progress?.Report(new UpdateDownloadProgress
                {
                    BytesDownloaded = downloadedBytes,
                    TotalBytes = totalBytes,
                    PercentComplete = totalBytes > 0 ? (int)(downloadedBytes * 100 / totalBytes) : -1
                });
            }

            // Verify checksum if provided
            if (!string.IsNullOrEmpty(updateInfo.Checksum))
            {
                var fileChecksum = await ComputeFileChecksumAsync(downloadPath);
                if (!fileChecksum.Equals(updateInfo.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(downloadPath);
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        ErrorMessage = "Checksum verification failed"
                    };
                }
            }

            _logger.LogInformation("Update downloaded successfully: {Path}", downloadPath);

            return new UpdateDownloadResult
            {
                Success = true,
                DownloadPath = downloadPath
            };
        }
        catch (TaskCanceledException)
        {
            return new UpdateDownloadResult
            {
                Success = false,
                ErrorMessage = "Download was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading update");
            return new UpdateDownloadResult
            {
                Success = false,
                ErrorMessage = $"Download failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Dismisses an update notification.
    /// </summary>
    public void DismissUpdate(string version)
    {
        _settings.DismissedVersions.Add(version);
        _logger.LogInformation("Update {Version} dismissed", version);
    }

    /// <summary>
    /// Checks if an update has been dismissed.
    /// </summary>
    public bool IsUpdateDismissed(string version)
    {
        return _settings.DismissedVersions.Contains(version);
    }

    /// <summary>
    /// Clears the update cache.
    /// </summary>
    public void ClearCache()
    {
        _cachedUpdateInfo = null;
        _lastCheckTime = DateTime.MinValue;
        _logger.LogDebug("Update cache cleared");
    }

    /// <summary>
    /// Gets the last check time.
    /// </summary>
    public DateTime? GetLastCheckTime()
    {
        return _lastCheckTime == DateTime.MinValue ? null : _lastCheckTime;
    }

    #region Private Methods

    private string GetUpdateUrl()
    {
        var channel = _settings.UpdateChannel.ToString().ToLower();
        return $"{_settings.UpdateServerUrl}/api/v1/updates/{channel}?current={GetCurrentVersion()}";
    }

    private bool IsUpdateAvailable(UpdateInfo updateInfo)
    {
        if (updateInfo == null)
            return false;

        if (IsUpdateDismissed(updateInfo.Version))
            return false;

        var currentVersion = GetCurrentVersion();
        var latestVersion = Version.Parse(updateInfo.Version);

        return latestVersion > currentVersion;
    }

    private static async Task<string> ComputeFileChecksumAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    #endregion
}

/// <summary>
/// Interface for update service.
/// </summary>
public interface IUpdateService
{
    void Configure(UpdateSettings settings);
    UpdateSettings GetSettings();
    Version GetCurrentVersion();
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    void DismissUpdate(string version);
    bool IsUpdateDismissed(string version);
    void ClearCache();
    DateTime? GetLastCheckTime();
}

/// <summary>
/// Update settings.
/// </summary>
public class UpdateSettings
{
    public string UpdateServerUrl { get; set; } = "https://updates.akml-sql.com";
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public bool AutoCheckEnabled { get; set; } = true;
    public bool AutoDownloadEnabled { get; set; } = false;
    public int CheckIntervalHours { get; set; } = 24;
    public HashSet<string> DismissedVersions { get; set; } = new();
}

/// <summary>
/// Update channels.
/// </summary>
public enum UpdateChannel
{
    Stable,
    Beta,
    Preview
}

/// <summary>
/// Update information.
/// </summary>
public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? DownloadUrl { get; set; }
    public long? DownloadSize { get; set; }
    public string? Checksum { get; set; }
    public bool IsMandatory { get; set; }
    public string? MinimumVersion { get; set; }
}

/// <summary>
/// Result of update check.
/// </summary>
public class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool UpdateAvailable { get; set; }
    public UpdateInfo? UpdateInfo { get; set; }
    public string? ErrorMessage { get; set; }
    public bool FromCache { get; set; }
}

/// <summary>
/// Result of update download.
/// </summary>
public class UpdateDownloadResult
{
    public bool Success { get; set; }
    public string? DownloadPath { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Download progress information.
/// </summary>
public class UpdateDownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public int PercentComplete { get; set; }
}

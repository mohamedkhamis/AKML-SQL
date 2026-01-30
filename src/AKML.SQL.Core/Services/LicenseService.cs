using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for license management.
/// Implements Story 12.1: Licensing.
/// </summary>
public class LicenseService : ILicenseService
{
    private readonly ILogger<LicenseService> _logger;
    private readonly string _licenseFilePath;
    private LicenseInfo? _currentLicense;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LicenseService(ILogger<LicenseService> logger)
    {
        _logger = logger;
        _licenseFilePath = Path.Combine(AkmlConstants.Paths.SettingsFolder, "license.json");
        LoadLicense();
    }

    /// <summary>
    /// Gets the current license information.
    /// </summary>
    public LicenseInfo GetCurrentLicense()
    {
        if (_currentLicense == null)
        {
            return CreateTrialLicense();
        }
        return _currentLicense;
    }

    /// <summary>
    /// Validates and activates a license key.
    /// </summary>
    public LicenseActivationResult ActivateLicense(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseActivationResult
            {
                Success = false,
                ErrorMessage = "License key cannot be empty"
            };
        }

        try
        {
            var license = ParseLicenseKey(licenseKey);
            if (license == null)
            {
                return new LicenseActivationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid license key format"
                };
            }

            if (!ValidateLicenseSignature(license))
            {
                return new LicenseActivationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid license signature"
                };
            }

            if (license.ExpirationDate < DateTime.UtcNow)
            {
                return new LicenseActivationResult
                {
                    Success = false,
                    ErrorMessage = "License has expired"
                };
            }

            _currentLicense = license;
            _currentLicense.ActivatedAt = DateTime.UtcNow;
            SaveLicense();

            _logger.LogInformation("License activated: {Type} for {Licensee}",
                license.LicenseType, license.LicenseeName);

            return new LicenseActivationResult
            {
                Success = true,
                License = license
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating license");
            return new LicenseActivationResult
            {
                Success = false,
                ErrorMessage = $"Activation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Deactivates the current license.
    /// </summary>
    public bool DeactivateLicense()
    {
        if (_currentLicense == null || _currentLicense.LicenseType == LicenseType.Trial)
        {
            return false;
        }

        _currentLicense = null;

        if (File.Exists(_licenseFilePath))
        {
            File.Delete(_licenseFilePath);
        }

        _logger.LogInformation("License deactivated");
        return true;
    }

    /// <summary>
    /// Checks if the current license is valid.
    /// </summary>
    public bool IsLicenseValid()
    {
        var license = GetCurrentLicense();
        return license.Status == LicenseStatus.Active;
    }

    /// <summary>
    /// Checks if a specific feature is available with the current license.
    /// </summary>
    public bool IsFeatureAvailable(LicenseFeature feature)
    {
        var license = GetCurrentLicense();

        if (license.Status != LicenseStatus.Active)
        {
            return false;
        }

        return license.LicenseType switch
        {
            LicenseType.Trial => GetTrialFeatures().Contains(feature),
            LicenseType.Personal => GetPersonalFeatures().Contains(feature),
            LicenseType.Professional => GetProfessionalFeatures().Contains(feature),
            LicenseType.Enterprise => true, // Enterprise has all features
            _ => false
        };
    }

    /// <summary>
    /// Gets the days remaining on the current license.
    /// </summary>
    public int GetDaysRemaining()
    {
        var license = GetCurrentLicense();
        if (license.ExpirationDate == DateTime.MaxValue)
        {
            return int.MaxValue;
        }

        var remaining = (license.ExpirationDate - DateTime.UtcNow).Days;
        return Math.Max(0, remaining);
    }

    /// <summary>
    /// Generates a trial license key (for development/testing).
    /// </summary>
    public string GenerateTrialLicenseKey(int trialDays = 14)
    {
        var license = new LicenseInfo
        {
            LicenseId = Guid.NewGuid().ToString(),
            LicenseType = LicenseType.Trial,
            LicenseeName = "Trial User",
            LicenseeEmail = "trial@localhost",
            ExpirationDate = DateTime.UtcNow.AddDays(trialDays),
            IssuedAt = DateTime.UtcNow,
            MaxDevices = 1
        };

        return EncodeLicense(license);
    }

    /// <summary>
    /// Gets features available for trial license.
    /// </summary>
    public IReadOnlyList<LicenseFeature> GetTrialFeatures()
    {
        return new List<LicenseFeature>
        {
            LicenseFeature.IntelliSense,
            LicenseFeature.BasicFormatting,
            LicenseFeature.CodeSnippets
        };
    }

    /// <summary>
    /// Gets features available for personal license.
    /// </summary>
    public IReadOnlyList<LicenseFeature> GetPersonalFeatures()
    {
        return new List<LicenseFeature>
        {
            LicenseFeature.IntelliSense,
            LicenseFeature.BasicFormatting,
            LicenseFeature.AdvancedFormatting,
            LicenseFeature.CodeSnippets,
            LicenseFeature.QueryHistory,
            LicenseFeature.TabColoring
        };
    }

    /// <summary>
    /// Gets features available for professional license.
    /// </summary>
    public IReadOnlyList<LicenseFeature> GetProfessionalFeatures()
    {
        return Enum.GetValues<LicenseFeature>()
            .Where(f => f != LicenseFeature.EnterpriseSupport)
            .ToList();
    }

    #region Private Methods

    private void LoadLicense()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
            {
                _currentLicense = null;
                return;
            }

            var json = File.ReadAllText(_licenseFilePath);
            _currentLicense = JsonSerializer.Deserialize<LicenseInfo>(json, _jsonOptions);

            if (_currentLicense != null)
            {
                UpdateLicenseStatus();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading license");
            _currentLicense = null;
        }
    }

    private void SaveLicense()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_licenseFilePath)!);
            var json = JsonSerializer.Serialize(_currentLicense, _jsonOptions);
            File.WriteAllText(_licenseFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving license");
        }
    }

    private LicenseInfo CreateTrialLicense()
    {
        return new LicenseInfo
        {
            LicenseId = "trial",
            LicenseType = LicenseType.Trial,
            LicenseeName = "Trial User",
            ExpirationDate = DateTime.UtcNow.AddDays(14),
            IssuedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            MaxDevices = 1
        };
    }

    private void UpdateLicenseStatus()
    {
        if (_currentLicense == null)
            return;

        if (_currentLicense.ExpirationDate < DateTime.UtcNow)
        {
            _currentLicense.Status = LicenseStatus.Expired;
        }
        else
        {
            _currentLicense.Status = LicenseStatus.Active;
        }
    }

    private LicenseInfo? ParseLicenseKey(string key)
    {
        try
        {
            // License format: BASE64(JSON + SIGNATURE)
            var decoded = Convert.FromBase64String(key);
            var json = Encoding.UTF8.GetString(decoded);

            // Split JSON and signature (last 64 chars are signature)
            if (json.Length < 65)
                return null;

            var signatureStart = json.LastIndexOf('|');
            if (signatureStart < 0)
                return null;

            var licenseJson = json.Substring(0, signatureStart);
            var signature = json.Substring(signatureStart + 1);

            var license = JsonSerializer.Deserialize<LicenseInfo>(licenseJson, _jsonOptions);
            if (license != null)
            {
                license.Signature = signature;
            }
            return license;
        }
        catch
        {
            return null;
        }
    }

    private bool ValidateLicenseSignature(LicenseInfo license)
    {
        // In production, this would validate against a public key
        // For development, we use a simple hash validation
        if (string.IsNullOrEmpty(license.Signature))
            return false;

        var dataToSign = $"{license.LicenseId}:{license.LicenseType}:{license.LicenseeName}:{license.ExpirationDate:O}";
        var expectedSignature = ComputeSignature(dataToSign);

        return license.Signature == expectedSignature;
    }

    private string EncodeLicense(LicenseInfo license)
    {
        var dataToSign = $"{license.LicenseId}:{license.LicenseType}:{license.LicenseeName}:{license.ExpirationDate:O}";
        license.Signature = ComputeSignature(dataToSign);

        var licenseJson = JsonSerializer.Serialize(license, _jsonOptions);
        var combined = licenseJson + "|" + license.Signature;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
    }

    private static string ComputeSignature(string data)
    {
        // Development signature - in production, use asymmetric cryptography
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data + "AKML-SQL-SECRET-KEY"));
        return Convert.ToBase64String(hash);
    }

    #endregion
}

/// <summary>
/// Interface for license service.
/// </summary>
public interface ILicenseService
{
    LicenseInfo GetCurrentLicense();
    LicenseActivationResult ActivateLicense(string licenseKey);
    bool DeactivateLicense();
    bool IsLicenseValid();
    bool IsFeatureAvailable(LicenseFeature feature);
    int GetDaysRemaining();
    string GenerateTrialLicenseKey(int trialDays = 14);
    IReadOnlyList<LicenseFeature> GetTrialFeatures();
    IReadOnlyList<LicenseFeature> GetPersonalFeatures();
    IReadOnlyList<LicenseFeature> GetProfessionalFeatures();
}

/// <summary>
/// License information.
/// </summary>
public class LicenseInfo
{
    public string LicenseId { get; set; } = string.Empty;
    public LicenseType LicenseType { get; set; } = LicenseType.Trial;
    public string LicenseeName { get; set; } = string.Empty;
    public string? LicenseeEmail { get; set; }
    public string? LicenseeCompany { get; set; }
    public DateTime ExpirationDate { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public LicenseStatus Status { get; set; } = LicenseStatus.Inactive;
    public int MaxDevices { get; set; } = 1;
    public string? Signature { get; set; }
}

/// <summary>
/// License types.
/// </summary>
public enum LicenseType
{
    Trial,
    Personal,
    Professional,
    Enterprise
}

/// <summary>
/// License status.
/// </summary>
public enum LicenseStatus
{
    Inactive,
    Active,
    Expired,
    Revoked
}

/// <summary>
/// License features.
/// </summary>
public enum LicenseFeature
{
    IntelliSense,
    BasicFormatting,
    AdvancedFormatting,
    CodeSnippets,
    QueryHistory,
    TabColoring,
    CodeAnalysis,
    Refactoring,
    AiAssistance,
    MultiVersion,
    EnterpriseSupport
}

/// <summary>
/// Result of license activation.
/// </summary>
public class LicenseActivationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LicenseInfo? License { get; set; }
}

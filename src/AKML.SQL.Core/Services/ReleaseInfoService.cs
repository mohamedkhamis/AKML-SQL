using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service providing release and product information.
/// Implements Story 13.1: Release Management.
/// </summary>
public class ReleaseInfoService : IReleaseInfoService
{
    private readonly ILogger<ReleaseInfoService> _logger;
    private readonly ILicenseService? _licenseService;
    private readonly ISqlVersionService? _sqlVersionService;

    public ReleaseInfoService(
        ILogger<ReleaseInfoService> logger,
        ILicenseService? licenseService = null,
        ISqlVersionService? sqlVersionService = null)
    {
        _logger = logger;
        _licenseService = licenseService;
        _sqlVersionService = sqlVersionService;
    }

    /// <summary>
    /// Gets the product version.
    /// </summary>
    public string GetVersion() => AkmlConstants.ProductVersion;

    /// <summary>
    /// Gets the product name.
    /// </summary>
    public string GetProductName() => AkmlConstants.ProductName;

    /// <summary>
    /// Gets detailed release information.
    /// </summary>
    public ReleaseInfo GetReleaseInfo()
    {
        return new ReleaseInfo
        {
            Version = GetVersion(),
            ProductName = GetProductName(),
            ReleaseDate = new DateTime(2026, 1, 31),
            BuildNumber = GetBuildNumber(),
            TargetFramework = ".NET 10",
            SupportedSsmsVersions = new[] { "SSMS 18", "SSMS 19", "SSMS 20+" },
            Features = GetFeatureList()
        };
    }

    /// <summary>
    /// Gets the list of all features.
    /// </summary>
    public IReadOnlyList<FeatureInfo> GetFeatureList()
    {
        return new List<FeatureInfo>
        {
            // Sprint 5: IntelliSense Core
            new()
            {
                Name = "Context-Aware IntelliSense",
                Description = "Smart SQL code completion based on context analysis",
                Category = FeatureCategory.IntelliSense,
                Sprint = 5,
                IsCore = true
            },
            new()
            {
                Name = "Schema-Aware Completions",
                Description = "Suggestions based on database schema metadata",
                Category = FeatureCategory.IntelliSense,
                Sprint = 5,
                IsCore = true
            },
            new()
            {
                Name = "Keyword & Function Suggestions",
                Description = "SQL keywords and built-in function completions",
                Category = FeatureCategory.IntelliSense,
                Sprint = 5,
                IsCore = true
            },

            // Sprint 6: Formatting & Styles
            new()
            {
                Name = "SQL Formatting",
                Description = "Automatic SQL code formatting with customizable styles",
                Category = FeatureCategory.Formatting,
                Sprint = 6,
                IsCore = true
            },
            new()
            {
                Name = "Format Presets",
                Description = "Built-in format styles: Compact, Standard, Expanded",
                Category = FeatureCategory.Formatting,
                Sprint = 6
            },
            new()
            {
                Name = "Custom Format Styles",
                Description = "Create and save custom formatting rules",
                Category = FeatureCategory.Formatting,
                Sprint = 6
            },

            // Sprint 7: Refactoring Tools
            new()
            {
                Name = "Rename Alias",
                Description = "Safely rename table aliases across queries",
                Category = FeatureCategory.Refactoring,
                Sprint = 7
            },
            new()
            {
                Name = "Extract to CTE",
                Description = "Extract subqueries to Common Table Expressions",
                Category = FeatureCategory.Refactoring,
                Sprint = 7
            },
            new()
            {
                Name = "Qualify Column Names",
                Description = "Add table prefixes to unqualified column references",
                Category = FeatureCategory.Refactoring,
                Sprint = 7
            },
            new()
            {
                Name = "Expand SELECT *",
                Description = "Replace SELECT * with explicit column list",
                Category = FeatureCategory.Refactoring,
                Sprint = 7
            },

            // Sprint 8: History & Snippets
            new()
            {
                Name = "Query History",
                Description = "Track and search executed query history",
                Category = FeatureCategory.Productivity,
                Sprint = 8
            },
            new()
            {
                Name = "Code Snippets",
                Description = "20+ built-in SQL snippets with placeholders",
                Category = FeatureCategory.Productivity,
                Sprint = 8
            },
            new()
            {
                Name = "Custom Snippets",
                Description = "Create, import and export custom snippets",
                Category = FeatureCategory.Productivity,
                Sprint = 8
            },

            // Sprint 9: Tab Management & Code Analysis
            new()
            {
                Name = "Tab Coloring",
                Description = "Color-code tabs based on server environment",
                Category = FeatureCategory.TabManagement,
                Sprint = 9
            },
            new()
            {
                Name = "Code Analysis",
                Description = "10 analysis rules for performance, security, best practices",
                Category = FeatureCategory.CodeAnalysis,
                Sprint = 9
            },
            new()
            {
                Name = "Pattern Matching",
                Description = "Wildcard, regex, and exact pattern matching for rules",
                Category = FeatureCategory.TabManagement,
                Sprint = 9
            },

            // Sprint 10: AI Integration
            new()
            {
                Name = "Natural Language to SQL",
                Description = "Convert plain English to T-SQL queries",
                Category = FeatureCategory.AI,
                Sprint = 10,
                RequiresLicense = LicenseType.Professional
            },
            new()
            {
                Name = "SQL Explanation",
                Description = "AI-powered query explanations at multiple detail levels",
                Category = FeatureCategory.AI,
                Sprint = 10,
                RequiresLicense = LicenseType.Professional
            },
            new()
            {
                Name = "Optimization Suggestions",
                Description = "AI analysis for query performance improvements",
                Category = FeatureCategory.AI,
                Sprint = 10,
                RequiresLicense = LicenseType.Professional
            },

            // Sprint 11: Performance & Multi-Version
            new()
            {
                Name = "Performance Monitoring",
                Description = "Track and analyze operation performance metrics",
                Category = FeatureCategory.Performance,
                Sprint = 11
            },
            new()
            {
                Name = "Multi-Version Support",
                Description = "SQL Server 2008-2022 compatibility",
                Category = FeatureCategory.Performance,
                Sprint = 11
            },
            new()
            {
                Name = "Version-Specific Parsing",
                Description = "Parsers and generators for each SQL Server version",
                Category = FeatureCategory.Performance,
                Sprint = 11
            },

            // Sprint 12: Licensing & Auto-Update
            new()
            {
                Name = "License Management",
                Description = "Trial, Personal, Professional, and Enterprise licenses",
                Category = FeatureCategory.System,
                Sprint = 12
            },
            new()
            {
                Name = "Auto-Update",
                Description = "Automatic update checking and downloading",
                Category = FeatureCategory.System,
                Sprint = 12
            }
        };
    }

    /// <summary>
    /// Gets features by category.
    /// </summary>
    public IReadOnlyList<FeatureInfo> GetFeaturesByCategory(FeatureCategory category)
    {
        return GetFeatureList().Where(f => f.Category == category).ToList();
    }

    /// <summary>
    /// Gets features by sprint.
    /// </summary>
    public IReadOnlyList<FeatureInfo> GetFeaturesBySprint(int sprint)
    {
        return GetFeatureList().Where(f => f.Sprint == sprint).ToList();
    }

    /// <summary>
    /// Gets product statistics.
    /// </summary>
    public ProductStats GetProductStats()
    {
        var features = GetFeatureList();
        return new ProductStats
        {
            TotalFeatures = features.Count,
            CoreFeatures = features.Count(f => f.IsCore),
            ProFeatures = features.Count(f => f.RequiresLicense == LicenseType.Professional),
            Categories = features.Select(f => f.Category).Distinct().Count(),
            Sprints = features.Max(f => f.Sprint),
            SupportedSqlVersions = _sqlVersionService?.GetSupportedVersions().Count ?? 7,
            AnalysisRules = 10,
            BuiltInSnippets = 20,
            ColorRules = 7
        };
    }

    /// <summary>
    /// Checks system health.
    /// </summary>
    public HealthCheckResult CheckHealth()
    {
        var result = new HealthCheckResult
        {
            IsHealthy = true,
            Timestamp = DateTime.UtcNow,
            Version = GetVersion()
        };

        // Check license
        try
        {
            if (_licenseService != null)
            {
                var license = _licenseService.GetCurrentLicense();
                result.Checks.Add(new HealthCheck
                {
                    Name = "License",
                    Status = license.Status == LicenseStatus.Active ? HealthStatus.Healthy : HealthStatus.Degraded,
                    Message = $"{license.LicenseType} license ({license.Status})"
                });
            }
        }
        catch (Exception ex)
        {
            result.Checks.Add(new HealthCheck
            {
                Name = "License",
                Status = HealthStatus.Unhealthy,
                Message = ex.Message
            });
            result.IsHealthy = false;
        }

        // Check SQL Version Service
        try
        {
            if (_sqlVersionService != null)
            {
                var versions = _sqlVersionService.GetSupportedVersions();
                result.Checks.Add(new HealthCheck
                {
                    Name = "SQL Version Support",
                    Status = HealthStatus.Healthy,
                    Message = $"{versions.Count} versions supported"
                });
            }
        }
        catch (Exception ex)
        {
            result.Checks.Add(new HealthCheck
            {
                Name = "SQL Version Support",
                Status = HealthStatus.Unhealthy,
                Message = ex.Message
            });
            result.IsHealthy = false;
        }

        result.Checks.Add(new HealthCheck
        {
            Name = "Core Service",
            Status = HealthStatus.Healthy,
            Message = "Running"
        });

        return result;
    }

    /// <summary>
    /// Gets copyright information.
    /// </summary>
    public string GetCopyright()
    {
        return $"Copyright © {DateTime.UtcNow.Year} Azka Innovation. All rights reserved.";
    }

    /// <summary>
    /// Gets support contact information.
    /// </summary>
    public SupportInfo GetSupportInfo()
    {
        return new SupportInfo
        {
            Email = "support@akml-sql.com",
            Website = "https://akml-sql.com",
            Documentation = "https://docs.akml-sql.com",
            GitHub = "https://github.com/akml-sql/akml-sql"
        };
    }

    private string GetBuildNumber()
    {
        // In production, this would come from build system
        return DateTime.UtcNow.ToString("yyyyMMdd");
    }
}

/// <summary>
/// Interface for release info service.
/// </summary>
public interface IReleaseInfoService
{
    string GetVersion();
    string GetProductName();
    ReleaseInfo GetReleaseInfo();
    IReadOnlyList<FeatureInfo> GetFeatureList();
    IReadOnlyList<FeatureInfo> GetFeaturesByCategory(FeatureCategory category);
    IReadOnlyList<FeatureInfo> GetFeaturesBySprint(int sprint);
    ProductStats GetProductStats();
    HealthCheckResult CheckHealth();
    string GetCopyright();
    SupportInfo GetSupportInfo();
}

/// <summary>
/// Release information.
/// </summary>
public class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }
    public string BuildNumber { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string[] SupportedSsmsVersions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<FeatureInfo> Features { get; set; } = Array.Empty<FeatureInfo>();
}

/// <summary>
/// Feature information.
/// </summary>
public class FeatureInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FeatureCategory Category { get; set; }
    public int Sprint { get; set; }
    public bool IsCore { get; set; }
    public LicenseType? RequiresLicense { get; set; }
}

/// <summary>
/// Feature categories.
/// </summary>
public enum FeatureCategory
{
    IntelliSense,
    Formatting,
    Refactoring,
    Productivity,
    TabManagement,
    CodeAnalysis,
    AI,
    Performance,
    System
}

/// <summary>
/// Product statistics.
/// </summary>
public class ProductStats
{
    public int TotalFeatures { get; set; }
    public int CoreFeatures { get; set; }
    public int ProFeatures { get; set; }
    public int Categories { get; set; }
    public int Sprints { get; set; }
    public int SupportedSqlVersions { get; set; }
    public int AnalysisRules { get; set; }
    public int BuiltInSnippets { get; set; }
    public int ColorRules { get; set; }
}

/// <summary>
/// Health check result.
/// </summary>
public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<HealthCheck> Checks { get; set; } = new();
}

/// <summary>
/// Individual health check.
/// </summary>
public class HealthCheck
{
    public string Name { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Health status.
/// </summary>
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// Support information.
/// </summary>
public class SupportInfo
{
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
}

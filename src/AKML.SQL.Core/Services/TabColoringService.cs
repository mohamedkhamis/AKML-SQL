using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for managing tab coloring based on connection patterns.
/// Implements Story 9.1: Tab Management.
/// </summary>
public class TabColoringService : ITabColoringService
{
    private readonly ILogger<TabColoringService> _logger;
    private readonly List<TabColorRule> _rules;
    private readonly string _rulesFilePath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TabColoringService(ILogger<TabColoringService> logger)
    {
        _logger = logger;
        _rules = new List<TabColorRule>();
        _rulesFilePath = Path.Combine(AkmlConstants.Paths.SettingsFolder, "tab-colors.json");

        LoadDefaultRules();
        LoadCustomRules();
    }

    /// <summary>
    /// Gets the color for a given connection.
    /// </summary>
    public TabColorResult GetColor(string? serverName, string? databaseName)
    {
        if (string.IsNullOrEmpty(serverName) && string.IsNullOrEmpty(databaseName))
        {
            return new TabColorResult
            {
                Color = "#808080",
                Label = "No Connection",
                MatchedRule = null
            };
        }

        foreach (var rule in _rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (MatchesRule(rule, serverName, databaseName))
            {
                return new TabColorResult
                {
                    Color = rule.Color,
                    Label = rule.Label ?? rule.Name,
                    MatchedRule = rule.Name
                };
            }
        }

        // Default color
        return new TabColorResult
        {
            Color = "#4A90D9",
            Label = databaseName ?? serverName ?? "Connected",
            MatchedRule = null
        };
    }

    /// <summary>
    /// Gets all color rules.
    /// </summary>
    public IReadOnlyList<TabColorRule> GetAllRules()
    {
        return _rules.AsReadOnly();
    }

    /// <summary>
    /// Adds or updates a rule.
    /// </summary>
    public void SaveRule(TabColorRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Name))
            throw new ArgumentException("Rule name cannot be empty", nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Pattern))
            throw new ArgumentException("Rule pattern cannot be empty", nameof(rule));

        var existing = _rules.FirstOrDefault(r => r.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Pattern = rule.Pattern;
            existing.Color = rule.Color;
            existing.Label = rule.Label;
            existing.MatchType = rule.MatchType;
            existing.TargetField = rule.TargetField;
            existing.IsEnabled = rule.IsEnabled;
            existing.Priority = rule.Priority;
            existing.IsBuiltIn = false;
        }
        else
        {
            rule.IsBuiltIn = false;
            _rules.Add(rule);
        }

        _logger.LogInformation("Saved tab color rule: {Name}", rule.Name);
        SaveCustomRules();
    }

    /// <summary>
    /// Deletes a rule.
    /// </summary>
    public bool DeleteRule(string name)
    {
        var rule = _rules.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
            return false;

        if (rule.IsBuiltIn)
            throw new InvalidOperationException("Cannot delete built-in rules");

        _rules.Remove(rule);
        _logger.LogInformation("Deleted tab color rule: {Name}", name);
        SaveCustomRules();
        return true;
    }

    /// <summary>
    /// Reorders rules by setting priorities.
    /// </summary>
    public void ReorderRules(IEnumerable<string> ruleNamesInOrder)
    {
        var priority = 0;
        foreach (var name in ruleNamesInOrder)
        {
            var rule = _rules.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (rule != null)
            {
                rule.Priority = priority++;
            }
        }
        SaveCustomRules();
    }

    /// <summary>
    /// Tests a pattern against a value.
    /// </summary>
    public bool TestPattern(string pattern, PatternMatchType matchType, string testValue)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(testValue))
            return false;

        return matchType switch
        {
            PatternMatchType.Contains => testValue.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.StartsWith => testValue.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.EndsWith => testValue.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Exact => testValue.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Wildcard => MatchWildcard(pattern, testValue),
            PatternMatchType.Regex => MatchRegex(pattern, testValue),
            _ => false
        };
    }

    /// <summary>
    /// Resets to default rules.
    /// </summary>
    public void ResetToDefaults()
    {
        _rules.Clear();
        LoadDefaultRules();

        if (File.Exists(_rulesFilePath))
            File.Delete(_rulesFilePath);

        _logger.LogInformation("Reset tab color rules to defaults");
    }

    private bool MatchesRule(TabColorRule rule, string? serverName, string? databaseName)
    {
        var targetValue = rule.TargetField switch
        {
            RuleTargetField.Server => serverName,
            RuleTargetField.Database => databaseName,
            RuleTargetField.Both => $"{serverName}.{databaseName}",
            _ => serverName
        };

        if (string.IsNullOrEmpty(targetValue))
            return false;

        return TestPattern(rule.Pattern, rule.MatchType, targetValue);
    }

    private bool MatchWildcard(string pattern, string value)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
    }

    private bool MatchRegex(string pattern, string value)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void LoadDefaultRules()
    {
        _rules.AddRange(new[]
        {
            new TabColorRule
            {
                Name = "Production",
                Pattern = "*prod*",
                Color = "#E74C3C",
                Label = "PRODUCTION",
                MatchType = PatternMatchType.Wildcard,
                TargetField = RuleTargetField.Server,
                Priority = 0,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "UAT",
                Pattern = "*uat*",
                Color = "#F39C12",
                Label = "UAT",
                MatchType = PatternMatchType.Wildcard,
                TargetField = RuleTargetField.Server,
                Priority = 1,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "Staging",
                Pattern = "*staging*",
                Color = "#9B59B6",
                Label = "Staging",
                MatchType = PatternMatchType.Wildcard,
                TargetField = RuleTargetField.Server,
                Priority = 2,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "QA",
                Pattern = "*qa*",
                Color = "#3498DB",
                Label = "QA",
                MatchType = PatternMatchType.Wildcard,
                TargetField = RuleTargetField.Server,
                Priority = 3,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "Development",
                Pattern = "*dev*",
                Color = "#2ECC71",
                Label = "Dev",
                MatchType = PatternMatchType.Wildcard,
                TargetField = RuleTargetField.Server,
                Priority = 4,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "Local",
                Pattern = "localhost",
                Color = "#1ABC9C",
                Label = "Local",
                MatchType = PatternMatchType.Contains,
                TargetField = RuleTargetField.Server,
                Priority = 5,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new TabColorRule
            {
                Name = "Local Instance",
                Pattern = "(localdb)",
                Color = "#1ABC9C",
                Label = "LocalDB",
                MatchType = PatternMatchType.Contains,
                TargetField = RuleTargetField.Server,
                Priority = 6,
                IsBuiltIn = true,
                IsEnabled = true
            }
        });

        _logger.LogDebug("Loaded {Count} default tab color rules", _rules.Count);
    }

    private void LoadCustomRules()
    {
        try
        {
            if (!File.Exists(_rulesFilePath))
                return;

            var json = File.ReadAllText(_rulesFilePath);
            var customRules = JsonSerializer.Deserialize<List<TabColorRule>>(json, _jsonOptions);

            if (customRules != null)
            {
                foreach (var rule in customRules)
                {
                    rule.IsBuiltIn = false;
                    var existing = _rules.FirstOrDefault(r => r.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        _rules.Remove(existing);
                    }
                    _rules.Add(rule);
                }

                _logger.LogDebug("Loaded {Count} custom tab color rules", customRules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom tab color rules");
        }
    }

    private void SaveCustomRules()
    {
        try
        {
            var customRules = _rules.Where(r => !r.IsBuiltIn).ToList();
            var json = JsonSerializer.Serialize(customRules, _jsonOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(_rulesFilePath)!);
            File.WriteAllText(_rulesFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving custom tab color rules");
        }
    }
}

/// <summary>
/// Interface for tab coloring service.
/// </summary>
public interface ITabColoringService
{
    TabColorResult GetColor(string? serverName, string? databaseName);
    IReadOnlyList<TabColorRule> GetAllRules();
    void SaveRule(TabColorRule rule);
    bool DeleteRule(string name);
    void ReorderRules(IEnumerable<string> ruleNamesInOrder);
    bool TestPattern(string pattern, PatternMatchType matchType, string testValue);
    void ResetToDefaults();
}

/// <summary>
/// Result of tab color lookup.
/// </summary>
public class TabColorResult
{
    public string Color { get; set; } = "#808080";
    public string Label { get; set; } = string.Empty;
    public string? MatchedRule { get; set; }
}

/// <summary>
/// Tab color rule definition.
/// </summary>
public class TabColorRule
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string Color { get; set; } = "#808080";
    public string? Label { get; set; }
    public PatternMatchType MatchType { get; set; } = PatternMatchType.Contains;
    public RuleTargetField TargetField { get; set; } = RuleTargetField.Server;
    public int Priority { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Pattern matching types.
/// </summary>
public enum PatternMatchType
{
    Contains,
    StartsWith,
    EndsWith,
    Exact,
    Wildcard,
    Regex
}

/// <summary>
/// Target field for rule matching.
/// </summary>
public enum RuleTargetField
{
    Server,
    Database,
    Both
}

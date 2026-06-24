using System.Text.Json;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Analysis;
using Serilog;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Loads and merges CAsettings from: built-in defaults → global AppSettings → nearest .casettings file.
/// Results are cached per directory path and invalidated on file change or explicit call to
/// <see cref="InvalidateCache"/>.
/// </summary>
public class CaSettingsLoader : IDisposable
{
    private readonly Dictionary<string, ResolvedAnalysisSettings> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly string[] CaSettingsFileNames = [".casettings", "akml.casettings.json"];

    public ResolvedAnalysisSettings Load(string? fileDirectory, CodeAnalysisSettings globalSettings)
    {
        var dir = fileDirectory ?? string.Empty;
        lock (_lock)
        {
            if (_cache.TryGetValue(dir, out var cached))
                return cached;

            var (resolved, caFile) = Build(dir, globalSettings);
            _cache[dir] = resolved;

            // Watch the directory that actually contains the governing .casettings file (which may be
            // an ancestor of dir). This ensures that editing a parent-level .casettings invalidates
            // all cache entries that resolved through it, not just entries for that exact directory.
            // Fall back to dir itself so we still detect a .casettings created there later.
            var watchDir = (!string.IsNullOrEmpty(caFile)
                ? Path.GetDirectoryName(caFile)
                : null)
                ?? dir;

            if (!string.IsNullOrEmpty(watchDir) && Directory.Exists(watchDir))
                EnsureWatcher(watchDir);

            return resolved;
        }
    }

    public void InvalidateCache()
    {
        lock (_lock) { _cache.Clear(); }
    }

    public void InvalidateDirectory(string dir)
    {
        lock (_lock) { _cache.Remove(dir); }
    }

    private (ResolvedAnalysisSettings, string?) Build(string startDir, CodeAnalysisSettings globalSettings)
    {
        var settings = new ResolvedAnalysisSettings
        {
            Enabled         = globalSettings.Enabled,
            RunOnType       = globalSettings.RunOnType,
            RunOnSave       = globalSettings.RunOnSave,
            AutoFixOnFormat = globalSettings.AutoFixOnFormat,
        };

        // Spec 030 T053: apply user-level per-rule overrides from the global config (Manage Rules
        // dialog) FIRST, so a project .casettings (loaded below) still wins. "ignore" severity, like
        // in .casettings, disables the rule.
        if (globalSettings.RuleOverrides != null)
        {
            foreach (var (ruleId, ov) in globalSettings.RuleOverrides)
            {
                if (string.IsNullOrWhiteSpace(ruleId) || ov == null) continue;
                settings.EffectiveRules[ruleId] = new ResolvedRuleConfig
                {
                    Enabled  = ov.Enabled && !string.Equals(ov.Severity, "ignore", StringComparison.OrdinalIgnoreCase),
                    Severity = ParseSeverity(ov.Severity)
                };
            }
        }

        var caFile = FindCaSettingsFile(startDir);
        if (caFile == null) return (settings, null);

        try
        {
            var json = File.ReadAllText(caFile);
            var ca   = JsonSerializer.Deserialize<CaSettings>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (ca == null) return (settings, caFile);

            foreach (var (ruleId, cfg) in ca.Rules)
            {
                settings.EffectiveRules[ruleId] = new ResolvedRuleConfig
                {
                    Enabled  = cfg.Enabled && !string.Equals(cfg.Severity, "ignore", StringComparison.OrdinalIgnoreCase),
                    Severity = ParseSeverity(cfg.Severity)
                };
            }

            foreach (var gs in ca.GlobalSuppressions)
            {
                if (!string.IsNullOrWhiteSpace(gs.Rule))
                    settings.GloballySuppressedRules.Add(gs.Rule.Trim().ToUpperInvariant());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load CAsettings from {File}; using defaults", caFile);
        }

        return (settings, caFile);
    }

    private static string? FindCaSettingsFile(string startDir)
    {
        if (string.IsNullOrEmpty(startDir)) return null;

        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            foreach (var name in CaSettingsFileNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent ?? string.Empty;
        }
        return null;
    }

    private void EnsureWatcher(string dir)
    {
        if (_watchers.ContainsKey(dir)) return;
        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                Filter            = "*.casettings*",
                NotifyFilter      = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            // Invalidate the whole cache so child-dir entries that resolved through an
            // ancestor .casettings are also dropped (not just the ancestor's own entry).
            watcher.Changed += (_, _) => InvalidateCache();
            watcher.Created += (_, _) => InvalidateCache();
            watcher.Deleted += (_, _) => InvalidateCache();
            _watchers[dir] = watcher;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not create file watcher for {Dir}", dir);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            foreach (var w in _watchers.Values)
                try { w.Dispose(); } catch { /* best-effort */ }
            _watchers.Clear();
            _cache.Clear();
        }
    }

    private static DiagnosticSeverity ParseSeverity(string? s)
    {
        return s?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "information" => DiagnosticSeverity.Information,
            "hint" => DiagnosticSeverity.Hint,
            _ => DiagnosticSeverity.Warning
        };
    }
}

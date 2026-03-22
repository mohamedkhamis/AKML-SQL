using System;
using System.Collections.Generic;
using System.IO;
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
public class CaSettingsLoader
{
    private readonly Dictionary<string, ResolvedAnalysisSettings> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly string[] CaSettingsFileNames = [".casettings", "akml.casettings.json"];

    public ResolvedAnalysisSettings Load(string? fileDirectory, CodeAnalysisSettings globalSettings)
    {
        var dir = fileDirectory ?? string.Empty;
        lock (_lock)
        {
            if (_cache.TryGetValue(dir, out var cached))
                return cached;

            var resolved = Build(dir, globalSettings);
            _cache[dir] = resolved;

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                EnsureWatcher(dir);

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

    private ResolvedAnalysisSettings Build(string startDir, CodeAnalysisSettings globalSettings)
    {
        var settings = new ResolvedAnalysisSettings
        {
            Enabled         = globalSettings.Enabled,
            RunOnType       = globalSettings.RunOnType,
            RunOnSave       = globalSettings.RunOnSave,
            AutoFixOnFormat = globalSettings.AutoFixOnFormat,
        };

        var caFile = FindCaSettingsFile(startDir);
        if (caFile == null) return settings;

        try
        {
            var json = File.ReadAllText(caFile);
            var ca   = JsonSerializer.Deserialize<CaSettings>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (ca == null) return settings;

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

        return settings;
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
            watcher.Changed += (_, _) => InvalidateDirectory(dir);
            watcher.Created += (_, _) => InvalidateDirectory(dir);
            watcher.Deleted += (_, _) => InvalidateDirectory(dir);
            _watchers[dir] = watcher;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not create file watcher for {Dir}", dir);
        }
    }

    private static DiagnosticSeverity ParseSeverity(string? s) => s?.ToLowerInvariant() switch
    {
        "error"       => DiagnosticSeverity.Error,
        "warning"     => DiagnosticSeverity.Warning,
        "information" => DiagnosticSeverity.Information,
        "hint"        => DiagnosticSeverity.Hint,
        _             => DiagnosticSeverity.Warning
    };
}

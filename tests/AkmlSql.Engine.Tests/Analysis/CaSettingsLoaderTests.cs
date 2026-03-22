using System;
using System.IO;
using System.Text.Json;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

public sealed class CaSettingsLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CaSettingsLoader _loader;

    public CaSettingsLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AkmlSqlTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _loader = new CaSettingsLoader();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* cleanup best-effort */ }
    }

    private static CodeAnalysisSettings DefaultGlobal() => new() { Enabled = true };

    [Fact]
    public void ReturnsDefaultSettingsWhenNoFileExists()
    {
        var result = _loader.Load(_tempDir, DefaultGlobal());

        Assert.True(result.Enabled);
        Assert.Empty(result.EffectiveRules);
    }

    [Fact]
    public void LoadsValidJsonCaSettingsFile()
    {
        WriteCaSettings(_tempDir, new
        {
            rules = new
            {
                PE003 = new { enabled = false, severity = "error" }
            }
        });

        var result = _loader.Load(_tempDir, DefaultGlobal());

        Assert.True(result.EffectiveRules.ContainsKey("PE003"));
        Assert.False(result.EffectiveRules["PE003"].Enabled);
    }

    [Fact]
    public void RespectsEnabledFalseForRule()
    {
        WriteCaSettings(_tempDir, new
        {
            rules = new
            {
                PE001 = new { enabled = false, severity = "warning" }
            }
        });

        var result = _loader.Load(_tempDir, DefaultGlobal());

        Assert.True(result.EffectiveRules.ContainsKey("PE001"));
        Assert.False(result.EffectiveRules["PE001"].Enabled);
    }

    [Fact]
    public void SeverityOverrideMapsCorrectly()
    {
        WriteCaSettings(_tempDir, new
        {
            rules = new
            {
                PE003 = new { enabled = true, severity = "information" }
            }
        });

        var result = _loader.Load(_tempDir, DefaultGlobal());

        Assert.Equal(DiagnosticSeverity.Information, result.EffectiveRules["PE003"].Severity);
    }

    [Fact]
    public void InvalidJsonLogsErrorAndUsesDefaults()
    {
        var path = Path.Combine(_tempDir, ".casettings");
        File.WriteAllText(path, "{ this is not valid json !!!");

        // Should not throw; uses defaults
        var result = _loader.Load(_tempDir, DefaultGlobal());

        Assert.True(result.Enabled);
        Assert.Empty(result.EffectiveRules);
    }

    [Fact]
    public void InvalidateCacheCausesReloadOnNextCall()
    {
        // First load: no file — result is empty and cached
        var first = _loader.Load(_tempDir, DefaultGlobal());
        Assert.Empty(first.EffectiveRules);

        // Write a .casettings file, then explicitly invalidate and reload
        WriteCaSettings(_tempDir, new { rules = new { PE001 = new { enabled = false } } });

        _loader.InvalidateCache();
        var reloaded = _loader.Load(_tempDir, DefaultGlobal());
        Assert.True(reloaded.EffectiveRules.ContainsKey("PE001"));
    }

    [Fact]
    public void WalksUpDirectoryTreeToFindFile()
    {
        // Place .casettings in the parent (_tempDir), look up from a subdirectory
        WriteCaSettings(_tempDir, new { rules = new { BP001 = new { enabled = false } } });

        var subDir = Path.Combine(_tempDir, "SubFolder");
        Directory.CreateDirectory(subDir);

        var result = _loader.Load(subDir, DefaultGlobal());

        // Should walk up and find the parent's .casettings
        Assert.True(result.EffectiveRules.ContainsKey("BP001"));
    }

    [Fact]
    public void GlobalSettingsDisabledPropagatesRegardlessOfCaFile()
    {
        var global = new CodeAnalysisSettings { Enabled = false };
        var result = _loader.Load(_tempDir, global);

        Assert.False(result.Enabled);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void WriteCaSettings(string dir, object content)
    {
        var path = Path.Combine(dir, ".casettings");
        File.WriteAllText(path, JsonSerializer.Serialize(content,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

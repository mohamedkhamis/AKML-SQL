using System.Text.Json;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// PR #247 regression guard: ancestor .casettings edits must invalidate child-dir cache entries.
/// Two bugs were fixed together:
///   1. Build() now returns (ResolvedAnalysisSettings, string?) so Load() can watch the ancestor dir.
///   2. Watcher callbacks now call InvalidateCache() (not InvalidateDirectory(watchDir)),
///      so child-dir cache entries that resolved through the ancestor are also dropped.
/// </summary>
public sealed class Pr247_CaSettingsLoaderFix : IDisposable
{
    private readonly string _tempDir;
    private readonly CaSettingsLoader _loader;

    public Pr247_CaSettingsLoaderFix()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AkmlSqlPr247_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _loader = new CaSettingsLoader();
    }

    public void Dispose()
    {
        _loader.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* cleanup best-effort */ }
    }

    private static CodeAnalysisSettings DefaultGlobal() => new() { Enabled = true };

    private static void WriteCaSettings(string dir, object content)
    {
        var path = Path.Combine(dir, ".casettings");
        File.WriteAllText(path, JsonSerializer.Serialize(content,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // ─── PR #247 fix 1: watcher is placed on ancestor dir, not just startDir ──

    [Fact]
    public void WatcherTracksAncestorDirectory_ExplicitInvalidateReflectsAncestorChange()
    {
        // Arrange: ancestor (.casettings in _tempDir) governs a subdirectory
        WriteCaSettings(_tempDir, new { rules = new { PE001 = new { enabled = false, severity = "warning" } } });

        var subDir = Path.Combine(_tempDir, "ChildProject");
        Directory.CreateDirectory(subDir);

        // Initial load from child; must walk up and pick up PE001
        var first = _loader.Load(subDir, DefaultGlobal());
        Assert.True(first.EffectiveRules.ContainsKey("PE001"),
            "Should walk up to ancestor and find PE001.");

        // Simulate editing the ancestor file, then manually invalidate (mirrors what watcher does)
        WriteCaSettings(_tempDir, new { rules = new { PE001 = new { enabled = false }, BP005 = new { enabled = false } } });
        _loader.InvalidateCache();

        // Reload from child — must now see BP005 added by the ancestor edit
        var second = _loader.Load(subDir, DefaultGlobal());
        Assert.True(second.EffectiveRules.ContainsKey("BP005"),
            "After ancestor .casettings edit + InvalidateCache, child-dir reload must pick up BP005.");
    }

    // ─── PR #247 fix 2: watcher callback fires InvalidateCache(), not InvalidateDirectory ──
    // Uses a real FileSystemWatcher, so we poll with a generous timeout.
    // If FS-watcher timing proves flaky in CI this test can be skipped via:
    //   [Trait("Category", "Integration")]

    [Fact]
    public void AncestorFileEdit_ViaRealWatcher_InvalidatesChildDirCacheEntry()
    {
        // Arrange: place .casettings in parent only
        WriteCaSettings(_tempDir, new { rules = new { PE001 = new { enabled = false } } });

        var subDir = Path.Combine(_tempDir, "Sub");
        Directory.CreateDirectory(subDir);

        // Load from child; watcher is armed on the ancestor dir by Load()
        var initial = _loader.Load(subDir, DefaultGlobal());
        Assert.True(initial.EffectiveRules.ContainsKey("PE001"),
            "Initial load from child should walk up and find PE001.");

        // Act: edit the ancestor .casettings — adds SE002
        WriteCaSettings(_tempDir, new
        {
            rules = new
            {
                PE001 = new { enabled = false },
                SE002 = new { enabled = false }
            }
        });

        // Poll until the FileSystemWatcher fires and the cache is invalidated, then verify.
        // Allow up to 3 seconds — watcher OS latency is typically <100 ms.
        ResolvedAnalysisSettings? reloaded = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var candidate = _loader.Load(subDir, DefaultGlobal());
            if (candidate.EffectiveRules.ContainsKey("SE002"))
            {
                reloaded = candidate;
                break;
            }
            Thread.Sleep(50);
        }

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.EffectiveRules.ContainsKey("SE002"),
            "After editing ancestor .casettings, child-dir cache must be invalidated and SE002 must appear.");
    }
}

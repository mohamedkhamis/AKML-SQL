using System.Text.Json;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T131, research R53, FR-053) — the editor options: font size 13, no word wrap, tab size 4 by default;
/// font size 9–32 and tab size 1–8; key <c>editorSettings</c> in the <c>analysisSettings</c> store.
/// </summary>
public sealed class EditorSettingsStoreTests
{
    [Fact]
    public async Task A_fresh_store_has_the_defaults()
    {
        var store = new EditorSettingsStore(new RecordingIndexedDbAdapter());

        var settings = await store.GetAsync();

        Assert.Equal(13, settings.FontSize);
        Assert.False(settings.WordWrap);
        Assert.Equal(4, settings.TabSize);
    }

    [Fact]
    public async Task Settings_are_saved_under_editorSettings_in_the_analysisSettings_store()
    {
        var db = new RecordingIndexedDbAdapter();
        await new EditorSettingsStore(db).SetAsync(new EditorSettings { FontSize = 16, WordWrap = true, TabSize = 2 });

        var (store, key, _) = Assert.Single(db.Writes);
        Assert.Equal(StoreNames.AnalysisSettings, store);
        Assert.Equal("editorSettings", key);

        var read = await new EditorSettingsStore(db).GetAsync();
        Assert.Equal(16, read.FontSize);
        Assert.True(read.WordWrap);
        Assert.Equal(2, read.TabSize);
    }

    [Theory]
    [InlineData(4, 9)]
    [InlineData(99, 32)]
    [InlineData(20, 20)]
    public async Task The_font_size_is_kept_between_9_and_32(int stored, int expected)
    {
        var db = new RecordingIndexedDbAdapter();
        db.Seed(StoreNames.AnalysisSettings, "editorSettings", JsonSerializer.Serialize(new { FontSize = stored, TabSize = 4 }));

        Assert.Equal(expected, (await new EditorSettingsStore(db).GetAsync()).FontSize);

        var store = new EditorSettingsStore(new RecordingIndexedDbAdapter());
        await store.SetAsync(new EditorSettings { FontSize = stored });
        Assert.Equal(expected, (await store.GetAsync()).FontSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 8)]
    [InlineData(3, 3)]
    public async Task The_tab_size_is_kept_between_1_and_8(int stored, int expected)
    {
        var store = new EditorSettingsStore(new RecordingIndexedDbAdapter());
        await store.SetAsync(new EditorSettings { TabSize = stored });

        Assert.Equal(expected, (await store.GetAsync()).TabSize);
    }

    // ── Spec 043 (T062, research R12, FR-017): the editor font ───────────────────────────────────

    [Fact]
    public async Task The_font_is_the_default_until_one_is_chosen_and_then_it_is_kept()
    {
        var db = new RecordingIndexedDbAdapter();
        Assert.Equal(EditorFontFamily.Default, (await new EditorSettingsStore(db).GetAsync()).FontFamily);

        await new EditorSettingsStore(db).SetAsync(new EditorSettings { FontFamily = EditorFontFamily.CascadiaMono });

        Assert.Equal(EditorFontFamily.CascadiaMono, (await new EditorSettingsStore(db).GetAsync()).FontFamily);
    }

    [Fact]
    public async Task A_record_from_before_the_font_setting_or_with_an_unknown_font_reads_the_default_font()
    {
        var db = new RecordingIndexedDbAdapter();
        db.Seed(StoreNames.AnalysisSettings, "editorSettings", JsonSerializer.Serialize(new { FontSize = 15, TabSize = 4 }));
        var old = await new EditorSettingsStore(db).GetAsync();
        Assert.Equal(EditorFontFamily.Default, old.FontFamily);
        Assert.Equal(15, old.FontSize);

        var odd = new RecordingIndexedDbAdapter();
        odd.Seed(StoreNames.AnalysisSettings, "editorSettings", JsonSerializer.Serialize(new { FontSize = 13, FontFamily = 99 }));
        Assert.Equal(EditorFontFamily.Default, (await new EditorSettingsStore(odd).GetAsync()).FontFamily);
    }

    [Theory]
    [InlineData(EditorFontFamily.Default, "default")]
    [InlineData(EditorFontFamily.CascadiaMono, "cascadia-mono")]
    [InlineData(EditorFontFamily.Consolas, "consolas")]
    [InlineData(EditorFontFamily.SystemMonospace, "system-monospace")]
    public void Each_font_has_the_name_the_editor_module_knows(EditorFontFamily font, string name) =>
        Assert.Equal(name, EditorSettings.FontName(font));

    [Fact]
    public async Task A_corrupt_record_reads_as_the_defaults()
    {
        var db = new RecordingIndexedDbAdapter();
        db.Seed(StoreNames.AnalysisSettings, "editorSettings", "{not json");

        Assert.Equal(13, (await new EditorSettingsStore(db).GetAsync()).FontSize);
    }

    [Fact]
    public async Task Changed_is_raised_on_save_and_on_reset()
    {
        var store = new EditorSettingsStore(new RecordingIndexedDbAdapter());
        int changed = 0;
        store.Changed += () => changed++;

        await store.SetAsync(new EditorSettings { FontSize = 15 });
        await store.ResetAsync();

        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task Restore_defaults_deletes_only_its_own_key()
    {
        var db = new RecordingIndexedDbAdapter();
        var store = new EditorSettingsStore(db);
        await store.SetAsync(new EditorSettings { FontSize = 18 });
        db.ResetLog();

        await store.ResetAsync();

        Assert.Equal(new[] { (StoreNames.AnalysisSettings, "editorSettings") }, db.Deletes);
        Assert.Empty(db.Clears);
        Assert.Equal(13, (await store.GetAsync()).FontSize);
    }

    [Fact]
    public async Task Reload_reads_what_another_tab_saved()
    {
        var db = new RecordingIndexedDbAdapter();
        var first = new EditorSettingsStore(db);
        await first.GetAsync();
        await new EditorSettingsStore(db).SetAsync(new EditorSettings { FontSize = 20 });

        await first.ReloadAsync();

        Assert.Equal(20, (await first.GetAsync()).FontSize);
    }
}

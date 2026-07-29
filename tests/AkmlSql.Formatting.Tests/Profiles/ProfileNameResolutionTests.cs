using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Pins the List()/Load() key agreement invariant.
///
/// <para>The bug this locks down: <see cref="ProfileManager.List"/> reports each profile's
/// <c>metadata.name</c> (read from inside the file), while <see cref="ProfileManager.Load"/>
/// resolved a PATH derived from that name (<c>SanitizeFileName(name) + ".akmlstyle"</c>). The
/// shipped built-ins use kebab-case filenames with Title-Case metadata names
/// (<c>khamis-style.akmlstyle</c> → <c>"Khamis Style"</c>), so every multi-word style was
/// unloadable by the only name the UI/config ever knows. Single-word styles worked purely by
/// accident of case-insensitive filesystems (<c>Compact</c> → <c>compact.akmlstyle</c>).</para>
///
/// <para>Impact was product-wide, not just cosmetic: <c>FormatterSettings.ActiveProfile</c>
/// defaults to <c>"Khamis Style"</c> and <c>FormatRequestHandler.LoadProfile</c> swallows the
/// resulting <see cref="FileNotFoundException"/> into a bare <c>new FormattingProfile()</c> — so
/// the product's own default style silently formatted with POCO defaults instead.</para>
/// </summary>
public class ProfileNameResolutionTests
{
    /// <summary>The shipped built-in profile directory (repo source of truth).</summary>
    private static string BuiltInDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn");
    }

    /// <summary>A ProfileManager over the real built-ins with an isolated (empty) custom dir.</summary>
    private static ProfileManager CreateManager(out string customDir)
    {
        customDir = Path.Combine(Path.GetTempPath(), "akmlsql-profile-resolution-" + Guid.NewGuid());
        return new ProfileManager(BuiltInDir(), customDir);
    }

    [Fact]
    public void EveryListedProfile_IsLoadableByItsListedName()
    {
        var manager = CreateManager(out _);
        var listed = manager.List();

        Assert.NotEmpty(listed); // guard: a broken BuiltInDir() would make this vacuous

        var unloadable = listed
            .Select(m => m.Name)
            .Where(name =>
            {
                try { manager.Load(name); return false; }
                catch (FileNotFoundException) { return true; }
            })
            .ToList();

        Assert.True(unloadable.Count == 0,
            "List() reported names that Load() cannot resolve: " + string.Join(", ", unloadable));
    }

    [Theory]
    // The three shipped styles whose filename differs from their metadata name.
    [InlineData("Khamis Style")]
    [InlineData("Leading Commas")]
    [InlineData("AlignedLeftBracket")]
    public void MultiWordBuiltIn_LoadsByItsDisplayName(string displayName)
    {
        var manager = CreateManager(out _);

        var profile = manager.Load(displayName);

        Assert.Equal(displayName, profile.Metadata.Name);
        Assert.True(profile.Metadata.IsBuiltIn, "resolved from the built-in directory");
    }

    [Fact]
    public void ShippedDefaultActiveProfile_IsLoadable()
    {
        // FormatterSettings.ActiveProfile defaults to "Khamis Style" and FormatRequestDispatcher
        // sends exactly that string, so this name MUST resolve or the default style never applies.
        var manager = CreateManager(out _);

        var profile = manager.Load("Khamis Style");

        Assert.Equal("Khamis Style", profile.Metadata.Name);
    }

    [Fact]
    public void ExactFilenameLookup_StillWins_ForCustomShadowingBuiltIn()
    {
        // Regression guard for the fix: adding metadata-name resolution must NOT break the
        // custom-shadows-built-in precedence that Load()/TryReadRaw() promise.
        var manager = CreateManager(out var customDir);
        Directory.CreateDirectory(customDir);
        try
        {
            // A custom file whose FILENAME matches the built-in display name, with distinct content.
            var custom = new FormattingProfile();
            custom.Metadata.Name = "Khamis Style";
            custom.Metadata.Description = "custom-shadow-marker";
            File.WriteAllText(
                Path.Combine(customDir, "Khamis Style.akmlstyle"),
                ProfileSerializer.Serialize(custom));

            var loaded = manager.Load("Khamis Style");

            Assert.Equal("custom-shadow-marker", loaded.Metadata.Description);
            Assert.False(loaded.Metadata.IsBuiltIn, "a custom shadow stays editable");
        }
        finally
        {
            try { Directory.Delete(customDir, recursive: true); } catch (IOException) { }
        }
    }
}

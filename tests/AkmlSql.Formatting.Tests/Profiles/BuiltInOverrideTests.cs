using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Editing a built-in style, and resetting it.
/// <para>
/// Built-ins used to be read-only: <see cref="ProfileManager.Save"/> threw, and the only way to
/// change one was to duplicate it under a new name. That left people with "Khamis Style copy" as
/// their real style while the style they actually wanted to adjust sat untouched beside it.
/// </para>
/// <para>
/// They are editable now, and the mechanism is the shadowing that <see cref="ProfileManager.Load"/>
/// already did: an edit writes a CUSTOM file under the same name, which resolves ahead of the
/// shipped one. The shipped file is never written to — that is what lets Reset restore the exact
/// original rather than a reconstruction of it, and it is the property these tests guard hardest.
/// </para>
/// </summary>
public class BuiltInOverrideTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly ProfileManager _profiles;

    public BuiltInOverrideTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "akmlsql-builtin-override-" + Guid.NewGuid());
        _builtInDir = Path.Combine(root, "builtin");
        _customDir = Path.Combine(root, "custom");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        _profiles = new ProfileManager(_builtInDir, _customDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_builtInDir)!, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes a built-in whose FILENAME differs from its metadata name, the way the shipped ones
    /// do ("khamis-style.akmlstyle" → "Khamis Style"). Every override and reset path has to resolve
    /// through the metadata-name tier, so testing with a matching filename would test the easy half.
    /// </summary>
    private string WriteBuiltIn(string fileStem, string displayName, string keywordCasing)
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = displayName;
        profile.Metadata.IsBuiltIn = true;
        profile.Casing.ReservedKeywords = keywordCasing;

        var path = Path.Combine(_builtInDir, fileStem + ".akmlstyle");
        File.WriteAllText(path, ProfileSerializer.Serialize(profile));
        return path;
    }

    private void Edit(string name, string keywordCasing)
    {
        var profile = _profiles.Load(name);
        profile.Casing.ReservedKeywords = keywordCasing;
        _profiles.Save(profile);
    }

    [Fact]
    public void EditingABuiltIn_WritesAnOverride_AndNeverTouchesTheShippedFile()
    {
        var shippedPath = WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        var shippedBefore = File.ReadAllBytes(shippedPath);

        Edit("Khamis Style", "lowercase");

        // The edit is what loads now...
        Assert.Equal("lowercase", _profiles.Load("Khamis Style").Casing.ReservedKeywords);

        // ...and the shipped file is byte-for-byte what it was. This is the whole safety property:
        // a bad edit can always be undone because the original was never the thing being written.
        Assert.Equal(shippedBefore, File.ReadAllBytes(shippedPath));
        Assert.Single(Directory.GetFiles(_builtInDir, "*.akmlstyle"));
    }

    [Fact]
    public void AnEditedBuiltIn_IsReportedAsShippedAndChanged()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        Assert.True(_profiles.HasBuiltIn("Khamis Style"));
        Assert.False(_profiles.IsCustomizedBuiltIn("Khamis Style"));

        Edit("Khamis Style", "lowercase");

        Assert.True(_profiles.HasBuiltIn("Khamis Style"));
        Assert.True(_profiles.IsCustomizedBuiltIn("Khamis Style"));

        // In the list it is no longer "built-in" (the file that resolves is the custom one) but it
        // is still shipped. Neither flag alone can say that, which is why there are two.
        var listed = _profiles.List().Single(m => m.Name == "Khamis Style");
        Assert.False(listed.IsBuiltIn);
        Assert.True(listed.IsCustomizedBuiltIn);
    }

    [Fact]
    public void Reset_RestoresTheShippedStyleExactly()
    {
        var shippedPath = WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        var shippedText = File.ReadAllText(shippedPath);

        Edit("Khamis Style", "lowercase");
        Assert.Equal("lowercase", _profiles.Load("Khamis Style").Casing.ReservedKeywords);

        Assert.True(_profiles.ResetToBuiltIn("Khamis Style"));

        // Byte-identical, not merely equivalent: the shipped file is what resolves again, so there
        // is no re-serialization step that could drop a field the editor does not model.
        Assert.True(_profiles.TryReadRaw("Khamis Style", out var json, out var isBuiltIn));
        Assert.Equal(shippedText, json);
        Assert.True(isBuiltIn);
        Assert.Equal("UPPERCASE", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
        Assert.False(_profiles.IsCustomizedBuiltIn("Khamis Style"));
        Assert.Empty(Directory.GetFiles(_customDir, "*.akmlstyle"));
    }

    [Fact]
    public void Reset_OnAnUneditedBuiltIn_IsANoOp_NotAnError()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        // The end state is what was asked for either way, so this reports "nothing to discard"
        // rather than failing. The UI uses the difference to avoid claiming it undid edits.
        Assert.False(_profiles.ResetToBuiltIn("Khamis Style"));
        Assert.Equal("UPPERCASE", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
    }

    [Fact]
    public void Reset_OnAStyleThatNeverShipped_RefusesAndKeepsIt()
    {
        var mine = new FormattingProfile();
        mine.Metadata.Name = "My Style";
        mine.Casing.ReservedKeywords = "lowercase";
        _profiles.Save(mine);

        // "Reset" on a style with no original could only mean "wipe it to defaults", which destroys
        // work under a word that does not imply it. Refusing is the whole point of the test.
        var ex = Assert.Throws<InvalidOperationException>(() => _profiles.ResetToBuiltIn("My Style"));
        Assert.Contains("not a built-in", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("lowercase", _profiles.Load("My Style").Casing.ReservedKeywords);
        Assert.Single(Directory.GetFiles(_customDir, "*.akmlstyle"));
    }

    [Fact]
    public void Delete_OnAnEditedBuiltIn_RefusesInsteadOfSilentlyRemovingTheOverride()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        Edit("Khamis Style", "lowercase");

        // Once built-ins became editable, an edited one IS a custom file. Delete's old
        // "no custom file? then check built-in" order would have deleted the override and reported
        // success -- after which the built-in reappears, looking like the delete silently failed.
        var ex = Assert.Throws<InvalidOperationException>(() => _profiles.Delete("Khamis Style"));
        Assert.Contains("reset", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("lowercase", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
        Assert.Single(Directory.GetFiles(_customDir, "*.akmlstyle"));
    }

    [Fact]
    public void AnOverrideFileDoesNotClaimToBeBuiltIn()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        // The JSON being saved is derived from the built-in's own text, so it arrives carrying
        // "isBuiltIn": true. The file being written lives in the custom directory, so persisting
        // that would put a false statement in the file.
        Edit("Khamis Style", "lowercase");

        var overridePath = Directory.GetFiles(_customDir, "*.akmlstyle").Single();
        Assert.DoesNotContain("\"isBuiltIn\": true", File.ReadAllText(overridePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_RemovesAnOverrideWhoseFilenameDiffersFromItsStyleName()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        // A style the user dropped in themselves carries the name only in its metadata, so the
        // filename probe alone would miss it and Reset would report success while the override
        // kept on shadowing the built-in.
        var handWritten = new FormattingProfile();
        handWritten.Metadata.Name = "Khamis Style";
        handWritten.Casing.ReservedKeywords = "lowercase";
        File.WriteAllText(
            Path.Combine(_customDir, "whatever-i-called-it.akmlstyle"),
            ProfileSerializer.Serialize(handWritten));

        Assert.True(_profiles.IsCustomizedBuiltIn("Khamis Style"));
        Assert.True(_profiles.ResetToBuiltIn("Khamis Style"));

        Assert.Empty(Directory.GetFiles(_customDir, "*.akmlstyle"));
        Assert.Equal("UPPERCASE", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
    }

    [Fact]
    public void AnEditSurvivesAReload_AndResetIsRepeatable()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        Edit("Khamis Style", "lowercase");
        Assert.Equal("lowercase", new ProfileManager(_builtInDir, _customDir).Load("Khamis Style").Casing.ReservedKeywords);

        Assert.True(_profiles.ResetToBuiltIn("Khamis Style"));
        Assert.False(_profiles.ResetToBuiltIn("Khamis Style"));   // idempotent, not an error

        Edit("Khamis Style", "PascalCase");
        Assert.Equal("PascalCase", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
        Assert.True(_profiles.ResetToBuiltIn("Khamis Style"));
        Assert.Equal("UPPERCASE", _profiles.Load("Khamis Style").Casing.ReservedKeywords);
    }
}

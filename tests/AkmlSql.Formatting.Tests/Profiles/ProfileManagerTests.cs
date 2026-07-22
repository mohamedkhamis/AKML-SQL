using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Tests for ProfileManager using temporary directories.
/// </summary>
public class ProfileManagerTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly ProfileManager _manager;

    public ProfileManagerTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "akmlsql-pm-test-" + Guid.NewGuid());
        _builtInDir = Path.Combine(root, "builtin");
        _customDir = Path.Combine(root, "custom");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        _manager = new ProfileManager(_builtInDir, _customDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_builtInDir)!, recursive: true); }
        catch
        {
            // ignored
        }
    }

    private static string ProfileJson(string name, bool isBuiltIn = false, int schemaVersion = 1)
    {
        var p = new FormattingProfile
        {
            Metadata =
            {
                Name = name,
                IsBuiltIn = isBuiltIn,
                SchemaVersion = schemaVersion
            }
        };
        return ProfileSerializer.Serialize(p);
    }

    private void WriteBuiltIn(string name)
    {
        var sanitized = name.Replace(" ", "");
        File.WriteAllText(Path.Combine(_builtInDir, sanitized + ".akmlstyle"), ProfileJson(name, isBuiltIn: true));
    }

    private void WriteCustom(string name)
    {
        var sanitized = name.Replace(" ", "");
        File.WriteAllText(Path.Combine(_customDir, sanitized + ".akmlstyle"), ProfileJson(name));
    }

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullBuiltInPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileManager(null!, _customDir));
    }

    [Fact]
    public void Constructor_NullCustomPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileManager(_builtInDir, null!));
    }

    // ── CreateDefault ─────────────────────────────────────────────────────

    [Fact]
    public void CreateDefault_ReturnsInstance()
    {
        var pm = ProfileManager.CreateDefault();
        Assert.NotNull(pm);
    }

    // ── Save + Load ───────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTrip()
    {
        var profile = new FormattingProfile
        {
            Metadata =
            {
                Name = "MyProfile"
            },
            Whitespace =
            {
                TabSize = 2
            }
        };

        _manager.Save(profile);
        var loaded = _manager.Load("MyProfile");

        Assert.Equal("MyProfile", loaded.Metadata.Name);
        Assert.Equal(2, loaded.Whitespace.TabSize);
    }

    [Fact]
    public void Save_CreatesFileInCustomDir()
    {
        var profile = new FormattingProfile { Metadata = { Name = "SaveTest" } };
        _manager.Save(profile);
        Assert.True(File.Exists(Path.Combine(_customDir, "SaveTest.akmlstyle")));
    }

    [Fact]
    public void Save_NullProfile_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _manager.Save(null!));
    }

    [Fact]
    public void Save_EmptyName_Throws()
    {
        var profile = new FormattingProfile { Metadata = { Name = "" } };
        Assert.Throws<ArgumentException>(() => _manager.Save(profile));
    }

    [Fact]
    public void Save_IsBuiltIn_Throws()
    {
        var profile = new FormattingProfile { Metadata = { Name = "Test", IsBuiltIn = true } };
        Assert.Throws<InvalidOperationException>(() => _manager.Save(profile));
    }

    [Fact]
    public void Save_OverBuiltInWithoutCustomCopy_Throws()
    {
        WriteBuiltIn("Builtin");
        var profile = new FormattingProfile { Metadata = { Name = "Builtin", IsBuiltIn = false } };
        Assert.Throws<InvalidOperationException>(() => _manager.Save(profile));
    }

    [Fact]
    public void Save_OverBuiltInWithExistingCustomCopy_Succeeds()
    {
        WriteBuiltIn("Shared");
        WriteCustom("Shared");
        var profile = new FormattingProfile { Metadata = { Name = "Shared" } };
        _manager.Save(profile); // should not throw
    }

    // ── Load ──────────────────────────────────────────────────────────────

    [Fact]
    public void Load_CustomProfile_ReturnsCustom()
    {
        WriteCustom("Custom");
        var p = _manager.Load("Custom");
        Assert.Equal("Custom", p.Metadata.Name);
        Assert.False(p.Metadata.IsBuiltIn);
    }

    [Fact]
    public void Load_BuiltInProfile_ReturnsBuiltIn()
    {
        WriteBuiltIn("Default");
        var p = _manager.Load("Default");
        Assert.Equal("Default", p.Metadata.Name);
        Assert.True(p.Metadata.IsBuiltIn);
    }

    [Fact]
    public void Load_CustomShadowsBuiltIn()
    {
        WriteBuiltIn("Shared");
        WriteCustom("Shared");
        var p = _manager.Load("Shared");
        // Custom is found first — IsBuiltIn is false from the custom file
        Assert.False(p.Metadata.IsBuiltIn);
    }

    [Fact]
    public void Load_NotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => _manager.Load("DoesNotExist"));
    }

    [Fact]
    public void Load_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _manager.Load(null!));
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingCustom_ReturnsTrueAndRemovesFile()
    {
        WriteCustom("ToDelete");
        var result = _manager.Delete("ToDelete");
        Assert.True(result);
        Assert.False(File.Exists(Path.Combine(_customDir, "ToDelete.akmlstyle")));
    }

    [Fact]
    public void Delete_NotExisting_ReturnsFalse()
    {
        var result = _manager.Delete("NonExistent");
        Assert.False(result);
    }

    [Fact]
    public void Delete_BuiltInProfile_Throws()
    {
        WriteBuiltIn("BuiltInOnly");
        Assert.Throws<InvalidOperationException>(() => _manager.Delete("BuiltInOnly"));
    }

    [Fact]
    public void Delete_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _manager.Delete(null!));
    }

    // ── List ──────────────────────────────────────────────────────────────

    [Fact]
    public void List_EmptyDirs_ReturnsEmpty()
    {
        var list = _manager.List();
        Assert.Empty(list);
    }

    [Fact]
    public void List_ReturnsAllProfiles()
    {
        WriteBuiltIn("A");
        WriteBuiltIn("B");
        WriteCustom("C");
        var list = _manager.List();
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void List_CustomShadowsBuiltIn()
    {
        WriteBuiltIn("Shared");
        WriteCustom("Shared");
        var list = _manager.List();
        Assert.Single(list);
        Assert.False(list[0].IsBuiltIn);
    }

    [Fact]
    public void List_IsSortedByName()
    {
        WriteBuiltIn("Charlie");
        WriteCustom("Alpha");
        WriteBuiltIn("Beta");
        var list = _manager.List();
        Assert.Equal(["Alpha", "Beta", "Charlie"], list.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void List_NonExistentBuiltInDir_SkipsIt()
    {
        var manager = new ProfileManager(
            Path.Combine(Path.GetTempPath(), "akml-nonexistent-" + Guid.NewGuid()),
            _customDir);
        WriteCustom("OnlyCustom");
        var list = manager.List();
        Assert.Single(list);
    }

    // ── GetBuiltIn ────────────────────────────────────────────────────────

    [Fact]
    public void GetBuiltIn_NoBuiltInDir_ReturnsEmpty()
    {
        var manager = new ProfileManager(
            Path.Combine(Path.GetTempPath(), "akml-nonexistent-" + Guid.NewGuid()),
            _customDir);
        Assert.Empty(manager.GetBuiltIn());
    }

    [Fact]
    public void GetBuiltIn_ReturnsBuiltInProfiles()
    {
        WriteBuiltIn("B1");
        WriteBuiltIn("B2");
        var builtIns = _manager.GetBuiltIn();
        Assert.Equal(2, builtIns.Count);
        Assert.All(builtIns, p => Assert.True(p.Metadata.IsBuiltIn));
    }

    [Fact]
    public void GetBuiltIn_MalformedFile_IsSkipped()
    {
        File.WriteAllText(Path.Combine(_builtInDir, "bad.akmlstyle"), "{ INVALID }");
        WriteBuiltIn("Good");
        var builtIns = _manager.GetBuiltIn();
        Assert.Single(builtIns);
    }

    // ── Duplicate ─────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_CreatesNewProfile()
    {
        WriteBuiltIn("Original");
        var copy = _manager.Duplicate("Original", "MyCopy");

        Assert.Equal("MyCopy", copy.Metadata.Name);
        Assert.False(copy.Metadata.IsBuiltIn);
        Assert.Equal("Original", copy.Metadata.BasedOn);
        Assert.NotEqual("Original", copy.Metadata.Id);
    }

    [Fact]
    public void Duplicate_SavedToCustomDir()
    {
        WriteBuiltIn("Source");
        _manager.Duplicate("Source", "Dest");
        Assert.True(File.Exists(Path.Combine(_customDir, "Dest.akmlstyle")));
    }

    // ── Export ────────────────────────────────────────────────────────────

    [Fact]
    public void Export_CreatesFileAtDestination()
    {
        WriteCustom("ExportMe");
        var dest = Path.Combine(Path.GetTempPath(), "exported-" + Guid.NewGuid() + ".akmlstyle");
        try
        {
            _manager.Export("ExportMe", dest);
            Assert.True(File.Exists(dest));
            var json = File.ReadAllText(dest);
            Assert.Contains("ExportMe", json);
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    // ── Import ────────────────────────────────────────────────────────────

    [Fact]
    public void Import_FromValidFile_LoadsAndSaves()
    {
        var srcPath = Path.Combine(Path.GetTempPath(), "import-src-" + Guid.NewGuid() + ".akmlstyle");
        try
        {
            File.WriteAllText(srcPath, ProfileJson("Imported"));
            var profile = _manager.Import(srcPath);
            Assert.Equal("Imported", profile.Metadata.Name);
            Assert.False(profile.Metadata.IsBuiltIn);
            Assert.True(File.Exists(Path.Combine(_customDir, "Imported.akmlstyle")));
        }
        finally
        {
            if (File.Exists(srcPath)) File.Delete(srcPath);
        }
    }

    [Fact]
    public void Import_WithNewName_UsesNewName()
    {
        var srcPath = Path.Combine(Path.GetTempPath(), "import-src-" + Guid.NewGuid() + ".akmlstyle");
        try
        {
            File.WriteAllText(srcPath, ProfileJson("OriginalName"));
            var profile = _manager.Import(srcPath, "RenamedProfile");
            Assert.Equal("RenamedProfile", profile.Metadata.Name);
            Assert.Equal("OriginalName", profile.Metadata.BasedOn);
        }
        finally
        {
            if (File.Exists(srcPath)) File.Delete(srcPath);
        }
    }

    [Fact]
    public void Import_FileNotFound_Throws()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent.akmlstyle");
        Assert.Throws<FileNotFoundException>(() => _manager.Import(fakePath));
    }

    // ── SanitizeFileName (via Save with special chars) ─────────────────────

    [Fact]
    public void Save_ProfileWithSpecialChars_SanitizesName()
    {
        // SanitizeFileName removes "..", "/", "\" and invalid chars
        var profile = new FormattingProfile { Metadata = { Name = "Valid Name" } };
        _manager.Save(profile); // "Valid Name" is valid, creates "Valid Name.akmlstyle"
        var loaded = _manager.Load("Valid Name");
        Assert.Equal("Valid Name", loaded.Metadata.Name);
    }

    [Fact]
    public void SanitizeFileName_EmptyAfterSanitization_Throws()
    {
        // "../.." becomes "" after removing ".." → throws ArgumentException
        var profile = new FormattingProfile { Metadata = { Name = "../.." } };
        Assert.Throws<ArgumentException>(() => _manager.Save(profile));
    }

    // ── GetCustomArtifactPath (SanitizeFileName + ValidatePathWithinBase pairing) ─────

    [Fact]
    public void GetCustomArtifactPath_HostileTraversalName_StaysInsideCustomDir()
    {
        // SanitizeFileName strips "..", "/" and "\" so "..\..\evil" collapses to "evil";
        // ValidatePathWithinBase then confirms the canonical path is under the custom dir.
        var path = _manager.GetCustomArtifactPath("..\\..\\evil", ".source.json");

        Assert.StartsWith(Path.GetFullPath(_customDir), Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("evil.source.json", Path.GetFileName(path));
    }

    // ── TryReadRaw (spec 033 T005 — ProfileGet's raw-text read) ───────────────────────

    [Fact]
    public void TryReadRaw_CustomProfile_ReturnsFileTextVerbatim()
    {
        // Unknown key nested INSIDE a group would be dropped by Deserialize→Serialize;
        // TryReadRaw must return the exact bytes so it survives as a merge base.
        const string raw = "{\n  \"metadata\": { \"name\": \"RawStyle\" },\n" +
                           "  \"whitespace\": { \"someFutureKey\": 42 },\n" +
                           "  \"unknownRootKey\": \"kept\"\n}";
        File.WriteAllText(Path.Combine(_customDir, "RawStyle.akmlstyle"), raw);

        var found = _manager.TryReadRaw("RawStyle", out var json, out var isBuiltIn);

        Assert.True(found);
        Assert.False(isBuiltIn);
        Assert.Equal(raw, json);
    }

    [Fact]
    public void TryReadRaw_BuiltIn_ReportsIsBuiltIn()
    {
        WriteBuiltIn("Default");

        var found = _manager.TryReadRaw("Default", out var json, out var isBuiltIn);

        Assert.True(found);
        Assert.True(isBuiltIn);
        Assert.Contains("\"Default\"", json);
    }

    [Fact]
    public void TryReadRaw_CustomShadowsBuiltIn_ReportsEditable()
    {
        WriteBuiltIn("Shadowed");
        File.WriteAllText(Path.Combine(_customDir, "Shadowed.akmlstyle"), ProfileJson("Shadowed"));

        var found = _manager.TryReadRaw("Shadowed", out _, out var isBuiltIn);

        Assert.True(found);
        Assert.False(isBuiltIn); // custom-first resolution wins — editable
    }

    [Fact]
    public void TryReadRaw_CustomFileLyingIsBuiltInTrue_StillReportsEditable()
    {
        // The read-only flag must come from WHICH DIRECTORY resolved, never from the JSON:
        // a custom file claiming "isBuiltIn": true must not become read-only (research R1 trap).
        File.WriteAllText(Path.Combine(_customDir, "Liar.akmlstyle"),
            ProfileJson("Liar", isBuiltIn: true));

        var found = _manager.TryReadRaw("Liar", out _, out var isBuiltIn);

        Assert.True(found);
        Assert.False(isBuiltIn);
    }

    [Fact]
    public void TryReadRaw_UnknownName_ReturnsFalse()
    {
        var found = _manager.TryReadRaw("NoSuchStyle", out var json, out var isBuiltIn);

        Assert.False(found);
        Assert.Equal(string.Empty, json);
        Assert.False(isBuiltIn);
    }

    // ── Rename (spec 033 T028 — atomic custom-profile rename) ─────────────────────────

    [Fact]
    public void Rename_RenamesFileAndRewritesMetadataName()
    {
        _manager.Save(new FormattingProfile { Metadata = { Name = "Original" } });

        var final = _manager.Rename("Original", "Renamed");

        Assert.Equal("Renamed", final);
        Assert.False(File.Exists(Path.Combine(_customDir, "Original.akmlstyle")));
        Assert.True(File.Exists(Path.Combine(_customDir, "Renamed.akmlstyle")));

        // Old name unloadable, new name loadable, JSON name consistent with the filename.
        Assert.Throws<FileNotFoundException>(() => _manager.Load("Original"));
        var loaded = _manager.Load("Renamed");
        Assert.Equal("Renamed", loaded.Metadata.Name);
    }

    [Fact]
    public void Rename_PreservesUnknownNestedFields()
    {
        const string raw = "{\"metadata\":{\"name\":\"KeepFields\"},\"whitespace\":{\"futureKey\":42}}";
        File.WriteAllText(Path.Combine(_customDir, "KeepFields.akmlstyle"), raw);

        _manager.Rename("KeepFields", "KeptFields");

        var renamed = File.ReadAllText(Path.Combine(_customDir, "KeptFields.akmlstyle"));
        Assert.Contains("futureKey", renamed);
        Assert.Contains("\"KeptFields\"", renamed);
    }

    [Fact]
    public void Rename_BuiltInSource_Throws()
    {
        WriteBuiltIn("Default");
        Assert.Throws<InvalidOperationException>(() => _manager.Rename("Default", "MyDefault"));
    }

    [Fact]
    public void Rename_UnknownSource_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => _manager.Rename("Ghost", "Anything"));
    }

    [Fact]
    public void Rename_CollisionWithCustom_Throws_CaseInsensitive()
    {
        _manager.Save(new FormattingProfile { Metadata = { Name = "Alpha" } });
        _manager.Save(new FormattingProfile { Metadata = { Name = "Beta" } });

        Assert.Throws<InvalidOperationException>(() => _manager.Rename("Alpha", "BETA"));
    }

    [Fact]
    public void Rename_CollisionWithBuiltInName_Throws()
    {
        WriteBuiltIn("Compact");
        _manager.Save(new FormattingProfile { Metadata = { Name = "Mine" } });

        Assert.Throws<InvalidOperationException>(() => _manager.Rename("Mine", "Compact"));
    }

    [Fact]
    public void Rename_CaseOnly_Succeeds()
    {
        _manager.Save(new FormattingProfile { Metadata = { Name = "my style" } });

        var final = _manager.Rename("my style", "My Style");

        Assert.Equal("My Style", final);
        var loaded = _manager.Load("My Style");
        Assert.Equal("My Style", loaded.Metadata.Name);
        // Exactly one profile file remains.
        Assert.Single(Directory.GetFiles(_customDir, "*.akmlstyle"));
    }

    [Fact]
    public void Rename_MovesSourceJsonSidecar()
    {
        _manager.Save(new FormattingProfile { Metadata = { Name = "Imported" } });
        var oldSidecar = _manager.GetCustomArtifactPath("Imported", ".source.json");
        File.WriteAllText(oldSidecar, "{\"verbatim\":true}");

        _manager.Rename("Imported", "ImportedRenamed");

        Assert.False(File.Exists(oldSidecar));
        var newSidecar = _manager.GetCustomArtifactPath("ImportedRenamed", ".source.json");
        Assert.True(File.Exists(newSidecar));
        Assert.Equal("{\"verbatim\":true}", File.ReadAllText(newSidecar));
    }
}

using System.Text.Json;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 031 addendum — generation drift-guard for the two new built-in styles ("Khamis Style" and
/// "Collapsed"). Both are produced by running a Redgate JSON style fixture through
/// <see cref="RedgateJsonStyleImporter"/>, stamping built-in metadata, and writing the result to
/// <c>src/AkmlSql.Formatting/Profiles/BuiltIn/</c> — mirroring how the six pre-existing built-ins
/// (default, compact, indented, aligned-left-bracket, leading-commas, minimalist) were authored.
///
/// <para><b>Determinism.</b> <see cref="ProfileSerializer.Serialize"/> stamps
/// <c>Metadata.Modified = DateTime.UtcNow</c> as a side effect on every call, which would make a
/// byte-exact golden comparison flaky (two runs a millisecond apart never match). Rather than
/// round-tripping through Serialize/Deserialize twice to strip the stamp back out again, this test
/// pins <c>Metadata.Created</c> and <c>Metadata.Modified</c> to a fixed sentinel *before*
/// serializing, then serializes via the same internal <see cref="ProfileJsonContext"/> that
/// <see cref="ProfileSerializer.Serialize"/> itself uses — reproducing the exact JSON shape/options
/// (<c>WriteIndented</c>, camelCase, ignore-null) while deliberately skipping only the
/// UtcNow-stamping line. <c>AkmlSql.Formatting.Tests</c> already has
/// <c>InternalsVisibleTo</c> access to <c>AkmlSql.Formatting</c> (see the csproj), so
/// <see cref="ProfileJsonContext"/> is reachable directly. Both sides of the comparison — the
/// freshly generated text and the committed <c>.akmlstyle</c> file — are produced by this exact
/// same deterministic path (the committed file was itself captured by this test), so no further
/// timestamp normalisation is required on read-back.</para>
///
/// <para><b>Capture vs compare</b> mirrors <c>FormatParityTests</c>' idiom: set
/// <c>AKML_UPDATE_BUILTIN_STYLES=1</c> (or simply delete the target file) to (re)write the built-in
/// under <c>src/AkmlSql.Formatting/Profiles/BuiltIn/</c>; otherwise the test asserts byte-exact
/// equality against the committed file, acting as a drift guard against accidental importer/mapping
/// changes silently altering a shipped built-in style.</para>
/// </summary>
public class BuiltInStyleGenerationTests
{
    private const string CaptureEnvVar = "AKML_UPDATE_BUILTIN_STYLES";
    private static readonly DateTime Sentinel = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// (fixture file under tests/AkmlSql.Formatting.Tests/Fixtures, display name, output .akmlstyle
    /// file under src/AkmlSql.Formatting/Profiles/BuiltIn, pinned metadata.id or null to keep the
    /// fixture's own metadata.id).
    /// </summary>
    public static IEnumerable<object?[]> Cases()
    {
        yield return new object?[] { "MohamedKhamis-style.json", "Khamis Style", "khamis-style.akmlstyle", null };
        yield return new object?[]
        {
            "Collapsed-style.json", "Collapsed", "collapsed.akmlstyle", "3f8a2b1c-9d4e-4f6a-8b2c-031c011a95ed",
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Generated_builtin_matches_committed_file(
        string fixtureFile, string displayName, string outputFile, string? pinnedId)
    {
        var repoRoot = FindRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "AkmlSql.Formatting.Tests", "Fixtures", fixtureFile);
        Assert.True(File.Exists(fixturePath), $"Fixture missing: {fixturePath}");

        var sourceJson = File.ReadAllText(fixturePath);
        var importResult = RedgateJsonStyleImporter.Import(sourceJson, fallbackName: displayName);
        Assert.True(importResult.Success, importResult.ParseError);

        var profile = importResult.Profile;
        var originalName = profile.Metadata.Name; // the fixture's own metadata.name, pre-rename

        if (pinnedId != null)
            profile.Metadata.Id = pinnedId;
        // else: keep the id the importer already read from the fixture's own metadata.id.

        profile.Metadata.Name = displayName;
        profile.Metadata.IsBuiltIn = true;
        profile.Metadata.Author = "AKML SQL"; // match the six hand-authored built-ins
        profile.Metadata.Description =
            $"Built-in style generated from Redgate style '{originalName}' via spec-031 importer";
        profile.Metadata.Created = Sentinel;
        profile.Metadata.Modified = Sentinel;

        // Deterministic serialize -- see class doc: same shape as ProfileSerializer.Serialize
        // without its Metadata.Modified = DateTime.UtcNow side effect.
        var generated = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.FormattingProfile);

        var outputPath = Path.Combine(repoRoot, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", outputFile);
        var shouldCapture = !File.Exists(outputPath) ||
            string.Equals(Environment.GetEnvironmentVariable(CaptureEnvVar), "1", StringComparison.Ordinal);

        if (shouldCapture)
        {
            File.WriteAllText(outputPath, generated);
            // Capture-mode still asserts non-empty output so a broken importer/mapping fails loudly
            // instead of silently writing a blank built-in.
            Assert.False(string.IsNullOrWhiteSpace(generated),
                $"Capture mode produced empty output for '{outputFile}'. Refusing to treat as captured.");
            return;
        }

        var committed = File.ReadAllText(outputPath);
        Assert.Equal(committed, generated);
    }

    /// <summary>
    /// Spec 031 addendum deliverable 3 — confirms the captured files are discoverable through the
    /// real <see cref="ProfileManager"/> surface (not just present on disk), pointed at the actual
    /// source <c>BuiltIn</c> directory rather than a synthetic temp fixture.
    /// </summary>
    [Fact]
    public void ProfileManager_surfaces_both_new_builtins()
    {
        var repoRoot = FindRepoRoot();
        var builtInDir = Path.Combine(repoRoot, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn");
        var customDir = Path.Combine(Path.GetTempPath(), "akml-builtin-verify-" + Guid.NewGuid());
        var manager = new ProfileManager(builtInDir, customDir);

        var list = manager.List();

        var khamis = Assert.Single(list, p => p.Name == "Khamis Style");
        Assert.True(khamis.IsBuiltIn);

        var collapsed = Assert.Single(list, p => p.Name == "Collapsed");
        Assert.True(collapsed.IsBuiltIn);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
    }
}

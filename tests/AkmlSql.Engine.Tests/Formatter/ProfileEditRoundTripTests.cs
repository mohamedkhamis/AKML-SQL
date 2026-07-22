using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Spec 033 (T017 / SC-006) — engine-level edit round-trip over a real imported Redgate style:
/// import → ProfileGet (raw) → merge one edited setting (mirroring the shell merger's output
/// shape) → ProfileSave → reload. Root-level unknown keys, the verbatim .source.json sidecar,
/// and the profile identity must all survive the edit.
/// </summary>
public class ProfileEditRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akml-033-rt-").FullName;
    private readonly string _customDir;
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public ProfileEditRoundTripTests()
    {
        _customDir = Path.Combine(_dir, "custom");
        _profiles = new ProfileManager(
            builtInProfilesPath: Path.Combine(_dir, "builtin"),
            customProfilesPath: _customDir);
        _handler = new FormatRequestHandler(_profiles);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    [Fact]
    public void Imported_style_edited_and_saved_keeps_identity_sidecar_and_unknown_root_keys()
    {
        // 1. Import the real Redgate fixture.
        var import = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = Encoding.UTF8.GetBytes(UserStyleJson),
        });
        Assert.True(import.Success);
        var name = import.ProfileName!;

        // Plant an unknown ROOT key in the stored profile (root ExtensionData is the
        // documented survival surface; keys nested inside groups are engine-normalized).
        var storedPath = Path.Combine(_customDir, name + ".akmlstyle");
        var planted = JsonNode.Parse(File.ReadAllText(storedPath))!.AsObject();
        planted["akmlFutureRootKey"] = "must-survive";
        File.WriteAllText(storedPath, planted.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var sidecarPath = Path.Combine(_customDir, name + ".source.json");
        Assert.True(File.Exists(sidecarPath));
        var sidecarBefore = File.ReadAllText(sidecarPath);

        // 2. ProfileGet returns the raw stored text (planted key included).
        var get = _handler.HandleProfileGet(new ProfileGetRequest { Name = name });
        Assert.True(get.Success);
        Assert.False(get.IsBuiltIn);
        Assert.Contains("akmlFutureRootKey", get.ProfileJson);

        // 3. Merge one edited setting into the raw JSON — the shell merger's output shape.
        var root = JsonNode.Parse(get.ProfileJson!)!.AsObject();
        if (root["casing"] is not JsonObject casing)
        {
            casing = new JsonObject();
            root["casing"] = casing;
        }
        casing["reservedKeywords"] = "lowercase";
        var merged = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // 4. Save through the normal pipe path.
        var save = _handler.HandleProfileSave(new ProfileSaveRequest { Name = name, ProfileJson = merged });
        Assert.True(save.Success, save.ErrorMessage);

        // 5. Reload: edit applied, identity kept, unknown root key survived, sidecar untouched.
        var reloaded = _profiles.Load(name);
        Assert.Equal(name, reloaded.Metadata.Name);
        Assert.Equal("lowercase", reloaded.Casing.ReservedKeywords);
        Assert.NotNull(reloaded.ExtensionData);
        Assert.True(reloaded.ExtensionData!.ContainsKey("akmlFutureRootKey"));

        var reloadedText = File.ReadAllText(storedPath);
        Assert.Contains("akmlFutureRootKey", reloadedText);
        Assert.Equal(sidecarBefore, File.ReadAllText(sidecarPath));

        // And a second ProfileGet reflects the saved edit (fresh raw text).
        var getAfter = _handler.HandleProfileGet(new ProfileGetRequest { Name = name });
        Assert.Contains("lowercase", getAfter.ProfileJson);
    }
}

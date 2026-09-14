using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// The ProfileReset IPC (37/137) and the flags ProfileGet reports alongside it.
/// <para>
/// The editor decides what to show — whether Reset is offered at all, and whether the style is the
/// shipped one or your edited version of it — entirely from these two responses. If they disagree
/// with what is on disk, the UI is confidently wrong about which of the two you are looking at,
/// which is the one thing a "reset" control must never be.
/// </para>
/// </summary>
public class ProfileResetHandlerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akml-profile-reset-").FullName;
    private readonly string _builtInDir;
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public ProfileResetHandlerTests()
    {
        _builtInDir = Path.Combine(_dir, "builtin");
        Directory.CreateDirectory(_builtInDir);
        _profiles = new ProfileManager(_builtInDir, Path.Combine(_dir, "custom"));
        _handler = new FormatRequestHandler(_profiles);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Kebab-case filename, Title-Case metadata name — the shape the shipped built-ins use.</summary>
    private void WriteBuiltIn(string fileStem, string displayName, string keywordCasing)
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = displayName;
        profile.Metadata.IsBuiltIn = true;
        profile.Casing.ReservedKeywords = keywordCasing;
        File.WriteAllText(
            Path.Combine(_builtInDir, fileStem + ".akmlstyle"),
            ProfileSerializer.Serialize(profile));
    }

    private void Edit(string name, string keywordCasing)
    {
        var profile = _profiles.Load(name);
        profile.Casing.ReservedKeywords = keywordCasing;
        _profiles.Save(profile);
    }

    [Fact]
    public void ProfileGet_OnAnUneditedBuiltIn_SaysShippedAndUnchanged()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        var response = _handler.HandleProfileGet(new ProfileGetRequest { Name = "Khamis Style" });

        Assert.True(response.Success);
        Assert.True(response.IsBuiltIn);
        Assert.True(response.HasBuiltIn);
        Assert.False(response.IsCustomizedBuiltIn);
    }

    [Fact]
    public void ProfileGet_OnAnEditedBuiltIn_StillSaysShipped()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        Edit("Khamis Style", "lowercase");

        var response = _handler.HandleProfileGet(new ProfileGetRequest { Name = "Khamis Style" });

        Assert.True(response.Success);

        // IsBuiltIn is false -- the file that resolved really is the custom one. HasBuiltIn is what
        // keeps the editor from offering Rename/Delete and from hiding Reset on a style that is
        // still, underneath, one of the shipped ones.
        Assert.False(response.IsBuiltIn);
        Assert.True(response.HasBuiltIn);
        Assert.True(response.IsCustomizedBuiltIn);
    }

    [Fact]
    public void ProfileGet_OnAStyleThatNeverShipped_SaysSo()
    {
        var mine = new FormattingProfile();
        mine.Metadata.Name = "My Style";
        _profiles.Save(mine);

        var response = _handler.HandleProfileGet(new ProfileGetRequest { Name = "My Style" });

        Assert.True(response.Success);
        Assert.False(response.IsBuiltIn);
        Assert.False(response.HasBuiltIn);
        Assert.False(response.IsCustomizedBuiltIn);
    }

    [Fact]
    public void Reset_ReturnsTheRestoredText_SoTheEditorNeedsNoSecondRoundTrip()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        var shipped = File.ReadAllText(Path.Combine(_builtInDir, "khamis-style.akmlstyle"));

        Edit("Khamis Style", "lowercase");

        var response = _handler.HandleProfileReset(new ProfileResetRequest { Name = "Khamis Style" });

        Assert.True(response.Success);
        Assert.True(response.ChangesDiscarded);

        // The text comes back read from disk AFTER the reset, so the editor rebinds from what is
        // now stored rather than from what the reset was expected to produce.
        Assert.Equal(shipped, response.ProfileJson);
    }

    [Fact]
    public void Reset_OnAnUneditedBuiltIn_SucceedsButReportsNothingDiscarded()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");

        var response = _handler.HandleProfileReset(new ProfileResetRequest { Name = "Khamis Style" });

        // The end state is what was asked for, so this is not a failure -- but the editor must not
        // tell someone it discarded changes that did not exist.
        Assert.True(response.Success);
        Assert.False(response.ChangesDiscarded);
    }

    [Fact]
    public void Reset_OnAStyleThatNeverShipped_FailsAndDoesNotDeleteIt()
    {
        var mine = new FormattingProfile();
        mine.Metadata.Name = "My Style";
        mine.Casing.ReservedKeywords = "lowercase";
        _profiles.Save(mine);

        var response = _handler.HandleProfileReset(new ProfileResetRequest { Name = "My Style" });

        Assert.False(response.Success);
        Assert.Contains("not a built-in", response.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // The failure must be inert. A "reset" that deleted a style with no original to return to
        // would destroy work under a word that does not imply it.
        Assert.Equal("lowercase", _profiles.Load("My Style").Casing.ReservedKeywords);
    }

    [Fact]
    public void ProfileList_MarksAnEditedBuiltIn()
    {
        WriteBuiltIn("khamis-style", "Khamis Style", "UPPERCASE");
        WriteBuiltIn("compact", "Compact", "lowercase");
        Edit("Khamis Style", "lowercase");

        var listed = _handler.HandleProfileList();

        var edited = listed.Profiles.Single(p => p.Name == "Khamis Style");
        Assert.False(edited.IsBuiltIn);
        Assert.True(edited.IsCustomizedBuiltIn);

        var untouched = listed.Profiles.Single(p => p.Name == "Compact");
        Assert.True(untouched.IsBuiltIn);
        Assert.False(untouched.IsCustomizedBuiltIn);
    }
}

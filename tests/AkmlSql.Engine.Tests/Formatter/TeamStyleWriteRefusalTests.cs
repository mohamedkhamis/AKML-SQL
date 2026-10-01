using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Spec 040 (T174, STY-10, contracts/ipc.md "ProfileInfo") — the engine refuses Save, Rename,
/// Delete and Reset on a read-only team style with the contract's text, lists team styles with
/// their source and read-only flag, and reports an unreachable team folder.
/// </summary>
public sealed class TeamStyleWriteRefusalTests : IDisposable
{
    private readonly string _root;
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly string _teamDir;
    private string _teamSetting;
    private readonly FormatRequestHandler _handler;

    private const string Refusal = "'Team A' is a team style and can't be changed here \u2014 copy it to edit.";

    public TeamStyleWriteRefusalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "akml_team_refusal_" + Guid.NewGuid().ToString("N"));
        _builtInDir = Path.Combine(_root, "builtin");
        _customDir = Path.Combine(_root, "custom");
        _teamDir = Path.Combine(_root, "team");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        Directory.CreateDirectory(_teamDir);
        _teamSetting = _teamDir;

        Write(_builtInDir, "default", "Default");
        Write(_teamDir, "team-a", "Team A");
        new DirectoryInfo(_teamDir).Attributes |= FileAttributes.ReadOnly;

        _handler = new FormatRequestHandler(new ProfileManager(_builtInDir, _customDir, () => _teamSetting));
    }

    public void Dispose()
    {
        try
        {
            new DirectoryInfo(_teamDir).Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }

    private static void Write(string dir, string stem, string name)
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = name;
        File.WriteAllText(Path.Combine(dir, stem + ".akmlstyle"), ProfileSerializer.Serialize(profile));
    }

    private string TeamFileText() => File.ReadAllText(Path.Combine(_teamDir, "team-a.akmlstyle"));

    [Fact]
    public void The_list_marks_the_team_style_read_only_and_its_source()
    {
        var list = _handler.HandleProfileList();

        var team = list.Profiles.Single(p => p.Name == "Team A");
        Assert.Equal("team", team.Source);
        Assert.True(team.IsReadOnly);
        Assert.False(team.IsBuiltIn);
        Assert.Equal("builtIn", list.Profiles.Single(p => p.Name == "Default").Source);
        Assert.False(list.TeamFolderUnavailable);
    }

    [Fact]
    public void Save_is_refused()
    {
        var before = TeamFileText();
        var profile = new FormattingProfile();
        profile.Metadata.Name = "Team A";
        profile.Casing.ReservedKeywords = "lowercase";

        var r = _handler.HandleProfileSave(new ProfileSaveRequest { ProfileJson = ProfileSerializer.Serialize(profile) });

        Assert.False(r.Success);
        Assert.Equal(Refusal, r.ErrorMessage);
        Assert.Equal(before, TeamFileText());
        Assert.Empty(Directory.GetFiles(_customDir));
    }

    [Fact]
    public void Rename_is_refused()
    {
        var r = _handler.HandleProfileRename(new ProfileRenameRequest { OldName = "Team A", NewName = "Team B" });

        Assert.False(r.Success);
        Assert.Equal(Refusal, r.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(_teamDir, "team-a.akmlstyle")));
    }

    [Fact]
    public void Delete_is_refused()
    {
        var r = _handler.HandleProfileDelete(new ProfileDeleteRequest { Name = "Team A" });

        Assert.False(r.Success);
        Assert.Equal(Refusal, r.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(_teamDir, "team-a.akmlstyle")));
    }

    [Fact]
    public void Reset_is_refused()
    {
        var r = _handler.HandleProfileReset(new ProfileResetRequest { Name = "Team A" });

        Assert.False(r.Success);
        Assert.Equal(Refusal, r.ErrorMessage);
    }

    [Fact]
    public void Copy_is_allowed_and_makes_the_users_own_style()
    {
        var r = _handler.HandleDuplicateProfile(new DuplicateProfileRequest { SourceName = "Team A", NewName = "Team A copy" });

        Assert.True(r.Success, r.ErrorMessage);
        var copy = _handler.HandleProfileList().Profiles.Single(p => p.Name == "Team A copy");
        Assert.Equal("user", copy.Source);
        Assert.False(copy.IsReadOnly);
    }

    [Fact]
    public void A_team_style_formats_like_any_other()
    {
        var r = _handler.HandleFormat(new FormatRequest { Text = "select 1", ProfileName = "Team A" });

        Assert.True(r.Success);
        Assert.Null(r.ProfileFallbackWarning);
    }

    [Fact]
    public void An_unreachable_team_folder_is_reported_and_the_other_styles_are_listed()
    {
        _teamSetting = $@"\\akml-no-such-host-{Guid.NewGuid():N}\share";

        var list = _handler.HandleProfileList();

        Assert.True(list.TeamFolderUnavailable);
        Assert.Contains(list.Profiles, p => p.Name == "Default");
        Assert.DoesNotContain(list.Profiles, p => p.Name == "Team A");
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 040 (T174, STY-10, FR-064, research R28) — a shared team style folder. Its styles are listed
/// as team styles, they are read-only unless the folder can be written to, names resolve
/// user &gt; team &gt; built-in, and a folder that can't be reached never holds up the other styles.
/// </summary>
public class TeamStyleFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly string _teamDir;

    public TeamStyleFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "akmlsql-team-styles-" + Guid.NewGuid().ToString("N"));
        _builtInDir = Path.Combine(_root, "builtin");
        _customDir = Path.Combine(_root, "custom");
        _teamDir = Path.Combine(_root, "team");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        Directory.CreateDirectory(_teamDir);
    }

    public void Dispose()
    {
        try
        {
            foreach (var dir in new[] { _teamDir, _customDir, _builtInDir })
                if (Directory.Exists(dir)) new DirectoryInfo(dir).Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private ProfileManager Manager(string? teamFolder) => new(_builtInDir, _customDir, () => teamFolder);

    private static void Write(string dir, string fileStem, string name, string keywordCasing = "UPPERCASE")
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = name;
        profile.Casing.ReservedKeywords = keywordCasing;
        File.WriteAllText(Path.Combine(dir, fileStem + ".akmlstyle"), ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void A_style_in_the_team_folder_is_listed_as_a_team_style()
    {
        Write(_builtInDir, "default", "Default");
        Write(_teamDir, "team-a", "Team A");
        Write(_customDir, "Mine", "Mine");

        var manager = Manager(_teamDir);
        var list = manager.List();

        Assert.Equal("team", list.Single(p => p.Name == "Team A").Source);
        Assert.Equal("user", list.Single(p => p.Name == "Mine").Source);
        Assert.Equal("builtIn", list.Single(p => p.Name == "Default").Source);
        Assert.False(list.Single(p => p.Name == "Team A").IsBuiltIn);
        Assert.False(manager.TeamFolderUnavailable);

        // The team style is a real style: it loads and formats like any other.
        Assert.Equal("Team A", manager.Load("Team A").Metadata.Name);
    }

    [Fact]
    public void A_sql_prompt_json_style_in_the_team_folder_is_a_team_style()
    {
        var path = Path.Combine(_teamDir, "house.json");
        File.WriteAllText(path, "{\"metadata\":{\"id\":\"h1\",\"name\":\"House Style\"},\"casing\":{\"reservedKeywords\":\"lowercase\"}}");
        File.WriteAllText(Path.Combine(_teamDir, "notes.json"), "{\"notes\":\"not a style\"}");
        var manager = Manager(_teamDir);

        var list = manager.List();
        var house = list.Single(p => p.Name == "House Style");
        Assert.Equal("team", house.Source);
        Assert.True(house.IsSqlPromptStyle);
        Assert.DoesNotContain(list, p => p.Name == "notes");
        Assert.NotNull(manager.Load("House Style").SqlPrompt);

        // A writable one is saved back as a SQL Prompt document, under its own file name.
        manager.Save(manager.Load("House Style"));
        Assert.True(AkmlSql.Formatting.SqlPrompt.SqlPromptStyleDocument.TryParse(File.ReadAllText(path), out var document, out _));
        Assert.Equal("House Style", document!.Name);
        Assert.Empty(Directory.GetFiles(_customDir));

        Assert.Equal("Our Style", manager.Rename("House Style", "Our Style"));
        Assert.True(File.Exists(Path.Combine(_teamDir, "Our Style.json")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_team_style_in_a_writable_folder_is_not_read_only()
    {
        Write(_teamDir, "team-a", "Team A");

        var style = Manager(_teamDir).List().Single(p => p.Name == "Team A");

        Assert.False(style.IsReadOnly);
    }

    [Fact]
    public void A_team_style_is_read_only_when_the_folder_is_marked_read_only()
    {
        Write(_teamDir, "team-a", "Team A");
        new DirectoryInfo(_teamDir).Attributes |= FileAttributes.ReadOnly;

        var manager = Manager(_teamDir);
        var style = manager.List().Single(p => p.Name == "Team A");

        Assert.True(style.IsReadOnly);
        Assert.True(manager.IsReadOnlyTeamStyle("Team A"));
    }

    [Fact]
    public void A_team_style_is_read_only_when_the_folder_denies_writes()
    {
        Write(_teamDir, "team-a", "Team A");
        var info = new DirectoryInfo(_teamDir);
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);

        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var style = Manager(_teamDir).List().Single(p => p.Name == "Team A");
            Assert.True(style.IsReadOnly);
        }
        finally
        {
            var restore = info.GetAccessControl();
            restore.RemoveAccessRule(deny);
            info.SetAccessControl(restore);
        }
    }

    [Fact]
    public void Built_in_and_user_styles_are_never_read_only()
    {
        Write(_builtInDir, "default", "Default");
        Write(_customDir, "Mine", "Mine");

        var list = Manager(null).List();

        Assert.All(list, p => Assert.False(p.IsReadOnly));
    }

    [Fact]
    public void Names_resolve_user_then_team_then_built_in()
    {
        Write(_builtInDir, "shared", "Shared", "UPPERCASE");
        Write(_teamDir, "shared", "Shared", "lowercase");

        var manager = Manager(_teamDir);

        // Team shadows built-in.
        Assert.Equal("lowercase", manager.Load("Shared").Casing.ReservedKeywords);
        Assert.Equal("team", manager.List().Single(p => p.Name == "Shared").Source);

        // User shadows team.
        Write(_customDir, "Shared", "Shared", "PascalCase");
        Assert.Equal("PascalCase", manager.Load("Shared").Casing.ReservedKeywords);
        var listed = manager.List().Where(p => p.Name == "Shared").ToList();
        Assert.Single(listed);
        Assert.Equal("user", listed[0].Source);
    }

    [Fact]
    public void A_team_style_found_by_its_metadata_name_resolves_ahead_of_a_built_in()
    {
        // Filenames differ from display names, the way shipped styles are stored.
        Write(_builtInDir, "khamis-style", "Khamis Style", "UPPERCASE");
        Write(_teamDir, "our-house-style", "Khamis Style", "lowercase");

        Assert.Equal("lowercase", Manager(_teamDir).Load("Khamis Style").Casing.ReservedKeywords);
    }

    [Fact]
    public void Removing_the_team_folder_setting_hides_the_team_styles()
    {
        Write(_teamDir, "team-a", "Team A");
        string? folder = _teamDir;
        var manager = new ProfileManager(_builtInDir, _customDir, () => folder);
        Assert.Contains(manager.List(), p => p.Name == "Team A");

        folder = "";

        Assert.DoesNotContain(manager.List(), p => p.Name == "Team A");
        Assert.False(manager.TeamFolderUnavailable);
        Assert.Throws<FileNotFoundException>(() => manager.Load("Team A"));
    }

    [Fact]
    public void A_newly_dropped_team_style_resolves_without_a_restart()
    {
        Write(_builtInDir, "khamis-style", "Khamis Style", "UPPERCASE");
        var manager = Manager(_teamDir);
        Assert.Equal("UPPERCASE", manager.Load("Khamis Style").Casing.ReservedKeywords);   // memoised: built-in

        Write(_teamDir, "house", "Khamis Style", "lowercase");
        Directory.SetLastWriteTimeUtc(_teamDir, DateTime.UtcNow.AddMinutes(1));   // coarse clocks

        Assert.Equal("lowercase", manager.Load("Khamis Style").Casing.ReservedKeywords);
    }

    [Fact]
    public void An_unreachable_team_folder_returns_the_other_styles_quickly_and_says_so()
    {
        Write(_builtInDir, "default", "Default");
        Write(_customDir, "Mine", "Mine");
        var unreachable = $@"\\akml-no-such-host-{Guid.NewGuid():N}\share\styles";
        var manager = Manager(unreachable);

        var sw = Stopwatch.StartNew();
        var list = manager.List();
        sw.Stop();

        // The 2 s budget plus scheduling slack — never the minutes an SMB timeout can take.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2.5), $"List took {sw.Elapsed.TotalMilliseconds:N0} ms");
        Assert.Contains(list, p => p.Name == "Default");
        Assert.Contains(list, p => p.Name == "Mine");
        Assert.True(manager.TeamFolderUnavailable);

        // Once known to be unreachable it is not probed again on every call (format requests
        // resolve styles too, and must never wait on the network).
        sw.Restart();
        _ = manager.List();
        Assert.Equal("Default", manager.Load("Default").Metadata.Name);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"Second pass took {sw.Elapsed.TotalMilliseconds:N0} ms");
        Assert.True(manager.TeamFolderUnavailable);
    }

    [Fact]
    public void A_team_folder_that_does_not_exist_is_unavailable()
    {
        Write(_customDir, "Mine", "Mine");
        var manager = Manager(Path.Combine(_root, "missing"));

        var list = manager.List();

        Assert.Contains(list, p => p.Name == "Mine");
        Assert.True(manager.TeamFolderUnavailable);
    }

    [Fact]
    public void A_relative_team_folder_is_ignored_rather_than_resolved_against_the_engine_folder()
    {
        Write(_teamDir, "team-a", "Team A");
        var manager = Manager("team");

        Assert.DoesNotContain(manager.List(), p => p.Name == "Team A");
        Assert.True(manager.TeamFolderUnavailable);
    }

    [Fact]
    public void A_throwing_provider_is_treated_as_no_team_folder()
    {
        Write(_customDir, "Mine", "Mine");
        var manager = new ProfileManager(_builtInDir, _customDir, () => throw new InvalidOperationException("settings broke"));

        Assert.Contains(manager.List(), p => p.Name == "Mine");
        Assert.False(manager.TeamFolderUnavailable);
    }

    [Fact]
    public void Writable_team_styles_are_edited_in_place_and_read_only_ones_are_refused()
    {
        Write(_teamDir, "team-a", "Team A", "UPPERCASE");
        var manager = Manager(_teamDir);

        // Writable: the shared file itself changes; no personal copy appears.
        var profile = manager.Load("Team A");
        profile.Casing.ReservedKeywords = "lowercase";
        manager.Save(profile);
        Assert.Equal("lowercase", ProfileSerializer.Deserialize(File.ReadAllText(Path.Combine(_teamDir, "team-a.akmlstyle"))).Casing.ReservedKeywords);
        Assert.Empty(Directory.GetFiles(_customDir));

        // Read-only: every write is refused with the contract text, and nothing changes.
        new DirectoryInfo(_teamDir).Attributes |= FileAttributes.ReadOnly;
        var readOnly = Manager(_teamDir);
        var expected = "'Team A' is a team style and can't be changed here \u2014 copy it to edit.";

        var again = readOnly.Load("Team A");
        again.Casing.ReservedKeywords = "PascalCase";
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => readOnly.Save(again)).Message);
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => readOnly.Delete("Team A")).Message);
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => readOnly.Rename("Team A", "Team B")).Message);
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => readOnly.ResetToBuiltIn("Team A")).Message);
        Assert.Equal("lowercase", readOnly.Load("Team A").Casing.ReservedKeywords);
        Assert.Empty(Directory.GetFiles(_customDir));

        // Copy is always allowed: it makes the user's own style.
        readOnly.Duplicate("Team A", "Team A copy");
        Assert.Equal("user", readOnly.List().Single(p => p.Name == "Team A copy").Source);
    }

    [Fact]
    public void Writable_team_styles_can_be_renamed_and_deleted_in_the_team_folder()
    {
        Write(_teamDir, "team-a", "Team A");
        var manager = Manager(_teamDir);

        Assert.Equal("Team B", manager.Rename("Team A", "Team B"));
        Assert.Equal("team", manager.List().Single(p => p.Name == "Team B").Source);
        Assert.DoesNotContain(manager.List(), p => p.Name == "Team A");
        Assert.Empty(Directory.GetFiles(_customDir));

        Assert.True(manager.Delete("Team B"));
        Assert.DoesNotContain(manager.List(), p => p.Name == "Team B");
        Assert.Empty(Directory.GetFiles(_teamDir, "*.akmlstyle"));
    }
}

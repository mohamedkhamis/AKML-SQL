using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 040 (T176, STY-10, STY-11) — the formatting keys added for team styles and Format SQL
/// actions (contracts/ipc.md "Formatting"). Every key is appended, and a payload written before
/// it existed still loads, with the new field at its "as today" value.
/// </summary>
public class FormattingContractTests
{
    // ---------------------------------------------------------------- FormatSqlActionsDto

    [Fact]
    public void Format_sql_actions_round_trip_on_keys_0_to_5()
    {
        var dto = new FormatSqlActionsDto
        {
            ApplyLayout = false,
            ApplyCasing = true,
            Semicolons = 1,
            SquareBrackets = 2,
            ExpandWildcards = true,
            QualifyObjectNames = false,
        };

        var bytes = MessagePackSerializer.Serialize(dto);
        var back = MessagePackSerializer.Deserialize<FormatSqlActionsDto>(bytes);

        Assert.False(back.ApplyLayout);
        Assert.True(back.ApplyCasing);
        Assert.Equal(1, back.Semicolons);
        Assert.Equal(2, back.SquareBrackets);
        Assert.True(back.ExpandWildcards);
        Assert.False(back.QualifyObjectNames);

        // Positional layout pins the key numbers, not just the round trip.
        var array = MessagePackSerializer.Deserialize<object[]>(bytes);
        Assert.Equal(6, array.Length);
        Assert.Equal(false, array[0]);
        Assert.Equal(true, array[1]);
        Assert.Equal(1, System.Convert.ToInt32(array[2]));
        Assert.Equal(2, System.Convert.ToInt32(array[3]));
        Assert.Equal(true, array[4]);
        Assert.Equal(false, array[5]);
    }

    [Fact]
    public void A_new_actions_dto_means_todays_formatting()
    {
        var dto = new FormatSqlActionsDto();

        Assert.True(dto.ApplyLayout);
        Assert.True(dto.ApplyCasing);
        Assert.Equal(0, dto.Semicolons);
        Assert.Equal(0, dto.SquareBrackets);
        Assert.False(dto.ExpandWildcards);
        Assert.False(dto.QualifyObjectNames);
    }

    // ---------------------------------------------------------------- FormatRequest key 5

    [Fact]
    public void Format_request_carries_actions_as_key_5()
    {
        var bytes = MessagePackSerializer.Serialize(new FormatRequest
        {
            SessionId = "s1",
            Text = "select 1",
            ProfileName = "Khamis Style",
            Actions = new FormatSqlActionsDto { Semicolons = 1, ExpandWildcards = true },
        });

        var back = MessagePackSerializer.Deserialize<FormatRequest>(bytes);
        Assert.Equal("s1", back.SessionId);
        Assert.NotNull(back.Actions);
        Assert.Equal(1, back.Actions!.Semicolons);
        Assert.True(back.Actions.ExpandWildcards);

        var array = MessagePackSerializer.Deserialize<object[]>(bytes);
        Assert.Equal(6, array.Length);
        Assert.NotNull(array[5]);
    }

    [Fact]
    public void A_format_request_without_key_5_has_no_actions()
    {
        // Keys 0-4 as an older shell (and the CLI, bulk and web paths) write them.
        var legacy = MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", "Default", null, null });

        var back = MessagePackSerializer.Deserialize<FormatRequest>(legacy);

        Assert.Equal("select 1", back.Text);
        Assert.Equal("Default", back.ProfileName);
        Assert.Null(back.Actions);
    }

    [Fact]
    public void A_format_request_with_no_actions_serializes_key_5_as_nil()
    {
        var bytes = MessagePackSerializer.Serialize(new FormatRequest { Text = "select 1" });
        var back = MessagePackSerializer.Deserialize<FormatRequest>(bytes);
        Assert.Null(back.Actions);
    }

    // ---------------------------------------------------------------- FormatSelectionRequest key 5

    [Fact]
    public void Format_selection_request_carries_actions_as_key_5()
    {
        var bytes = MessagePackSerializer.Serialize(new FormatSelectionRequest
        {
            SessionId = "s1",
            Text = "select 1; select 2",
            SelectionStart = 0,
            SelectionEnd = 9,
            ProfileName = "Default",
            Actions = new FormatSqlActionsDto { ApplyCasing = false, SquareBrackets = 1, QualifyObjectNames = true },
        });

        var back = MessagePackSerializer.Deserialize<FormatSelectionRequest>(bytes);
        Assert.Equal(9, back.SelectionEnd);
        Assert.NotNull(back.Actions);
        Assert.False(back.Actions!.ApplyCasing);
        Assert.Equal(1, back.Actions.SquareBrackets);
        Assert.True(back.Actions.QualifyObjectNames);

        var array = MessagePackSerializer.Deserialize<object[]>(bytes);
        Assert.Equal(6, array.Length);
    }

    [Fact]
    public void A_format_selection_request_without_key_5_has_no_actions()
    {
        var legacy = MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", 0, 8, "Default" });

        var back = MessagePackSerializer.Deserialize<FormatSelectionRequest>(legacy);

        Assert.Equal(8, back.SelectionEnd);
        Assert.Equal("Default", back.ProfileName);
        Assert.Null(back.Actions);
    }

    // ---------------------------------------------------------------- ProfileInfo keys 9-10

    [Fact]
    public void Profile_info_carries_source_and_read_only_as_keys_9_and_10()
    {
        var bytes = MessagePackSerializer.Serialize(new ProfileInfo
        {
            Name = "Team A",
            Source = "team",
            IsReadOnly = true,
        });

        var back = MessagePackSerializer.Deserialize<ProfileInfo>(bytes);
        Assert.Equal("Team A", back.Name);
        Assert.Equal("team", back.Source);
        Assert.True(back.IsReadOnly);

        var array = MessagePackSerializer.Deserialize<object[]>(bytes);
        Assert.Equal(11, array.Length);
        Assert.Equal("team", array[9]);
        Assert.Equal(true, array[10]);
    }

    [Fact]
    public void An_older_profile_info_without_keys_9_and_10_loads_as_unknown_source_and_writable()
    {
        // Keys 0-8 as an older engine writes them.
        var legacy = MessagePackSerializer.Serialize(new object?[]
        {
            "Khamis Style", "desc", "author", true, false, null, "2026-01-01T00:00:00Z", false, false,
        });

        var back = MessagePackSerializer.Deserialize<ProfileInfo>(legacy);

        Assert.Equal("Khamis Style", back.Name);
        Assert.True(back.IsBuiltIn);
        Assert.Null(back.Source);
        Assert.False(back.IsReadOnly);
    }

    // ---------------------------------------------------------------- ProfileListResponse key 1

    [Fact]
    public void Profile_list_reports_an_unreachable_team_folder_as_key_1()
    {
        var bytes = MessagePackSerializer.Serialize(new ProfileListResponse
        {
            Profiles = [new ProfileInfo { Name = "Mine", Source = "user" }],
            TeamFolderUnavailable = true,
        });

        var back = MessagePackSerializer.Deserialize<ProfileListResponse>(bytes);
        Assert.True(back.TeamFolderUnavailable);
        Assert.Single(back.Profiles);
        Assert.Equal("user", back.Profiles[0].Source);
    }

    [Fact]
    public void An_older_profile_list_without_key_1_reports_the_team_folder_as_available()
    {
        var legacy = MessagePackSerializer.Serialize(new ProfileListLegacyShape
        {
            Profiles = [new ProfileInfo { Name = "Mine" }],
        });

        var back = MessagePackSerializer.Deserialize<ProfileListResponse>(legacy);

        Assert.Single(back.Profiles);
        Assert.False(back.TeamFolderUnavailable);
    }

    /// <summary>Mirror of ProfileListResponse as it existed before key 1.</summary>
    [MessagePackObject]
    public class ProfileListLegacyShape
    {
        [Key(0)] public ProfileInfo[] Profiles { get; set; } = [];
    }
}

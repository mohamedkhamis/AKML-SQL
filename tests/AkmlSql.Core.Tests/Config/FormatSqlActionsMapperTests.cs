using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 040 (T179, STY-11, research R29) — Options › Format › Styles "When you run Format SQL,
/// AKML SQL will:" maps onto the wire DTO field by field, and its defaults are what the shipped
/// styles already do, so turning the feature on changes nobody's output.
/// </summary>
public class FormatSqlActionsMapperTests
{
    [Theory]
    [InlineData("leave", 0)]
    [InlineData("insert", 1)]
    [InlineData("remove", 2)]
    [InlineData("Insert", 1)]     // hand-edited config: case does not matter
    [InlineData("REMOVE", 2)]
    [InlineData("style", 3)]
    [InlineData("", 3)]           // anything unknown: as the style says (the default)
    [InlineData("sometimes", 3)]
    public void Semicolons_map_to_0_leave_1_insert_2_remove(string value, int expected)
    {
        var dto = FormatSqlActionsMapper.ToDto(new FormatSqlActions { Semicolons = value });
        Assert.Equal(expected, dto.Semicolons);
    }

    [Theory]
    [InlineData("leave", 0)]
    [InlineData("add", 1)]
    [InlineData("remove", 2)]
    [InlineData("Add", 1)]
    [InlineData("style", 3)]
    [InlineData("", 3)]
    [InlineData("insert", 3)]     // "insert" is a semicolons word, not a brackets one
    public void Square_brackets_map_to_0_leave_1_add_2_remove(string value, int expected)
    {
        var dto = FormatSqlActionsMapper.ToDto(new FormatSqlActions { SquareBrackets = value });
        Assert.Equal(expected, dto.SquareBrackets);
    }

    [Fact]
    public void A_null_or_default_semicolons_or_brackets_value_is_as_the_style_says()
    {
        // A style that inserts semicolons or adds brackets keeps doing so on Format SQL; an explicit
        // "leave" used to be sent for everyone and silently turned those off.
        var fromNull = FormatSqlActionsMapper.ToDto(new FormatSqlActions { Semicolons = null!, SquareBrackets = null! });
        var fromDefaults = FormatSqlActionsMapper.ToDto(new FormatSqlActions());
        Assert.Equal(FormatSqlActionsDto.UseStyle, fromNull.Semicolons);
        Assert.Equal(FormatSqlActionsDto.UseStyle, fromNull.SquareBrackets);
        Assert.Equal(FormatSqlActionsDto.UseStyle, fromDefaults.Semicolons);
        Assert.Equal(FormatSqlActionsDto.UseStyle, fromDefaults.SquareBrackets);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Each_switch_maps_to_its_own_field(bool on)
    {
        // Each switch alone, so a mapper that crossed two fields would fail.
        Assert.Equal(on, FormatSqlActionsMapper.ToDto(new FormatSqlActions { ApplyLayout = on }).ApplyLayout);
        Assert.Equal(on, FormatSqlActionsMapper.ToDto(new FormatSqlActions { ApplyCasing = on }).ApplyCasing);
        Assert.Equal(on, FormatSqlActionsMapper.ToDto(new FormatSqlActions { ExpandWildcards = on }).ExpandWildcards);
        Assert.Equal(on, FormatSqlActionsMapper.ToDto(new FormatSqlActions { QualifyObjectNames = on }).QualifyObjectNames);

        var mixed = FormatSqlActionsMapper.ToDto(new FormatSqlActions
        {
            ApplyLayout = on,
            ApplyCasing = !on,
            ExpandWildcards = on,
            QualifyObjectNames = !on,
        });
        Assert.Equal(on, mixed.ApplyLayout);
        Assert.Equal(!on, mixed.ApplyCasing);
        Assert.Equal(on, mixed.ExpandWildcards);
        Assert.Equal(!on, mixed.QualifyObjectNames);
    }

    [Fact]
    public void Null_settings_map_to_the_defaults()
    {
        var fromNull = FormatSqlActionsMapper.ToDto(null);
        var fromDefaults = FormatSqlActionsMapper.ToDto(new FormatSqlActions());

        Assert.Equal(fromDefaults.ApplyLayout, fromNull.ApplyLayout);
        Assert.Equal(fromDefaults.ApplyCasing, fromNull.ApplyCasing);
        Assert.Equal(fromDefaults.Semicolons, fromNull.Semicolons);
        Assert.Equal(fromDefaults.SquareBrackets, fromNull.SquareBrackets);
        Assert.Equal(fromDefaults.ExpandWildcards, fromNull.ExpandWildcards);
        Assert.Equal(fromDefaults.QualifyObjectNames, fromNull.QualifyObjectNames);
    }

    [Fact]
    public void The_defaults_are_the_common_format_actions_of_the_built_in_styles()
    {
        var styles = BuiltInFormatActions();
        Assert.NotEmpty(styles);

        // "Common" = the value most built-in styles carry. (Minimalist alone says applyLayout
        // false, but the pipeline never read that flag, so every style has always been laid out.)
        bool Common(string field) => styles
            .GroupBy(s => s.TryGetProperty(field, out var v) && v.GetBoolean())
            .OrderByDescending(g => g.Count())
            .First().Key;

        var dto = FormatSqlActionsMapper.ToDto(new FormatSqlActions());

        Assert.Equal(Common("applyLayout"), dto.ApplyLayout);
        Assert.Equal(Common("applyCasing"), dto.ApplyCasing);
        Assert.Equal(Common("expandWildcards"), dto.ExpandWildcards);
        Assert.Equal(Common("qualifyObjectNames"), dto.QualifyObjectNames);

        // Semicolons and brackets are left to each style's own setting, whatever it is.
        Assert.Equal(FormatSqlActionsDto.UseStyle, dto.Semicolons);
        Assert.Equal(FormatSqlActionsDto.UseStyle, dto.SquareBrackets);
    }

    [Fact]
    public void The_defaults_are_written_with_the_documented_words()
    {
        var defaults = new FormatSqlActions();
        Assert.Equal("style", defaults.Semicolons);
        Assert.Equal("style", defaults.SquareBrackets);
        Assert.True(defaults.ApplyLayout);
        Assert.True(defaults.ApplyCasing);
        Assert.False(defaults.ExpandWildcards);
        Assert.False(defaults.QualifyObjectNames);
    }

    [Fact]
    public void Formatter_settings_carry_format_sql_actions_and_an_empty_team_folder_by_default()
    {
        var formatter = new FormatterSettings();
        Assert.NotNull(formatter.FormatSqlActions);
        Assert.Equal(string.Empty, formatter.TeamStyleFolder);
    }

    [Fact]
    public void Format_sql_actions_survive_a_config_round_trip()
    {
        var settings = new AppSettings();
        settings.Formatter.TeamStyleFolder = @"\\server\share\styles";
        settings.Formatter.FormatSqlActions.Semicolons = "insert";
        settings.Formatter.FormatSqlActions.SquareBrackets = "remove";
        settings.Formatter.FormatSqlActions.ExpandWildcards = true;

        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"teamStyleFolder\"", json);
        Assert.Contains("\"formatSqlActions\"", json);

        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(@"\\server\share\styles", back.Formatter.TeamStyleFolder);
        Assert.Equal("insert", back.Formatter.FormatSqlActions.Semicolons);
        Assert.Equal("remove", back.Formatter.FormatSqlActions.SquareBrackets);
        Assert.True(back.Formatter.FormatSqlActions.ExpandWildcards);
    }

    [Fact]
    public void An_older_config_without_the_new_keys_loads_the_defaults()
    {
        var back = JsonSerializer.Deserialize<AppSettings>("{\"formatter\":{\"activeProfile\":\"Default\"}}")!;

        Assert.Equal("Default", back.Formatter.ActiveProfile);
        Assert.Equal(string.Empty, back.Formatter.TeamStyleFolder);
        Assert.NotNull(back.Formatter.FormatSqlActions);
        Assert.Equal("style", back.Formatter.FormatSqlActions.Semicolons);
    }

    private static List<JsonElement> BuiltInFormatActions()
    {
        var dir = Path.Combine(FindRepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn");
        var result = new List<JsonElement>();
        foreach (var file in Directory.GetFiles(dir, "*.akmlstyle"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("formatActions", out var actions))
                result.Add(actions.Clone());
        }
        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
    }
}

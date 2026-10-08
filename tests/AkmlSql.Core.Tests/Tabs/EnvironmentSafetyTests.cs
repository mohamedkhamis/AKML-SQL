using System;
using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs;

/// <summary>
/// Spec 040 (OPT-08) — Safety keys on the environment name, which Edit environments can change:
/// a renamed production environment must stay production and keep its severity.
/// </summary>
public sealed class EnvironmentSafetyTests
{
    private static Dictionary<string, string> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["PRODUCTION"] = "TypeServerName",
        ["STAGING"] = "SimpleConfirm",
        ["DEV"] = "SimpleConfirm",
    };

    [Theory]
    [InlineData("PRODUCTION", true)]
    [InlineData("Prod DB", true)]
    [InlineData("STAGING", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Production_by_name(string? label, bool expected)
        => Assert.Equal(expected, EnvironmentSafety.IsProduction(label, Defaults()));

    [Fact]
    public void An_environment_that_asks_for_the_server_name_is_production_whatever_it_is_called()
    {
        var severity = Defaults();
        severity["Live"] = "TypeServerName";

        Assert.True(EnvironmentSafety.IsProduction("Live", severity));
        Assert.True(EnvironmentSafety.IsProduction("live", severity));
        Assert.False(EnvironmentSafety.IsProduction("Live", null));
    }

    [Fact]
    public void A_rename_moves_the_severity_to_the_new_name()
    {
        var severity = Defaults();

        var moved = EnvironmentSafety.FollowRenames(severity, new Dictionary<string, string> { ["PRODUCTION"] = "Live", ["DEV"] = "DEV" });

        Assert.Equal(1, moved);
        Assert.Equal("TypeServerName", severity["Live"]);
        Assert.False(severity.ContainsKey("PRODUCTION"));
        Assert.Equal("SimpleConfirm", severity["DEV"]);
    }

    [Fact]
    public void Swapped_names_swap_their_severities()
    {
        var severity = Defaults();

        EnvironmentSafety.FollowRenames(severity, new Dictionary<string, string> { ["PRODUCTION"] = "STAGING", ["STAGING"] = "PRODUCTION" });

        Assert.Equal("SimpleConfirm", severity["PRODUCTION"]);
        Assert.Equal("TypeServerName", severity["STAGING"]);
    }

    [Fact]
    public void A_stale_severity_under_the_new_name_is_replaced()
    {
        // Names are unique: a "Live" entry left from a deleted environment is not this one's.
        var severity = Defaults();
        severity["Live"] = "Disabled";

        EnvironmentSafety.FollowRenames(severity, new Dictionary<string, string> { ["PRODUCTION"] = "Live" });

        Assert.Equal("TypeServerName", severity["Live"]);
        Assert.False(severity.ContainsKey("PRODUCTION"));
    }
}

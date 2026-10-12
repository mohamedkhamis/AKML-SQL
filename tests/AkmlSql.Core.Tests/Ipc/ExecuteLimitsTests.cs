using System;
using AkmlSql.Core.Ipc.Messages;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// PR #255 — the time limits one execute runs under, which the engine enforces and the web waits on: the
/// command timeout per command run, clamped as the engine clamps it, and the whole script at most an hour.
/// </summary>
public class ExecuteLimitsTests
{
    [Theory]
    [InlineData(0, ExecuteLimits.CommandTimeoutDefaultSeconds)]
    [InlineData(-5, ExecuteLimits.CommandTimeoutDefaultSeconds)]
    [InlineData(45, 45)]
    [InlineData(5_000, ExecuteLimits.CommandTimeoutCeilingSeconds)]
    public void The_command_timeout_is_defaulted_and_clamped(int requested, int expected)
    {
        Assert.Equal(expected, ExecuteLimits.CommandTimeoutSeconds(requested));
    }

    [Fact]
    public void The_script_limit_is_the_timeout_for_each_run()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), ExecuteLimits.ScriptTimeLimit(30, 3));
        Assert.Equal(TimeSpan.FromSeconds(30), ExecuteLimits.ScriptTimeLimit(30, 0));
    }

    [Fact]
    public void The_script_limit_never_exceeds_the_ceiling()
    {
        // WAITFOR DELAY '00:00:20' GO 10000 would otherwise hold the engine connection for days.
        Assert.Equal(TimeSpan.FromSeconds(ExecuteLimits.ScriptTimeLimitCeilingSeconds), ExecuteLimits.ScriptTimeLimit(20, 10_000));
        Assert.Equal(TimeSpan.FromSeconds(ExecuteLimits.ScriptTimeLimitCeilingSeconds), ExecuteLimits.ScriptTimeLimit(600, long.MaxValue));
    }
}

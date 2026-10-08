using System;
using System.Collections.Generic;
using AkmlSql.Core.Models.Connections;
using Xunit;

namespace AkmlSql.Core.Tests.Connections;

/// <summary>
/// SQL Server client aliases (the ConnectTo registry values SQL Server Configuration Manager
/// writes) resolve to the server they point at; direct names, IPs and explicit protocols are
/// left alone.
/// </summary>
public class SqlServerAliasesTests
{
    private static readonly Dictionary<string, string> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ServerDemo"] = "DBMSSOCN,192.168.4.5,1433",
        ["ReportsDb"] = @"DBMSSOCN,sql02\REPORTS",
        ["PipeAlias"] = @"DBNMPNTW,\\sql03\pipe\sql\query",
        ["PipeShort"] = "DBNMPNTW,sql03",
        ["LocalMem"] = "DBMSLPCN,(local)",
        ["Broken"] = "VIA,somewhere,1433",
    };

    private static string? Lookup(string name) => Registry.TryGetValue(name, out var v) ? v : null;

    [Theory]
    [InlineData("ServerDemo", "tcp:192.168.4.5,1433")]
    [InlineData("serverdemo", "tcp:192.168.4.5,1433")]
    [InlineData(" ServerDemo ", "tcp:192.168.4.5,1433")]
    [InlineData("ReportsDb", @"tcp:sql02\REPORTS")]
    [InlineData("PipeAlias", @"np:\\sql03\pipe\sql\query")]
    [InlineData("PipeShort", @"np:\\sql03\pipe\sql\query")]
    [InlineData("LocalMem", "lpc:(local)")]
    public void An_alias_resolves_to_its_target(string server, string expected)
        => Assert.Equal(expected, SqlServerAliases.Resolve(server, Lookup));

    [Theory]
    [InlineData("192.168.4.5")]
    [InlineData("sql01")]
    [InlineData(@"sql01\INST")]
    [InlineData("(local)")]
    [InlineData("tcp:ServerDemo")]       // an explicit protocol is never an alias
    [InlineData("ServerDemo,1433")]      // nor is a name with a port
    [InlineData("Broken")]               // an unknown protocol leaves the name alone
    [InlineData("")]
    public void Anything_else_is_left_as_typed(string server)
        => Assert.Equal(server, SqlServerAliases.Resolve(server, Lookup));

    [Fact]
    public void A_failing_lookup_leaves_the_name_as_typed()
        => Assert.Equal("ServerDemo", SqlServerAliases.Resolve("ServerDemo", _ => throw new UnauthorizedAccessException()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DBMSSOCN")]
    [InlineData("DBMSSOCN,")]
    public void Malformed_values_have_no_target(string? value)
        => Assert.Null(SqlServerAliases.ParseTarget(value));

    [Fact]
    public void IsAlias_tells_a_resolved_name_from_a_direct_one()
    {
        Assert.True(SqlServerAliases.IsAlias("ServerDemo", "tcp:192.168.4.5,1433"));
        Assert.False(SqlServerAliases.IsAlias("sql01", "sql01"));
        Assert.False(SqlServerAliases.IsAlias(null, "x"));
    }
}

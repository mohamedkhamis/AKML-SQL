using System;
using System.Data.Common;
using AkmlSql.Core.Connections;
using Xunit;

namespace AkmlSql.Core.Tests.Connections;

/// <summary>
/// Spec 042 (FR-002, FR-100, FR-103; contracts/ipc.md "Connection strings"): the one place that spells a PostgreSQL
/// connection string, and the one place that describes one for a log without its password.
/// </summary>
public class PgConnectionStringTests
{
    private static DbConnectionStringBuilder Parse(string connectionString) => new() { ConnectionString = connectionString };

    [Fact]
    public void The_string_carries_the_keys_the_contract_names()
    {
        var text = PgConnectionString.Build("db.example.com", 5433, "northwind", "alice", "s3cret", "verify-full", @"C:\certs\root.crt");
        var b = Parse(text);

        Assert.Equal("db.example.com", b["Host"]);
        Assert.Equal("5433", b["Port"]);
        Assert.Equal("northwind", b["Database"]);
        Assert.Equal("alice", b["Username"]);
        Assert.Equal("s3cret", b["Password"]);
        Assert.Equal("VerifyFull", b["SSL Mode"]);
        Assert.Equal(@"C:\certs\root.crt", b["Root Certificate"]);
        Assert.Equal("AKML SQL", b["Application Name"]);
        Assert.True(b.ContainsKey("Timeout"));
    }

    [Theory]
    [InlineData("disable", "Disable")]
    [InlineData("prefer", "Prefer")]
    [InlineData("require", "Require")]
    [InlineData("verify-ca", "VerifyCA")]
    [InlineData("verify-full", "VerifyFull")]
    [InlineData("VERIFY-CA", "VerifyCA")]
    [InlineData(null, "Prefer")]
    [InlineData("", "Prefer")]
    public void TLS_modes_map_to_the_driver_names_and_default_to_prefer(string? mode, string expected)
    {
        Assert.Equal(expected, Parse(PgConnectionString.Build("h", 5432, "d", "u", "p", mode, null))["SSL Mode"]);
    }

    [Fact]
    public void An_unknown_TLS_mode_is_not_silently_weakened()
    {
        Assert.Throws<ArgumentException>(() => PgConnectionString.Build("h", 5432, "d", "u", "p", "allow", null));
    }

    [Fact]
    public void The_port_defaults_to_5432_and_the_certificate_is_omitted_when_empty()
    {
        var b = Parse(PgConnectionString.Build("h", null, "d", "u", "p", null, ""));

        Assert.Equal("5432", b["Port"]);
        Assert.False(b.ContainsKey("Root Certificate"));
    }

    [Fact]
    public void A_missing_password_is_left_out_so_the_engine_can_supply_a_remembered_one()
    {
        var b = Parse(PgConnectionString.Build("h", 5432, "d", "u", null, null, null));

        Assert.False(b.ContainsKey("Password"));
    }

    [Fact]
    public void Values_with_connection_string_metacharacters_are_quoted_not_interpreted()
    {
        var text = PgConnectionString.Build("h", 5432, "d", "u", "p;Host=evil", null, null);
        var b = Parse(text);

        Assert.Equal("p;Host=evil", b["Password"]);
        Assert.Equal("h", b["Host"]);
    }

    [Fact]
    public void Describe_names_the_target_and_never_the_password()
    {
        var text = PgConnectionString.Build("db.example.com", 5433, "northwind", "alice", "s3cret-value", "require", null);

        var description = PgConnectionString.Describe(text);

        Assert.Contains("db.example.com:5433", description);
        Assert.Contains("northwind", description);
        Assert.Contains("alice", description);
        Assert.DoesNotContain("s3cret-value", description);
        Assert.DoesNotContain("Password", description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string at all ===")]
    public void Describe_copes_with_garbage(string? text)
    {
        var description = PgConnectionString.Describe(text);

        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public void ServerKey_is_lower_case_host_and_port_and_the_credential_store_key()
    {
        Assert.Equal("pg://db.example.com:5433", PgConnectionString.ServerKey("DB.Example.COM", 5433));
        Assert.Equal("pg://localhost:5432", PgConnectionString.ServerKey("localhost", 0));
        Assert.Equal("pg://localhost:5432", PgConnectionString.ServerKey(" LocalHost ", null));
    }
}

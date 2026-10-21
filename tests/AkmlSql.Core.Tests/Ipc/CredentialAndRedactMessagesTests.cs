using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>Spec 042 (contracts/ipc.md): the new credential and redaction messages and the dialect error ids.</summary>
public class CredentialAndRedactMessagesTests
{
    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void Message_type_numbers_continue_after_ExecuteParseResult()
    {
        Assert.Equal(221, MessageTypes.CredentialRequest);
        Assert.Equal(222, MessageTypes.CredentialResponse);
        Assert.Equal(223, MessageTypes.RedactRequest);
        Assert.Equal(224, MessageTypes.RedactResponse);
    }

    [Fact]
    public void CredentialRequest_round_trips_and_never_has_a_password_field()
    {
        var back = RoundTrip(new CredentialRequest
        {
            Action = CredentialAction.Forget,
            Kind = (int)DatabaseKind.PostgreSql,
            Server = "db.example.com",
            Port = 5433,
            Login = "alice",
        });

        Assert.Equal(CredentialAction.Forget, back.Action);
        Assert.Equal("db.example.com", back.Server);
        Assert.Equal(5433, back.Port);
        Assert.Equal("alice", back.Login);

        var names = typeof(CredentialRequest).GetProperties().Select(p => p.Name).Concat(
            typeof(CredentialResponse).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(names, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CredentialResponse_round_trips()
    {
        var back = RoundTrip(new CredentialResponse { Ok = true, Has = true, Error = null });
        Assert.True(back.Ok);
        Assert.True(back.Has);
        Assert.Null(back.Error);
    }

    [Fact]
    public void RedactMessages_round_trip()
    {
        var request = RoundTrip(new RedactRequest { Dialect = DatabaseKindNames.PostgreSql, Text = "select 'x'" });
        Assert.Equal("postgresql", request.Dialect);
        Assert.Equal("select 'x'", request.Text);

        var response = RoundTrip(new RedactResponse { Ok = false, Text = "", Error = "unterminated string" });
        Assert.False(response.Ok);
        Assert.Equal("unterminated string", response.Error);
        Assert.Empty(response.Placeholders);
    }

    [Fact]
    public void RedactResponse_carries_the_placeholders_at_key_3_and_the_first_keys_do_not_move()
    {
        // Spec 042 (T111, FR-081): the client puts the literals back into the answer, so it needs the map; the keys 0 to 2
        // of the contract stay where they were.
        var response = RoundTrip(new RedactResponse
        {
            Ok = true,
            Text = "select '__STR_0__'",
            Placeholders = new Dictionary<string, string> { ["'__STR_0__'"] = "'x'" },
        });

        Assert.Equal("'x'", response.Placeholders["'__STR_0__'"]);

        var keys = typeof(RedactResponse).GetProperties()
            .ToDictionary(p => p.Name, p => ((KeyAttribute)p.GetCustomAttributes(typeof(KeyAttribute), false).Single()).IntKey);
        Assert.Equal(0, keys[nameof(RedactResponse.Ok)]);
        Assert.Equal(1, keys[nameof(RedactResponse.Text)]);
        Assert.Equal(2, keys[nameof(RedactResponse.Error)]);
        Assert.Equal(3, keys[nameof(RedactResponse.Placeholders)]);
    }

    [Fact]
    public void A_RedactResponse_from_before_the_map_existed_still_reads()
    {
        var old = MessagePackSerializer.Serialize(new object?[] { true, "select 1", null });

        var response = MessagePackSerializer.Deserialize<RedactResponse>(old);

        Assert.True(response.Ok);
        Assert.Equal("select 1", response.Text);
        Assert.Empty(response.Placeholders);
    }

    [Fact]
    public void Dialect_error_ids_and_codes_match_the_contract()
    {
        Assert.Equal("dialect.unsupported", DialectErrors.Unsupported);
        Assert.Equal("dialect.password-required", DialectErrors.PasswordRequired);
        Assert.Equal("dialect.credentials-required", DialectErrors.CredentialsRequired);

        var codes = new[] { DialectErrors.UnsupportedCode, DialectErrors.PasswordRequiredCode, DialectErrors.CredentialsRequiredCode };
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, c => Assert.True(c > 0));
        Assert.StartsWith("dialect.unsupported:", DialectErrors.UnsupportedMessage);
        Assert.Contains("Not available for PostgreSQL", DialectErrors.UnsupportedMessage);
    }
}

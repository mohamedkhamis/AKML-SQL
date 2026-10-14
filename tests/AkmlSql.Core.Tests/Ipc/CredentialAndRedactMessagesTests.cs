using System;
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

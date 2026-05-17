using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 021 (web edition) -- M5 schema-cache change detection.
/// Mirrors the SchemaIdentifyHandler test design: the handler is callback-pure
/// so we exercise every path without standing up a real SQL connection.
/// </summary>
public sealed class SchemaChecksumHandlerTests
{
    private static SchemaChecksumHandler Build(string? checksum)
    {
        return new SchemaChecksumHandler(checksumFetcher: _ => checksum);
    }

    [Fact]
    public async Task Returns_checksum_when_fetcher_succeeds()
    {
        var handler = Build("abc123-checksum-value");
        var response = await handler.HandleAsync(
            new SchemaChecksumRequest { SessionId = "s1", DatabaseName = "AdventureWorks" },
            null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        Assert.Equal("abc123-checksum-value", response.Checksum);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public async Task Returns_no_connection_when_fetcher_returns_null()
    {
        var handler = Build(null);
        var response = await handler.HandleAsync(
            new SchemaChecksumRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.NotNull(response.ErrorMessage);
        Assert.Equal(string.Empty, response.Checksum);
    }

    [Fact]
    public async Task Empty_session_id_is_rejected()
    {
        var handler = Build("any");
        var response = await handler.HandleAsync(
            new SchemaChecksumRequest { SessionId = string.Empty }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("SessionId", response.ErrorMessage);
    }

    [Fact]
    public async Task Fetcher_exception_is_captured_into_error_message()
    {
        var handler = new SchemaChecksumHandler(
            checksumFetcher: _ => throw new InvalidOperationException("connection lost"));

        var response = await handler.HandleAsync(
            new SchemaChecksumRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("connection lost", response.ErrorMessage);
    }

    [Fact]
    public async Task Echoes_request_correlation_fields()
    {
        var handler = Build("xyz");
        var response = await handler.HandleAsync(
            new SchemaChecksumRequest
            {
                SessionId = "session-A",
                ServerCanonicalIdentity = "PROD-DB01\\SQL2022",
                DatabaseName = "AdventureWorks2022",
            },
            null!, CancellationToken.None);

        Assert.Equal("session-A", response.SessionId);
        Assert.Equal("PROD-DB01\\SQL2022", response.ServerCanonicalIdentity);
        Assert.Equal("AdventureWorks2022", response.DatabaseName);
    }
}

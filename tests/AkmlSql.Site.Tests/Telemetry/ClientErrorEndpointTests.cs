using System.Text;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Telemetry;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Telemetry;

/// <summary>
/// POST /api/client-errors: the feature switch (404 when off), the 256 KB body cap, JSON
/// tolerance (camelCase, malformed/wrong-shape → 400), batch normalization (level casing,
/// unknown levels and empty events skipped, configured caps enforced) and the 204 contract.
/// </summary>
public sealed class ClientErrorEndpointTests
{
    private const string ValidJson = """
        {
          "installId": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef99",
          "productVersion": "1.4.0",
          "host": "ssms",
          "os": "Windows 11",
          "events": [
            { "utc": "2026-09-15T10:00:00Z", "level": "error", "message": "Parser blew up", "exception": "System.Exception: boom", "source": "parser" },
            { "utc": "2026-09-15T10:01:00Z", "level": "WARNING", "message": "Slow completion" },
            { "utc": "2026-09-15T10:02:00Z", "level": "banana", "message": "unknown level — dropped" },
            { "utc": "2026-09-15T10:03:00Z", "level": "Information" }
          ]
        }
        """;

    private static DefaultHttpContext NewContext(string json, long? contentLength = null)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = contentLength ?? bytes.Length;
        return http;
    }

    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? -1;

    [Fact]
    public async Task Handle_Disabled_Returns404AndAcceptsNothing()
    {
        var sink = new RecordingSink();

        var result = await ClientErrorEndpoint.Handle(
            NewContext(ValidJson), new ClientErrorOptions { Enabled = false }, sink);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Theory]
    [InlineData("{ not json")]   // malformed
    [InlineData("[1,2,3]")]      // well-formed but the wrong shape
    [InlineData("null")]         // JSON null is not a batch
    public async Task Handle_MalformedOrWrongShapeJson_Returns400(string json)
    {
        var sink = new RecordingSink();

        var result = await ClientErrorEndpoint.Handle(NewContext(json), new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task Handle_OversizedContentLength_Returns400WithoutReading()
    {
        var sink = new RecordingSink();
        var http = NewContext(ValidJson, contentLength: 300 * 1024);

        var result = await ClientErrorEndpoint.Handle(http, new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task Handle_OversizedBodyWithoutContentLength_Returns400()
    {
        // Chunked requests carry no Content-Length; the capped read is the real guard.
        var sink = new RecordingSink();
        var json = "{ \"events\": [] }" + new string(' ', 300 * 1024);
        var http = NewContext(json);
        http.Request.ContentLength = null;

        var result = await ClientErrorEndpoint.Handle(http, new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Theory]
    [InlineData("{ \"installId\": \"abc\", \"events\": [] }")]   // empty batch
    [InlineData("{ \"installId\": \"abc\" }")]                   // no events property at all
    public async Task Handle_EmptyBatch_Returns204AndEnqueuesNothing(string json)
    {
        var sink = new RecordingSink();

        var result = await ClientErrorEndpoint.Handle(NewContext(json), new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task Handle_ValidBatch_Returns204AndEnqueuesOneNormalizedBatch()
    {
        var sink = new RecordingSink();

        var result = await ClientErrorEndpoint.Handle(NewContext(ValidJson), new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));

        var batch = Assert.Single(sink.Batches);
        Assert.Equal(2, batch.Errors.Count); // unknown level and message-less event skipped

        var first = batch.Errors[0];
        Assert.Equal("Error", first.Level);                        // casing normalized
        Assert.Equal("Parser blew up", first.Message);
        Assert.Equal("System.Exception: boom", first.Exception);
        Assert.Equal("parser", first.Source);
        Assert.Equal("1.4.0", first.ProductVersion);
        Assert.Equal("ssms", first.Host);
        Assert.Equal(64, first.InstallId!.Length);                 // 70-char id capped
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), first.EventUtc);
        Assert.True(DateTimeOffset.UtcNow - first.ReceivedUtc < TimeSpan.FromMinutes(1));

        var second = batch.Errors[1];
        Assert.Equal("Warning", second.Level);
        Assert.Null(second.Exception);
    }

    [Fact]
    public async Task Handle_EnforcesConfiguredCaps()
    {
        var sink = new RecordingSink();
        var options = new ClientErrorOptions { MaxBatchEvents = 1, MaxMessageLength = 5, MaxExceptionLength = 6 };
        var json = """
            {
              "events": [
                { "level": "Error", "message": "0123456789", "exception": "012345678901" },
                { "level": "Fatal", "message": "truncated away with the rest of the batch" }
              ]
            }
            """;

        var result = await ClientErrorEndpoint.Handle(NewContext(json), options, sink);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var batch = Assert.Single(sink.Batches);
        var error = Assert.Single(batch.Errors); // the batch cap keeps only the first event
        Assert.Equal("01234", error.Message);
        Assert.Equal("012345", error.Exception);
        Assert.Null(error.InstallId);
    }

    [Fact]
    public async Task Handle_BatchCapDropsEverything_Returns204WithoutEnqueuing()
    {
        var sink = new RecordingSink();
        var options = new ClientErrorOptions { MaxBatchEvents = 1 };
        // The only event inside the cap is invalid; the valid one is beyond it.
        var json = """
            { "events": [ { "level": "banana", "message": "x" }, { "level": "Error", "message": "never seen" } ] }
            """;

        var result = await ClientErrorEndpoint.Handle(NewContext(json), options, sink);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task Handle_MissingEventTimestamp_UsesTheReceiveTime()
    {
        var sink = new RecordingSink();
        var json = """{ "events": [ { "level": "Error", "message": "no clock" } ] }""";

        var result = await ClientErrorEndpoint.Handle(NewContext(json), new ClientErrorOptions(), sink);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var error = Assert.Single(Assert.Single(sink.Batches).Errors);
        Assert.Equal(error.ReceivedUtc, error.EventUtc);
    }

    [Theory]
    [InlineData("verbose", "Verbose")]
    [InlineData("Debug", "Debug")]
    [InlineData("INFORMATION", "Information")]
    [InlineData("Warning", "Warning")]
    [InlineData("eRRoR", "Error")]
    [InlineData(" fatal ", "Fatal")]
    [InlineData("warn", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Normalize_MapsSerilogLevelNamesOnly(string? input, string? expected) =>
        Assert.Equal(expected, ClientErrorLevelNormalization.Normalize(input));

    /// <summary>In-memory <see cref="IAnalyticsSink"/> that records what would have been queued.</summary>
    private sealed class RecordingSink : IAnalyticsSink
    {
        public List<ClientErrorBatch> Batches { get; } = [];

        public void EnqueueVisit(VisitInfo visit)
        {
        }

        public void EnqueueDownload(DownloadInfo download)
        {
        }

        public void EnqueueNotFound(NotFoundInfo notFound)
        {
        }

        public void EnqueueClientErrors(ClientErrorBatch batch) => Batches.Add(batch);
    }
}

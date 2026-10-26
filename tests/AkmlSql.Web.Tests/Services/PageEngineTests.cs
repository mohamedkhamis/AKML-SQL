using System.Net;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

public sealed class PageEngineTests
{
    [Theory]
    [InlineData(true, PageEngineKind.Engine)]
    [InlineData(false, PageEngineKind.Static)]
    public async Task Definitive_answers_are_cached_and_probe_the_root_with_no_cache(bool marker, PageEngineKind expected)
    {
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.Equal("https://sql-box:59417/", request.RequestUri!.AbsoluteUri);
            Assert.True(request.Headers.CacheControl?.NoCache);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (marker) response.Headers.Add("X-AKML-Host", "engine");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://sql-box:59417/editor") };
        var service = new PageEngine(http);
        var result = await service.GetAsync();
        Assert.Equal(expected, result.Kind);
        if (marker) Assert.Equal(new PageEngineInfo("sql-box", 59417, "wss"), result.Engine);
        Assert.Same(result, await service.GetAsync());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_failed_probe_is_unknown_and_a_later_engine_response_is_cached()
    {
        var first = true;
        var handler = new Handler(_ =>
        {
            if (first) { first = false; throw new HttpRequestException("not ready"); }
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("X-AKML-Host", "engine");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost:59417/") };
        var service = new PageEngine(http);
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Engine, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Engine, (await service.GetAsync()).Kind);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Three_failures_end_detection_and_are_cached()
    {
        var handler = new Handler(_ => throw new HttpRequestException());
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost:59417/") };
        var service = new PageEngine(http);
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Sixty_seconds_after_the_first_failure_end_detection()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new Handler(_ => throw new HttpRequestException());
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost:59417/") };
        var service = new PageEngine(http, () => now);
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        now = now.AddSeconds(60);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }
}

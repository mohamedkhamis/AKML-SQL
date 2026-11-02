using AkmlSql.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

public sealed class PageEngineTests
{
    [Theory]
    [InlineData(true, PageEngineKind.Engine)]
    [InlineData(false, PageEngineKind.Static)]
    public async Task Definitive_answers_are_cached_and_probe_the_root_with_no_cache(bool marker, PageEngineKind expected)
    {
        var handler = new Handler(url =>
        {
            Assert.Equal("https://sql-box:59417/", url);
            return marker;
        });
        var service = new PageEngine(handler, new Navigation("https://sql-box:59417/"));
        var result = await service.GetAsync();
        Assert.Equal(expected, result.Kind);
        if (marker) Assert.Equal(new PageEngineInfo("sql-box", 59417, "wss"), result.Engine);
        Assert.Same(result, await service.GetAsync());
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, handler.Disposals);
    }

    [Fact]
    public async Task A_failed_probe_is_unknown_and_a_later_engine_response_is_cached()
    {
        var first = true;
        var handler = new Handler(_ =>
        {
            if (first) { first = false; throw new JSException("not ready"); }
            return true;
        });
        var service = new PageEngine(handler, new Navigation("http://localhost:59417/"));
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Engine, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Engine, (await service.GetAsync()).Kind);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Three_failures_end_detection_and_are_cached()
    {
        var handler = new Handler(_ => throw new JSException("network failure"));
        var service = new PageEngine(handler, new Navigation("http://localhost:59417/"));
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
        var handler = new Handler(_ => throw new JSException("network failure"));
        var service = new PageEngine(handler, new Navigation("http://localhost:59417/"), () => now);
        Assert.Equal(PageEngineKind.Unknown, (await service.GetAsync()).Kind);
        now = now.AddSeconds(60);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
        Assert.Equal(PageEngineKind.Static, (await service.GetAsync()).Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_cached_as_a_static_page()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var handler = new Handler(_ => true);
        var service = new PageEngine(handler, new Navigation("http://localhost:59417/"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancelled.Token));
        Assert.Equal(PageEngineKind.Engine, (await service.GetAsync()).Kind);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string origin) => Initialize(origin, origin + "editor");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<string, bool> response) : IJSRuntime, IJSObjectReference
    {
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "import")
            {
                Assert.Equal("./js/akml-page-engine.js", Assert.Single(args!));
                return ValueTask.FromResult((TValue)(object)this);
            }
            Assert.Equal("detect", identifier);
            Calls++;
            return ValueTask.FromResult((TValue)(object)response((string)Assert.Single(args!)!));
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}

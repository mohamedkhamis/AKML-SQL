using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T033 (US5). The ghost-text controller logic: opt-in gate, prompt+prefix
/// cache (no duplicate provider call), rate limiting, fully-local silent skip, and the session
/// request counter. (The CodeMirror grey-text decorator behaviour is verified interactively.)
/// </summary>
public sealed class GhostTextControllerTests
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    private (AiGhostTextService svc, FakeClient client) Build(AiFeatureSettings settings, string provider = "ollama")
    {
        var db = new InMemoryIndexedDbAdapter();
        var store = new AiFeatureSettingsStore(db);
        store.SetAsync(settings).GetAwaiter().GetResult();
        var client = new FakeClient();
        var svc = new AiGhostTextService(client, new FixedPreference(provider), new EmptySchema(), store, () => _now);
        return (svc, client);
    }

    private static AiFeatureSettings Enabled(int max = 1) =>
        new() { GhostTextEnabled = true, GhostTextMaxRequestsPer3s = max, GlobalDefaultMode = AiPrivacyMode.FullSchema };

    [Fact]
    public async Task Disabled_ReturnsNullAndNeverCallsProvider()
    {
        var (svc, client) = Build(new AiFeatureSettings { GhostTextEnabled = false });
        Assert.Null(await svc.CompleteAsync("SELECT * FROM ", CancellationToken.None));
        Assert.Equal(0, client.SendCount);
    }

    [Fact]
    public async Task CacheHit_DoesNotCallProviderTwice()
    {
        var (svc, client) = Build(Enabled());
        var first = await svc.CompleteAsync("SELECT * FROM ", CancellationToken.None);
        var second = await svc.CompleteAsync("SELECT * FROM ", CancellationToken.None);

        Assert.Equal("Orders o", first);
        Assert.Equal(first, second);
        Assert.Equal(1, client.SendCount);     // second served from cache
        Assert.Equal(1, svc.SessionRequestCount);
    }

    [Fact]
    public async Task RateLimit_SkipsBeyondMaxWithinWindow()
    {
        var (svc, client) = Build(Enabled(max: 1));
        var a = await svc.CompleteAsync("prefix-a", CancellationToken.None);   // proceeds
        var b = await svc.CompleteAsync("prefix-b", CancellationToken.None);   // rate-limited

        Assert.NotNull(a);
        Assert.Null(b);
        Assert.Equal(1, client.SendCount);
    }

    [Fact]
    public async Task RateLimit_AllowsAfterWindowElapses()
    {
        var (svc, client) = Build(Enabled(max: 1));
        await svc.CompleteAsync("prefix-a", CancellationToken.None);
        _now = _now.AddSeconds(4);                                            // window elapsed
        var b = await svc.CompleteAsync("prefix-b", CancellationToken.None);

        Assert.NotNull(b);
        Assert.Equal(2, client.SendCount);
    }

    [Fact]
    public async Task FullyLocal_WithCloudCapableProvider_SkipsSilently()
    {
        // gemini is browser-direct capable but NOT local -> fully-local must skip (return null).
        var settings = Enabled();
        settings.GlobalDefaultMode = AiPrivacyMode.FullyLocal;
        var (svc, client) = Build(settings, provider: "gemini");

        Assert.Null(await svc.CompleteAsync("SELECT * FROM ", CancellationToken.None));
        Assert.Equal(0, client.SendCount);
    }

    private sealed class FakeClient : IAiClientFactory
    {
        public int SendCount { get; private set; }
        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct)
        {
            SendCount++;
            return Task.FromResult("Orders o");
        }
#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            SendCount++;
            yield return "Orders o";
        }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    private sealed class FixedPreference : IAiPreference
    {
        private readonly string _id;
        public FixedPreference(string id) => _id = id;
        public Task<string> GetActiveAsync() => Task.FromResult(_id);
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
    }

    private sealed class EmptySchema : IAiSchemaContextProvider
    {
        public Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }
}

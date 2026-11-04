using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>Shared service fakes for AiChatPanel component tests.</summary>
internal sealed class FakeAiClientFactory : IAiClientFactory
{
    public List<AiChatRequest> Requests { get; } = new();

    public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult("It lists databases.");
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string providerId, AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add(request);
        yield return "It lists ";
        yield return "databases.";
        await Task.CompletedTask;
    }

    public bool IsOriginAllowed(string providerId, string origin) => true;
    public bool IsBrowserDirectCapable(string providerId) => true;
}

internal sealed class FakeAiPreference : IAiPreference
{
    private string _active;
    public FakeAiPreference(string active) => _active = active;
    public Task<string> GetActiveAsync() => Task.FromResult(_active);
    public Task SetActiveAsync(string providerId) { _active = providerId; return Task.CompletedTask; }
}

internal sealed class FakeAiFeatureSettings : IAiFeatureSettings
{
    private AiFeatureSettings _settings = new();
    public Task<AiFeatureSettings> GetAsync() => Task.FromResult(_settings);
    public Task SetAsync(AiFeatureSettings settings) { _settings = settings; return Task.CompletedTask; }
    public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(_settings.Resolve(featureId));
    public Task ResetAsync() { _settings = new(); return Task.CompletedTask; }
    public Task ReloadAsync() => Task.CompletedTask;
}

internal sealed class FakeChatHistoryStore : IChatHistoryStore
{
    public ChatConversation? Saved { get; set; }
    public Task<ChatConversation?> GetAsync() => Task.FromResult(Saved);
    public Task SaveAsync(ChatConversation conversation) { Saved = conversation; return Task.CompletedTask; }
    public Task ClearAsync() { Saved = null; return Task.CompletedTask; }
}

internal sealed class FakeDiagnosticsRingBuffer : IDiagnosticsRingBuffer
{
    public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
    public IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
    public void Clear() { }
    public Task FlushAsync() => Task.CompletedTask;
    public Task RestoreAsync() => Task.CompletedTask;
}

/// <summary>Spec 046 (US4): a key vault that only lists agents — the chat's agent selector reads
/// nothing else. Every secret-handling member throws, so a test that reaches one fails loudly.</summary>
internal sealed class FakeAiKeyVault : IAiKeyVault
{
    public List<AiProviderConfig> Configs { get; } = new();

    public Task<IReadOnlyList<AiProviderConfig>> ListAsync() => Task.FromResult<IReadOnlyList<AiProviderConfig>>(Configs);
    public Task<AiProviderConfig?> GetConfigAsync(string agentId) => Task.FromResult(Configs.FirstOrDefault(c => c.Id == agentId));
    public Task<bool> HasKeyAsync(string agentId) => Task.FromResult(Configs.Any(c => c.Id == agentId && c.HasKey));
    public Task SetKeyAsync(string agentId, AiProviderConfig config, string apiKeyPlain) => throw new NotSupportedException();
    public Task<UnwrappedKey> UnwrapForCallAsync(string agentId) => throw new NotSupportedException();
    public Task RemoveAsync(string agentId) => throw new NotSupportedException();
    public Task UpsertConfigAsync(AiProviderConfig config) => throw new NotSupportedException();
    public Task SaveAgentAsync(AiProviderConfig config, string? apiKeyPlain) => throw new NotSupportedException();
    public Task DuplicateAsync(string sourceAgentId, AiProviderConfig copy) => throw new NotSupportedException();
}

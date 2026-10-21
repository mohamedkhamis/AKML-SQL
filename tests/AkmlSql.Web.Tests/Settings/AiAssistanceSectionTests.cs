using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Settings › AI assistance, found in the 2026-10-19 Gemini test: the form refused "Model is required" under a box that showed
/// gemini-flash-latest, and the one provider saved was not made active, so the AI panel still said none was configured.
/// </summary>
public sealed class AiAssistanceSectionTests : BunitContext
{
    private readonly MemoryVault _vault = new();
    private readonly MemoryPreference _preference = new();

    public AiAssistanceSectionTests()
    {
        Services.AddSingleton<IAiKeyVault>(_vault);
        Services.AddSingleton<IAiPreference>(_preference);
        Services.AddSingleton<IAiFeatureSettings>(new Settings());
        Services.AddSingleton<IAiClientFactory>(new Client());
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private IRenderedComponent<AiAssistanceSection> SaveProvider(string providerId, string? model, string key)
    {
        var cut = Render<AiAssistanceSection>();
        cut.Find("form select").Change(providerId);
        if (model != null) cut.FindAll("form input.akml-input")[1].Change(model);   // Display name, Model, ...
        cut.Find("form input[type=password]").Change(key);
        cut.Find("form").Submit();
        return cut;
    }

    [Fact]
    public void An_empty_model_uses_the_default_the_form_shows_and_the_first_provider_becomes_active()
    {
        var cut = SaveProvider("gemini", model: null, key: "test-key");

        cut.WaitForAssertion(() => Assert.Equal("gemini-flash-latest", Assert.Single(_vault.Configs).Model));
        cut.WaitForAssertion(() => Assert.Equal("gemini", _preference.Active));
        Assert.DoesNotContain("Model is required", cut.Markup);
    }

    [Fact]
    public void Azure_still_needs_its_deployment_name()
    {
        var cut = Render<AiAssistanceSection>();
        cut.Find("form select").Change("azure");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Model is required.", cut.Markup));
        Assert.Empty(_vault.Configs);
    }

    [Fact]
    public void Saving_another_provider_leaves_the_active_one_alone()
    {
        _preference.Active = "anthropic";
        _vault.Configs.Add(new AiProviderConfig { ProviderId = "anthropic", DisplayName = "Anthropic", Model = "claude", HasKey = true });

        var cut = SaveProvider("gemini", model: "gemini-flash-latest", key: "test-key");

        cut.WaitForAssertion(() => Assert.Equal(2, _vault.Configs.Count));
        Assert.Equal("anthropic", _preference.Active);
    }

    private sealed class MemoryVault : IAiKeyVault
    {
        public List<AiProviderConfig> Configs { get; } = new();

        public Task SetKeyAsync(string providerId, AiProviderConfig config, string apiKeyPlain)
        {
            Configs.RemoveAll(c => c.ProviderId == providerId);
            config.HasKey = !string.IsNullOrEmpty(apiKeyPlain);
            Configs.Add(config);
            return Task.CompletedTask;
        }

        public Task<AiProviderConfig?> GetConfigAsync(string providerId) => Task.FromResult(Configs.FirstOrDefault(c => c.ProviderId == providerId));
        public Task<IReadOnlyList<AiProviderConfig>> ListAsync() => Task.FromResult<IReadOnlyList<AiProviderConfig>>(Configs.ToList());
        public Task<bool> HasKeyAsync(string providerId) => Task.FromResult(Configs.Any(c => c.ProviderId == providerId && c.HasKey));
        public Task<UnwrappedKey> UnwrapForCallAsync(string providerId) => throw new System.NotSupportedException();
        public Task RemoveAsync(string providerId) { Configs.RemoveAll(c => c.ProviderId == providerId); return Task.CompletedTask; }
        public Task UpsertConfigAsync(AiProviderConfig config) { Configs.RemoveAll(c => c.ProviderId == config.ProviderId); Configs.Add(config); return Task.CompletedTask; }
    }

    private sealed class MemoryPreference : IAiPreference
    {
        public string Active { get; set; } = string.Empty;
        public Task<string> GetActiveAsync() => Task.FromResult(Active);
        public Task SetActiveAsync(string providerId) { Active = providerId; return Task.CompletedTask; }
    }

    private sealed class Settings : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(AiPrivacyMode.FullSchema);
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class Client : IAiClientFactory
    {
        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct) => Task.FromResult(string.Empty);
#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct) { yield break; }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }
}

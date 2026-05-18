using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 021 (web edition) -- M6 task T127. Verifies the contract invariants from
/// contracts/ai-key-wrapping.md applied to the per-provider key vault.
/// </summary>
public sealed class KeyVaultTests
{
    private static (IAiKeyVault vault, InMemoryIndexedDbAdapter db) Build()
    {
        var db = new InMemoryIndexedDbAdapter();
        return (new AiKeyVault(db, new InMemoryWebCryptoWrapper()), db);
    }

    [Fact]
    public async Task Set_then_Unwrap_returns_the_plaintext()
    {
        var (vault, _) = Build();
        var config = new AiProviderConfig { DisplayName = "OpenAI", Model = "gpt-4o" };
        await vault.SetKeyAsync("openai", config, "sk-test-1234");

        using var unwrapped = await vault.UnwrapForCallAsync("openai");
        Assert.Equal("sk-test-1234", unwrapped.Value);
    }

    [Fact]
    public async Task Plaintext_never_appears_in_IndexedDB_after_Set()
    {
        // Contract Invariant 2 (adapted): a grep of the IndexedDB dump must not find
        // the plaintext API key anywhere.
        var (vault, db) = Build();
        const string plain = "sk-this-is-the-secret-key";
        await vault.SetKeyAsync("openai", new AiProviderConfig { Model = "x" }, plain);

        var entries = await db.ListAsync(StoreNames.AiKeys);
        foreach (var (_, value) in entries)
        {
            Assert.DoesNotContain(plain, value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetConfigAsync_returns_config_without_the_key()
    {
        var (vault, _) = Build();
        await vault.SetKeyAsync("openai",
            new AiProviderConfig { DisplayName = "OpenAI", Model = "gpt-4o" }, "sk-test");

        var config = await vault.GetConfigAsync("openai");
        Assert.NotNull(config);
        Assert.Equal("gpt-4o", config!.Model);
        Assert.True(config.HasKey);
    }

    [Fact]
    public async Task ListAsync_returns_every_provider_sorted_by_display_name()
    {
        var (vault, _) = Build();
        await vault.SetKeyAsync("a", new AiProviderConfig { DisplayName = "Anthropic", Model = "x" }, "key1");
        await vault.SetKeyAsync("o", new AiProviderConfig { DisplayName = "OpenAI", Model = "y" }, "key2");

        var list = await vault.ListAsync();
        Assert.Equal(new[] { "Anthropic", "OpenAI" }, list.Select(c => c.DisplayName));
    }

    [Fact]
    public async Task HasKeyAsync_returns_false_for_unknown_provider()
    {
        var (vault, _) = Build();
        Assert.False(await vault.HasKeyAsync("missing"));
    }

    [Fact]
    public async Task UnwrapForCallAsync_throws_for_unknown_provider()
    {
        var (vault, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.UnwrapForCallAsync("missing"));
    }

    [Fact]
    public async Task RemoveAsync_drops_the_record()
    {
        var (vault, db) = Build();
        await vault.SetKeyAsync("openai", new AiProviderConfig { Model = "x" }, "sk-test");
        Assert.True(await vault.HasKeyAsync("openai"));

        await vault.RemoveAsync("openai");

        Assert.False(await vault.HasKeyAsync("openai"));
        Assert.Equal(0, db.Count(StoreNames.AiKeys));
    }

    [Fact]
    public async Task Aad_persists_with_provider_id_prefix()
    {
        // Contract Invariant 5 -- belt: the persisted record's Aad field MUST be
        // UTF-8 of "akmlsql.aikey." + providerId.
        var (_, db) = Build();
        var vault = new AiKeyVault(db, new InMemoryWebCryptoWrapper());
        await vault.SetKeyAsync("openai", new AiProviderConfig { Model = "x" }, "sk");

        var raw = await db.GetAsync(StoreNames.AiKeys, "openai");
        Assert.NotNull(raw);
        using var doc = JsonDocument.Parse(raw!);
        var aadBase64 = doc.RootElement.GetProperty("Aad").GetString();
        Assert.NotNull(aadBase64);
        var aadBytes = Convert.FromBase64String(aadBase64!);
        Assert.Equal("akmlsql.aikey.openai", Encoding.UTF8.GetString(aadBytes));
    }

    [Fact]
    public async Task SetKeyAsync_rejects_empty_provider()
    {
        var (vault, _) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            vault.SetKeyAsync("", new AiProviderConfig(), "x"));
    }

    [Fact]
    public async Task SetKeyAsync_accepts_empty_key_for_local_providers()
    {
        // Local providers (Ollama, LM Studio) don't authenticate with a key. The
        // vault should persist the config with HasKey=false rather than throw —
        // per-provider gating is the UI's job, not the vault's.
        var (vault, _) = Build();
        await vault.SetKeyAsync("ollama", new AiProviderConfig
        {
            DisplayName = "Ollama (local)",
            Model = "llama3.1:8b",
            Endpoint = "http://localhost:11434",
        }, string.Empty);

        var config = await vault.GetConfigAsync("ollama");
        Assert.NotNull(config);
        Assert.False(config!.HasKey);
        Assert.Equal("llama3.1:8b", config.Model);
    }

    [Fact]
    public void UnwrappedKey_Dispose_clears_the_buffer()
    {
        var k = new UnwrappedKey("plain-value");
        Assert.Equal("plain-value", k.Value);
        k.Dispose();
        // After dispose the Value getter returns an empty string.
        Assert.Equal(string.Empty, k.Value);
    }
}

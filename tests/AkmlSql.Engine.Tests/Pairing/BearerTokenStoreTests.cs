using System;
using System.IO;
using System.Linq;
using AkmlSql.Engine.Pairing;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 021 (web edition) -- M3 task T066. Covers <see cref="BearerTokenStore"/>'s
/// hash-at-rest property, mint/validate happy path, revocation, TTL expiry, and on-disk
/// persistence round-trip.
/// </summary>
public sealed class BearerTokenStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public BearerTokenStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"akml_btst_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "tokens.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Mint_then_validate_returns_true()
    {
        var store = new BearerTokenStore(_path);
        var token = store.Mint("Chrome on M1 MBP");

        Assert.True(store.Validate(token));
    }

    [Fact]
    public void Validate_rejects_unknown_token()
    {
        var store = new BearerTokenStore(_path);
        Assert.False(store.Validate("not-a-real-token-just-random-hex-bytes-here"));
    }

    [Fact]
    public void Validate_rejects_empty_input()
    {
        var store = new BearerTokenStore(_path);
        Assert.False(store.Validate(string.Empty));
        Assert.False(store.Validate(null!));
    }

    [Fact]
    public void Raw_token_never_appears_in_the_on_disk_file()
    {
        // Spec 021 contracts/pairing-flow.md: "Engine MUST hash the bearer token at rest.
        // The plain token MUST NOT be stored in BearerTokenStore."
        var store = new BearerTokenStore(_path);
        var token = store.Mint("test-browser");

        var diskContents = File.ReadAllText(_path);

        Assert.DoesNotContain(token, diskContents);
        // The hash IS present.
        var expectedHash = BearerTokenStore.HashToken(token);
        Assert.Contains(expectedHash, diskContents);
    }

    [Fact]
    public void TTL_expiry_invalidates_the_token()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = () => now;
        var store = new BearerTokenStore(_path, ttl: TimeSpan.FromMinutes(1), clock: clock);

        var token = store.Mint("test");
        Assert.True(store.Validate(token));

        // Advance past the TTL.
        now = now.Add(TimeSpan.FromMinutes(2));
        Assert.False(store.Validate(token));
    }

    [Fact]
    public void RevokeAll_invalidates_all_outstanding_tokens()
    {
        var store = new BearerTokenStore(_path);
        var t1 = store.Mint("browser-1");
        var t2 = store.Mint("browser-2");

        store.RevokeAll();

        Assert.False(store.Validate(t1));
        Assert.False(store.Validate(t2));
        Assert.Empty(store.List());
    }

    [Fact]
    public void RevokeByHash_removes_only_the_matching_entry()
    {
        var store = new BearerTokenStore(_path);
        var t1 = store.Mint("browser-1");
        var t2 = store.Mint("browser-2");
        var hash1 = BearerTokenStore.HashToken(t1);

        Assert.True(store.RevokeByHash(hash1));

        Assert.False(store.Validate(t1));
        Assert.True(store.Validate(t2));
    }

    [Fact]
    public void Persistence_round_trips_through_a_fresh_store_instance()
    {
        var t1 = new BearerTokenStore(_path).Mint("persisted-browser");

        // Reload the file in a brand-new instance.
        var reloaded = new BearerTokenStore(_path);
        Assert.True(reloaded.Validate(t1));
    }

    [Fact]
    public void List_returns_metadata_keyed_by_hash_only()
    {
        var store = new BearerTokenStore(_path);
        var t1 = store.Mint("browser-a");
        var t2 = store.Mint("browser-b");

        var entries = store.List();

        Assert.Equal(2, entries.Count);
        // The hash is the only identifier exposed -- raw tokens never appear in the List output.
        Assert.All(entries, e =>
        {
            Assert.NotEqual(t1, e.TokenHash);
            Assert.NotEqual(t2, e.TokenHash);
            Assert.False(string.IsNullOrEmpty(e.BrowserLabel));
        });
    }
}

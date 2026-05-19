using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 foundation. In-memory <see cref="IIndexedDbAdapter"/> used
/// by tests that exercise store logic without a real browser. Safe for parallel use by xUnit
/// (the test classes do not share instances).
/// </summary>
public sealed class InMemoryIndexedDbAdapter : IIndexedDbAdapter
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _stores = new();

    private ConcurrentDictionary<string, string> Bucket(string store) =>
        _stores.GetOrAdd(store, _ => new ConcurrentDictionary<string, string>());

    public Task<string?> GetAsync(string store, string key)
    {
        Bucket(store).TryGetValue(key, out var v);
        return Task.FromResult<string?>(v);
    }

    public Task SetAsync(string store, string key, string valueJson)
    {
        Bucket(store)[key] = valueJson;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string store, string key)
    {
        Bucket(store).TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store)
    {
        var snapshot = new List<KeyValuePair<string, string>>(Bucket(store));
        return Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>(snapshot);
    }

    public Task ClearAsync(string store)
    {
        Bucket(store).Clear();
        return Task.CompletedTask;
    }

    /// <summary>Test helper: pre-load a record without going through SetAsync.</summary>
    public void Seed(string store, string key, string valueJson) =>
        Bucket(store)[key] = valueJson;

    /// <summary>Test helper: read current record count for assertions.</summary>
    public int Count(string store) => Bucket(store).Count;
}

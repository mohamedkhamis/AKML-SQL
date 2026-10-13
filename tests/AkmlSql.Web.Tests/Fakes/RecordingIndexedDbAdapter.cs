using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 041 — the in-memory adapter, recording every write, delete and clear, so a test can assert that a reset
/// deletes only its own key and that loading or filtering writes nothing.
/// </summary>
internal sealed class RecordingIndexedDbAdapter : IIndexedDbAdapter
{
    private readonly InMemoryIndexedDbAdapter _inner = new();

    public List<(string Store, string Key, string Value)> Writes { get; } = new();
    public List<(string Store, string Key)> Deletes { get; } = new();
    public List<string> Clears { get; } = new();

    /// <summary>Every change of any kind.</summary>
    public int ChangeCount => Writes.Count + Deletes.Count + Clears.Count;

    public Task<string?> GetAsync(string store, string key) => _inner.GetAsync(store, key);

    public Task SetAsync(string store, string key, string valueJson)
    {
        Writes.Add((store, key, valueJson));
        return _inner.SetAsync(store, key, valueJson);
    }

    public Task DeleteAsync(string store, string key)
    {
        Deletes.Add((store, key));
        return _inner.DeleteAsync(store, key);
    }

    public Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store) => _inner.ListAsync(store);

    public Task ClearAsync(string store)
    {
        Clears.Add(store);
        return _inner.ClearAsync(store);
    }

    /// <summary>Put a record in place without recording it as a change.</summary>
    public void Seed(string store, string key, string valueJson) => _inner.Seed(store, key, valueJson);

    /// <summary>Forget the recorded changes (after arranging a test).</summary>
    public void ResetLog()
    {
        Writes.Clear();
        Deletes.Clear();
        Clears.Clear();
    }
}

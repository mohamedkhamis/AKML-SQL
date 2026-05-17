using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 foundation. Abstraction over the browser's IndexedDB API.
/// All M2 stores (<see cref="ProfileStore"/>, <see cref="AnalysisSettingsStore"/>,
/// <see cref="EditorSessionStore"/>, <see cref="DiagnosticsRingBufferReal"/>, and the M5
/// SchemaCacheStore) talk to IndexedDB through this interface so:
///
/// <list type="bullet">
///   <item>The production binding (<see cref="JsIndexedDbAdapter"/>) calls into the
///   <c>akml-indexeddb.js</c> shim via <c>IJSRuntime</c>.</item>
///   <item>Tests bind to <see cref="InMemoryIndexedDbAdapter"/> and run without a browser.</item>
/// </list>
///
/// Records are stored as JSON strings -- callers serialise / deserialise. Keeping the
/// adapter primitive simple keeps the JS shim small.
/// </summary>
public interface IIndexedDbAdapter
{
    /// <summary>Read one record by key. Returns null when the record does not exist.</summary>
    Task<string?> GetAsync(string store, string key);

    /// <summary>Insert or replace a record. The store is created on first write.</summary>
    Task SetAsync(string store, string key, string valueJson);

    /// <summary>Remove a record. No-op when the record does not exist.</summary>
    Task DeleteAsync(string store, string key);

    /// <summary>
    /// Enumerate every record in the store. The returned pairs are not ordered -- callers
    /// that need an order must sort on a field in the deserialised record.
    /// </summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store);

    /// <summary>Drop every record in the store.</summary>
    Task ClearAsync(string store);
}

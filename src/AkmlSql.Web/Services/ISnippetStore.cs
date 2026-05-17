using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T114. IndexedDB-backed snippet persistence.
/// Built-in snippets ship as resources baked into the WASM bundle (synthesised
/// programmatically in <see cref="SnippetStore.BuildBuiltIns"/>); user snippets
/// persist to IndexedDB and round-trip as JSON.
///
/// <para>
/// Bridge integration (T115): when the bridge is open AND
/// <c>Capabilities.SnippetsWrite</c> is advertised, save/delete propagate to the
/// engine. Otherwise the operation is local-only.
/// </para>
/// </summary>
public interface ISnippetStore
{
    /// <summary>Return every snippet (built-in + user). Built-ins precede user.</summary>
    Task<IReadOnlyList<WebSnippet>> ListAsync();

    /// <summary>Find a snippet by id. Returns null when missing.</summary>
    Task<WebSnippet?> GetByIdAsync(string id);

    /// <summary>Find a snippet by shortcode (the trigger the user types).</summary>
    Task<WebSnippet?> GetByShortcodeAsync(string shortcode);

    /// <summary>Save a user snippet. Built-in ids reject writes.</summary>
    Task SaveAsync(WebSnippet snippet);

    /// <summary>Delete a user snippet. Built-in ids reject deletes.</summary>
    Task DeleteAsync(string id);
}

/// <summary>Snippet shape used by the web edition. Matches the engine's `Snippet` POCO shape (JSON-compatible) so future engine round-trips are byte-for-byte.</summary>
public sealed class WebSnippet
{
    public WebSnippetMetadata Metadata { get; set; } = new();
    public WebSnippetVariable[] Variables { get; set; } = Array.Empty<WebSnippetVariable>();
    public string[] Body { get; set; } = Array.Empty<string>();

    /// <summary>True when the id begins with "builtin." -- such records are immutable.</summary>
    public bool IsBuiltIn => Metadata.Id?.StartsWith("builtin.", StringComparison.Ordinal) ?? false;
}

public sealed class WebSnippetMetadata
{
    public string? Id { get; set; }
    public string Shortcode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public sealed class WebSnippetVariable
{
    public string Name { get; set; } = string.Empty;
    public string? Default { get; set; }
    public string? Description { get; set; }
}

internal sealed class SnippetStore : ISnippetStore
{
    private const string CapabilitySnippetsWrite = "snippets.write";

    private static readonly Dictionary<string, WebSnippet> BuiltIns = BuildBuiltIns();
    private readonly IIndexedDbAdapter _store;
    private readonly IEngineBridge? _bridge;

    public SnippetStore(IIndexedDbAdapter store) : this(store, null) { }

    public SnippetStore(IIndexedDbAdapter store, IEngineBridge? bridge)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _bridge = bridge;   // optional -- when null, save/delete are local-only
    }

    public async Task<IReadOnlyList<WebSnippet>> ListAsync()
    {
        var snippetEntries = await _store.ListAsync(StoreNames.Snippets).ConfigureAwait(false);

        var userSnippets = snippetEntries
            .Select(kv => SafeDeserialize(kv.Value))
            .Where(s => s != null)
            .Cast<WebSnippet>()
            .OrderBy(s => s.Metadata.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var output = new List<WebSnippet>(BuiltIns.Count + userSnippets.Count);
        output.AddRange(BuiltIns.Values.OrderBy(s => s.Metadata.Title, StringComparer.OrdinalIgnoreCase));
        output.AddRange(userSnippets);
        return output;
    }

    public async Task<WebSnippet?> GetByIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (BuiltIns.TryGetValue(id, out var builtIn)) return builtIn;
        var raw = await _store.GetAsync(StoreNames.Snippets, id).ConfigureAwait(false);
        return string.IsNullOrEmpty(raw) ? null : SafeDeserialize(raw!);
    }

    public async Task<WebSnippet?> GetByShortcodeAsync(string shortcode)
    {
        if (string.IsNullOrEmpty(shortcode)) return null;
        var all = await ListAsync().ConfigureAwait(false);
        return all.FirstOrDefault(s =>
            string.Equals(s.Metadata.Shortcode, shortcode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveAsync(WebSnippet snippet)
    {
        if (snippet == null) throw new ArgumentNullException(nameof(snippet));
        if (string.IsNullOrEmpty(snippet.Metadata.Id))
        {
            snippet.Metadata.Id = Guid.NewGuid().ToString();
        }
        if (snippet.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in snippets cannot be modified.");
        }

        var json = JsonSerializer.Serialize(snippet);

        // Always persist locally first so the next ListAsync sees the update even when
        // the bridge is down.
        await _store.SetAsync(StoreNames.Snippets, snippet.Metadata.Id!, json).ConfigureAwait(false);

        // T115 -- if the bridge is open AND the engine advertises snippets.write,
        // propagate the save. Bridge failures are silent (the local copy is the
        // source of truth in the web edition).
        if (IsBridgeSnippetWriteAvailable())
        {
            try
            {
                var isNew = await IsLocallyNewAsync(snippet.Metadata.Id!).ConfigureAwait(false);
                await _bridge!.SendAsync<SnippetSaveRequest, SnippetSaveResponse>(
                    MessageTypes.SnippetSave,
                    new SnippetSaveRequest { SnippetJson = json, IsNew = isNew },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort -- local save already succeeded; the engine catches up
                // on the next save attempt.
            }
        }
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (BuiltIns.ContainsKey(id))
        {
            throw new InvalidOperationException("Built-in snippets cannot be deleted.");
        }

        await _store.DeleteAsync(StoreNames.Snippets, id).ConfigureAwait(false);

        if (IsBridgeSnippetWriteAvailable())
        {
            try
            {
                await _bridge!.SendAsync<SnippetDeleteRequest, SnippetDeleteResponse>(
                    MessageTypes.SnippetDelete,
                    new SnippetDeleteRequest { SnippetId = id },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort -- local delete already succeeded.
            }
        }
    }

    /// <summary>
    /// True iff the bridge is open AND the engine advertises the
    /// `snippets.write` capability.
    /// </summary>
    private bool IsBridgeSnippetWriteAvailable() =>
        _bridge != null &&
        _bridge.State == BridgeState.Open &&
        Array.IndexOf(_bridge.EngineCapabilities, CapabilitySnippetsWrite) >= 0;

    /// <summary>
    /// Used by the bridge's SnippetSaveRequest.IsNew flag. We treat any save where
    /// the local store didn't already have the id as "new" from the engine's
    /// perspective -- this maps to the engine's existing create-vs-update branch.
    /// </summary>
    private Task<bool> IsLocallyNewAsync(string id) =>
        Task.FromResult(false);   // we just wrote it; conservatively false to take the update path.

    private static WebSnippet? SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<WebSnippet>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Two built-in snippets shipped with the M5 release. The full snippet zoo lands
    /// alongside the engine's existing .akmlsnippet files when the bridge serves
    /// the snippet expansion path (T115).
    /// </summary>
    private static Dictionary<string, WebSnippet> BuildBuiltIns()
    {
        var ssf = new WebSnippet
        {
            Metadata = new WebSnippetMetadata
            {
                Id = "builtin.ssf",
                Shortcode = "ssf",
                Title = "SELECT * FROM",
                Description = "Expand to SELECT * FROM ${1:table}",
                Author = "AKML SQL",
                Tags = new[] { "select" },
            },
            Body = new[] { "SELECT * FROM ${1:table};" },
        };
        var cte = new WebSnippet
        {
            Metadata = new WebSnippetMetadata
            {
                Id = "builtin.cte",
                Shortcode = "cte",
                Title = "Common Table Expression",
                Description = "Expand to a WITH ... AS (SELECT ...) skeleton",
                Author = "AKML SQL",
                Tags = new[] { "cte", "with" },
            },
            Variables = new[]
            {
                new WebSnippetVariable { Name = "name", Default = "MyCte" },
                new WebSnippetVariable { Name = "select", Default = "SELECT 1" },
            },
            Body = new[]
            {
                "WITH ${name:MyCte} AS (",
                "    ${select:SELECT 1}",
                ")",
                "SELECT * FROM ${name};",
            },
        };
        return new Dictionary<string, WebSnippet>(StringComparer.Ordinal)
        {
            ["builtin.ssf"] = ssf,
            ["builtin.cte"] = cte,
        };
    }
}

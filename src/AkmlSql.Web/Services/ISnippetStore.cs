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

    /// <summary>
    /// Spec 027 (M5 offline closure) T008 / FR-003: true when this snippet wraps a
    /// selection (surround-with eligible). Mirrors the engine's
    /// <c>SnippetMetadata.SurroundsWith</c> so import/export round-trips stay lossless.
    /// Surround-capable bodies embed the engine-native <c>$SELECTEDTEXT$</c> token where the
    /// current selection lands (NOT the VS-Code <c>$selected$</c> form).
    /// </summary>
    public bool SurroundsWith { get; set; }
}

public sealed class WebSnippetVariable
{
    public string Name { get; set; } = string.Empty;
    public string? Default { get; set; }
    public string? Description { get; set; }

    /// <summary>Spec 027 T008: optional hover tooltip, mirroring the engine's <c>SnippetVariable.Tooltip</c>.</summary>
    public string? Tooltip { get; set; }
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
    /// Spec 027 (M5 offline closure) T009 / FR-001: the curated built-in snippet set,
    /// synthesised programmatically (the same convention <see cref="ProfileStore"/> uses for
    /// built-in profiles — no MSBuild EmbeddedResource gymnastics for the WASM bundle). The
    /// set is the in-repo source of record: the engine ships no canonical <c>.akmlsnippet</c>
    /// files (it loads built-ins from an installer-placed directory at runtime), so this is
    /// defined fresh per spec 027 §Assumptions.
    ///
    /// <para>
    /// <b>Placeholder syntax is the ENGINE-NATIVE form</b> (confirmed against
    /// <c>AkmlSql.Engine.Snippets.PlaceholderParser</c>, regex <c>\$([A-Za-z_]\w*)\$</c>): a
    /// tab-stop / variable is <c>$Name$</c> — it must start with a letter or underscore, so a
    /// numbered <c>$1$</c> is NOT valid; use descriptive names like <c>$table$</c>. The final
    /// caret is <c>$CURSOR$</c>; the selection slot for surround-with is <c>$SELECTEDTEXT$</c>.
    /// We do NOT use the VS-Code/CodeMirror <c>${1:label}</c> / <c>$selected$</c> dialect — the
    /// editor's <c>expandSnippet</c> translates <c>$Name$</c> to CM6 placeholders at expand
    /// time, so a web-authored snippet exported as <c>.akmlsnippet</c> ALSO expands correctly
    /// on the engine/WPF surface (FR-006 / SC-002 cross-surface fidelity). Default text for a
    /// named stop comes from the matching <see cref="WebSnippetVariable"/> (else the name).
    /// </para>
    /// </summary>
    private static Dictionary<string, WebSnippet> BuildBuiltIns()
    {
        var defs = new (string Shortcode, string Title, string Desc, string[] Tags, bool Surrounds,
                        (string Name, string Default)[] Vars, string[] Body)[]
        {
            ("ssf", "SELECT * FROM", "SELECT * FROM a table", new[] { "select" }, false,
                new[] { ("table", "table") },
                new[] { "SELECT * FROM $table$$CURSOR$;" }),

            ("sel", "SELECT columns", "SELECT a column list with a WHERE clause", new[] { "select" }, false,
                new[] { ("columns", "columns"), ("table", "table"), ("condition", "condition") },
                new[] { "SELECT $columns$", "FROM $table$", "WHERE $condition$;" }),

            ("cte", "Common Table Expression", "A WITH ... AS (SELECT ...) skeleton", new[] { "cte", "with" }, false,
                new[] { ("name", "MyCte"), ("query", "SELECT 1") },
                new[] { "WITH $name$ AS (", "    $query$", ")", "SELECT * FROM $name$;" }),

            ("ins", "INSERT INTO", "INSERT INTO with an explicit column list", new[] { "insert" }, false,
                new[] { ("table", "table"), ("columns", "columns"), ("values", "values") },
                new[] { "INSERT INTO $table$ ($columns$)", "VALUES ($values$);" }),

            ("upd", "UPDATE SET", "UPDATE ... SET ... WHERE", new[] { "update" }, false,
                new[] { ("table", "table"), ("column", "column"), ("value", "value"), ("condition", "condition") },
                new[] { "UPDATE $table$", "SET $column$ = $value$", "WHERE $condition$;" }),

            ("del", "DELETE FROM", "DELETE FROM ... WHERE (guarded with a WHERE)", new[] { "delete" }, false,
                new[] { ("table", "table"), ("condition", "condition") },
                new[] { "DELETE FROM $table$", "WHERE $condition$;" }),

            ("ij", "INNER JOIN", "An INNER JOIN ... ON clause", new[] { "join" }, false,
                new[] { ("table", "table"), ("alias", "alias"), ("condition", "condition") },
                new[] { "INNER JOIN $table$ AS $alias$", "    ON $condition$" }),

            ("crproc", "CREATE PROCEDURE", "A stored-procedure skeleton", new[] { "ddl", "procedure" }, false,
                new[] { ("name", "dbo.MyProcedure"), ("param", "@param int") },
                new[]
                {
                    "CREATE PROCEDURE $name$",
                    "    $param$",
                    "AS",
                    "BEGIN",
                    "    SET NOCOUNT ON;",
                    "    $CURSOR$",
                    "END;",
                }),

            // ── surround-with snippets: $SELECTEDTEXT$ is replaced by the current selection ──
            ("beginend", "Surround with BEGIN/END", "Wrap the selection in a BEGIN ... END block",
                new[] { "surround", "block" }, true,
                Array.Empty<(string, string)>(),
                new[] { "BEGIN", "    $SELECTEDTEXT$", "END;" }),

            ("iftest", "Surround with IF", "Wrap the selection in an IF block",
                new[] { "surround", "control" }, true,
                new[] { ("condition", "condition") },
                new[] { "IF $condition$", "BEGIN", "    $SELECTEDTEXT$", "END;" }),

            ("try", "Surround with TRY/CATCH", "Wrap the selection in a TRY ... CATCH block",
                new[] { "surround", "error" }, true,
                Array.Empty<(string, string)>(),
                new[]
                {
                    "BEGIN TRY",
                    "    $SELECTEDTEXT$",
                    "END TRY",
                    "BEGIN CATCH",
                    "    $CURSOR$",
                    "END CATCH;",
                }),
        };

        var map = new Dictionary<string, WebSnippet>(StringComparer.Ordinal);
        foreach (var d in defs)
        {
            var id = "builtin." + d.Shortcode;
            map[id] = new WebSnippet
            {
                Metadata = new WebSnippetMetadata
                {
                    Id = id,
                    Shortcode = d.Shortcode,
                    Title = d.Title,
                    Description = d.Desc,
                    Author = "AKML SQL",
                    Tags = d.Tags,
                    SurroundsWith = d.Surrounds,
                },
                Variables = d.Vars.Select(v => new WebSnippetVariable { Name = v.Name, Default = v.Default }).ToArray(),
                Body = d.Body,
            };
        }
        return map;
    }
}

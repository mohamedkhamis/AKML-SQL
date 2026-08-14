using System;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// The web has one editor, but its Blazor Server circuit is destroyed by a full page reload. An
/// in-memory key would mint a new query-grouping session on every reload, splitting one piece of
/// work into many history entries. Persisting the key alongside the rest of the editor session
/// record (see <see cref="EditorSessionRecord.SessionKey"/>) keeps a reload inside ONE history
/// session; "Reset editor session" (Settings page) is the deliberate boundary that starts a new one.
/// </summary>
internal static class EditorSessionKeys
{
    /// <summary>Returns the persisted session key, minting and persisting a new GUID "N" key on
    /// first use (or after a reset).</summary>
    internal static async Task<string> GetOrCreateAsync(IEditorSessionStore store) =>
        await GetOrCreateAsync(store, await store.RestoreAsync().ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Same as <see cref="GetOrCreateAsync(IEditorSessionStore)"/>, but reuses a record the
    /// caller already restored instead of round-tripping the store again. Editor.razor's
    /// <c>OnInitializedAsync</c> already calls <c>SessionStore.RestoreAsync()</c> once to seed
    /// <c>_initialText</c> -- this overload lets it hand that same record over rather than paying for
    /// a second IndexedDB read on every page load.</summary>
    internal static async Task<string> GetOrCreateAsync(IEditorSessionStore store, EditorSessionRecord? alreadyRestored)
    {
        var record = alreadyRestored ?? new EditorSessionRecord();
        if (string.IsNullOrEmpty(record.SessionKey))
        {
            record.SessionKey = Guid.NewGuid().ToString("N");
            await store.SaveAsync(record).ConfigureAwait(false);
        }
        return record.SessionKey!;
    }

    /// <summary>Clears the persisted session key so the next <see cref="GetOrCreateAsync"/> mints a
    /// fresh one. Does not touch the rest of the record (document text / caret / active profile).</summary>
    internal static async Task ResetAsync(IEditorSessionStore store)
    {
        var record = await store.RestoreAsync().ConfigureAwait(false) ?? new EditorSessionRecord();
        record.SessionKey = null;
        await store.SaveAsync(record).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the record for a debounced text-change save (Editor.razor's <c>OnTextChangedAsync</c>,
    /// fired on every keystroke). <see cref="IEditorSessionStore.SaveAsync"/> overwrites the WHOLE
    /// persisted record, so <paramref name="sessionKey"/> must be threaded through explicitly here —
    /// building the record without it would silently wipe the session key on the very first keystroke
    /// after it was minted, and every reload after that would start a new session. Extracted as a pure
    /// helper (mirrors <c>WebHistoryLogic</c>) so this exact failure mode is unit-testable without
    /// standing up a full render of the Editor page.
    /// </summary>
    internal static EditorSessionRecord BuildTextChangeRecord(string sessionKey, string documentText, string? activeProfileId) =>
        new()
        {
            DocumentText = documentText,
            CursorOffset = 0,
            ActiveProfileId = activeProfileId,
            SessionKey = sessionKey,
        };

    /// <summary>
    /// Rewrites the persisted DocumentText via read-modify-write, preserving whatever SessionKey (and
    /// every other field) is already there. Used by History.razor's "Open in Editor" / "Re-execute"
    /// actions, which previously did <c>SaveAsync(new EditorSessionRecord { DocumentText = ... })</c>
    /// -- the identical defect class fixed in <see cref="BuildTextChangeRecord"/>: SaveAsync overwrites
    /// the WHOLE persisted record, so constructing a fresh one silently dropped SessionKey to null. The
    /// web has ONE editor, so opening a past query from History is still the same editor session;
    /// preserving (not minting a fresh key) keeps that continuity. "Reset editor session" remains the
    /// sole deliberate boundary that starts a new one.
    /// </summary>
    internal static async Task SetDocumentTextAsync(IEditorSessionStore store, string documentText)
    {
        var record = await store.RestoreAsync().ConfigureAwait(false) ?? new EditorSessionRecord();
        record.DocumentText = documentText;
        await store.SaveAsync(record).ConfigureAwait(false);
    }
}

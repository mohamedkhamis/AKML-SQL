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
    internal static async Task<string> GetOrCreateAsync(IEditorSessionStore store)
    {
        var record = await store.RestoreAsync().ConfigureAwait(false) ?? new EditorSessionRecord();
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
}

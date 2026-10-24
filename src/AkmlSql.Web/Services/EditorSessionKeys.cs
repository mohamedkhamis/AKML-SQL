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
    /// fresh one. Does not touch the rest of the record (document text / caret / active profile).
    /// <para>
    /// Finding 8 (PR #249 review): a no-op when there is nothing to clear. Settings.razor's
    /// ResetSessionAsync calls <see cref="IEditorSessionStore.ClearAsync"/> (which drops the
    /// persisted record entirely) immediately BEFORE calling this method as an explicit, belt-
    /// and-suspenders boundary. The old version unconditionally did
    /// <c>RestoreAsync() ?? new EditorSessionRecord()</c> then <c>SaveAsync</c> — which, right
    /// after a Clear, resurrected a brand-new (empty) record in the store moments later, defeating
    /// "reset leaves the store genuinely empty". Only touch the store when a record actually
    /// exists AND still carries a key to clear; a store with nothing persisted (or a record whose
    /// key is already null) is left exactly as it was.
    /// </para>
    /// </summary>
    internal static async Task ResetAsync(IEditorSessionStore store)
    {
        var record = await store.RestoreAsync().ConfigureAwait(false);
        if (record == null || record.SessionKey == null) return;

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
    /// <para>Spec 041 (R64): the document's name, modified state and default-name counter are threaded through
    /// the same way.</para>
    internal static EditorSessionRecord BuildTextChangeRecord(string sessionKey, string documentText, string? activeProfileId,
        string? documentName = null, bool isModified = false, int nextDefaultNumber = 1) =>
        new()
        {
            DocumentText = documentText,
            CursorOffset = 0,
            ActiveProfileId = activeProfileId,
            SessionKey = sessionKey,
            DocumentName = documentName,
            IsModified = isModified,
            NextDefaultNumber = nextDefaultNumber,
        };

    /// <summary>
    /// Spec 041 (R64, FR-072) — New query and Open: a fresh record for a new document, with a NEW session key (the
    /// document boundary, so earlier history entries keep their names). "Reset editor session" keeps
    /// <see cref="IEditorSessionStore.ClearAsync"/>. Returns the new key.
    /// </summary>
    internal static async Task<string> StartDocumentAsync(IEditorSessionStore store, string name, string text, int nextDefaultNumber)
    {
        var previous = await store.RestoreAsync().ConfigureAwait(false);
        var key = Guid.NewGuid().ToString("N");
        await store.SaveAsync(new EditorSessionRecord
        {
            DocumentText = text ?? string.Empty,
            CursorOffset = 0,
            ActiveProfileId = previous?.ActiveProfileId,
            SessionKey = key,
            DocumentName = name,
            IsModified = false,
            NextDefaultNumber = Math.Max(1, nextDefaultNumber),
        }).ConfigureAwait(false);
        return key;
    }

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

    /// <summary>
    /// Spec 043 (research R8, data-model §5): the first load after the upgrade to query tabs. The single document of before
    /// tabs — its text, caret, name, modified state, history key and default-name counter — becomes tab 1, with a new engine
    /// session id. The tab record is written first, then the set — only if no other window of the app made one meanwhile
    /// (review of US1: that window's set then stands, and this copy of tab 1 is deleted) — then the old record is cleared, so a
    /// load stopped at any point finds either no set (and moves the document again) or a set and its tab.
    /// With no old record the editor starts with one empty <c>SQLQuery1</c>.
    /// </summary>
    internal static async Task<EditorTabSetRecord> MigrateLegacyAsync(IEditorSessionStore store)
    {
        var legacy = await store.RestoreAsync().ConfigureAwait(false);
        int next = Math.Max(1, legacy?.NextDefaultNumber ?? 1);
        var tabId = Guid.NewGuid().ToString("N");
        string name;
        if (string.IsNullOrWhiteSpace(legacy?.DocumentName)) name = EditorDocument.DefaultName(next++);
        else name = legacy!.DocumentName!;

        var record = new EditorSessionRecord
        {
            TabId = tabId,
            DocumentText = legacy?.DocumentText ?? string.Empty,
            CursorOffset = legacy?.CursorOffset ?? 0,
            ActiveProfileId = legacy?.ActiveProfileId,
            SessionKey = legacy?.SessionKey ?? Guid.NewGuid().ToString("N"),
            DocumentName = name,
            IsModified = legacy?.IsModified ?? false,
            NextDefaultNumber = next,
            EngineSessionId = Guid.NewGuid().ToString("N"),
        };
        var set = new EditorTabSetRecord { Order = new() { tabId }, ActiveId = tabId, NextDefaultNumber = next };

        // The tab record first (no set yet, so nothing stops it), then the set, then the old record: a load stopped at any
        // point finds either no set (and migrates again from the old record) or the set and its tab, never a set without it.
        await store.SaveTabAsync(tabId, record).ConfigureAwait(false);
        await store.FlushAsync().ConfigureAwait(false);
        if (!await store.CreateSetAsync(set).ConfigureAwait(false))
        {
            // Another window moved the document first: its set and its tab 1 stand; this window's copy goes.
            await store.DeleteTabAsync(tabId).ConfigureAwait(false);
            return await store.RestoreSetAsync().ConfigureAwait(false) ?? set;
        }
        if (legacy != null) await store.ClearAsync().ConfigureAwait(false);
        return set;
    }
}

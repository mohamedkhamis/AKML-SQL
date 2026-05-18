// Spec 021 (web edition) -- M2 task T032. CodeMirror 6 wrapper exposing a Blazor-facing
// API. The Blazor EditorComponent.razor calls into this module via IJSRuntime; it does
// not know that the underlying editor is CM6 -- it could be swapped for Monaco or any
// other editor by replacing this file.
//
// CodeMirror is loaded lazily from the official ESM CDN. The release build switches to a
// vendored copy under wwwroot/lib/codemirror/ (T054 bundle-size audit will lock the
// version) by replacing the import URL with a relative path.

const CM_BASE = 'https://esm.sh/@codemirror';
let _cmModulesPromise = null;

function loadCm() {
    if (_cmModulesPromise) return _cmModulesPromise;
    _cmModulesPromise = Promise.all([
        import(`${CM_BASE}/state@6`),
        import(`${CM_BASE}/view@6`),
        import(`${CM_BASE}/commands@6`),
        import(`${CM_BASE}/language@6`),
        import(`${CM_BASE}/lang-sql@6`),
        import(`${CM_BASE}/autocomplete@6`),
        import(`${CM_BASE}/search@6`),
        import(`${CM_BASE}/lint@6`),
    ]).then(([state, view, commands, language, langSql, autocomplete, search, lint]) => ({
        state, view, commands, language, langSql, autocomplete, search, lint,
    }));
    return _cmModulesPromise;
}

// One editor instance per hosting element. Keyed by hostElementId.
const _instances = new Map();

/**
 * Create a CodeMirror editor inside the given host element.
 * @param {string} hostElementId  The DOM id of the container div.
 * @param {string} initialText    Initial document text.
 * @param {object} dotNetRef      Blazor DotNetObjectReference for OnTextChanged callback.
 * @returns {Promise<void>}
 */
export async function create(hostElementId, initialText, dotNetRef) {
    const host = document.getElementById(hostElementId);
    if (!host) {
        throw new Error(`akml-editor: host element '${hostElementId}' not found.`);
    }
    if (_instances.has(hostElementId)) {
        // Already created -- replace the content.
        await setText(hostElementId, initialText ?? '');
        return;
    }

    const cm = await loadCm();

    const updateListener = cm.view.EditorView.updateListener.of((update) => {
        if (update.docChanged && dotNetRef) {
            const text = update.state.doc.toString();
            // Fire-and-forget; the C# side debounces.
            dotNetRef.invokeMethodAsync('OnTextChangedFromJs', text);

            // T109 follow-up Issue 1: when the user types a space (or any
            // non-identifier char) immediately after a trigger keyword, CM's
            // activateOnTyping doesn't fire because the typed char isn't a word
            // char. Detect that case and manually open the popup so the user
            // doesn't have to press Ctrl+Space after "WHERE ", "AND ", etc.
            try {
                let typedNonWord = false;
                update.changes.iterChanges((_fromA, _toA, _fromB, _toB, inserted) => {
                    const ins = inserted.toString();
                    if (ins.length > 0 && /[\s.,()=<>!+\-*/]/.test(ins[ins.length - 1])) {
                        typedNonWord = true;
                    }
                });
                if (typedNonWord) {
                    const pos = update.state.selection.main.head;
                    const line = update.state.doc.lineAt(pos);
                    const lineUpToCaret = line.text.slice(0, pos - line.from);
                    if (POST_KEYWORD_TRIGGER.test(lineUpToCaret)) {
                        cm.autocomplete.startCompletion(update.view);
                    }
                }
            } catch { /* never let trigger detection break the editor */ }
        }
    });

    // T109 follow-up: route CodeMirror's autocomplete through ICompletionService
    // on the .NET side so the popup is fed by the bridge (online) or the cached
    // schema snapshot (offline). Returns null when the .NET side gives an empty
    // list -- CM hides the popup automatically in that case.
    //
    // Trigger contexts (each independently opens the popup):
    //   1. Caret sits on or right after an identifier (CM's "typing" flow):
    //      replace text starts at the word boundary, CM fuzzy-filters by prefix.
    //   2. Caret is right after whitespace following an SQL keyword that
    //      grammatically expects an expression next (WHERE / AND / OR / FROM /
    //      JOIN / ON / SET / HAVING / SELECT / GROUP BY / ORDER BY): show the
    //      full candidate list anchored at the caret, no replacement range.
    //      This is the case the user reported -- typing "... AND " (trailing
    //      space) should suggest columns / tables without forcing Ctrl+Space.
    //   3. context.explicit === true (Ctrl+Space) overrides everything.
    const POST_KEYWORD_TRIGGER = /\b(?:where|and|or|from|join|on|set|having|select|group\s+by|order\s+by|by|when|then|else|in)\s+$/i;

    const completionSource = async (context) => {
        if (!dotNetRef) return null;

        const word = context.matchBefore(/[\w]+/);
        const wordValid = word && (word.from !== word.to || context.explicit);

        // Detect "after a trigger keyword + whitespace" by looking at the line text
        // leading up to the caret. Cheaper than scanning the whole document.
        let postKeyword = false;
        if (!wordValid) {
            const lineUpToCaret = context.state.doc.lineAt(context.pos)
                .text.slice(0, context.pos - context.state.doc.lineAt(context.pos).from);
            postKeyword = POST_KEYWORD_TRIGGER.test(lineUpToCaret);
        }

        if (!wordValid && !postKeyword && !context.explicit) return null;

        try {
            const items = await dotNetRef.invokeMethodAsync('RequestCompletionsFromJs', context.pos);
            if (!items || items.length === 0) return null;
            return {
                from: wordValid ? word.from : context.pos,
                options: items.map(i => ({
                    label: i.label,
                    apply: i.insertText,
                    type: i.type || 'text',
                    detail: i.detail || undefined,
                })),
                // CM does its own fuzzy filter against the prefix at `from`. The
                // empty-prefix case (post-keyword trigger) re-invokes the source
                // as soon as the user types a non-word char, which is what we want.
                validFor: /^[\w]*$/,
            };
        } catch {
            // Never let a .NET interop crash kill the popup.
            return null;
        }
    };

    const state = cm.state.EditorState.create({
        doc: initialText ?? '',
        extensions: [
            cm.view.lineNumbers(),
            cm.view.highlightActiveLine(),
            cm.view.keymap.of([
                ...cm.commands.defaultKeymap,
                ...cm.commands.historyKeymap,
                ...cm.search.searchKeymap,
                cm.commands.indentWithTab,
            ]),
            cm.langSql.sql(),
            // Without this the SQL grammar parses but renders as plain text.
            // fallback:true is needed because we don't pair a CM6 editor theme yet.
            cm.language.syntaxHighlighting(cm.language.defaultHighlightStyle, { fallback: true }),
            cm.autocomplete.autocompletion({
                override: [completionSource],
                activateOnTyping: true,
                maxRenderedOptions: 50,
            }),
            cm.lint.lintGutter(),
            updateListener,
        ],
    });

    const view = new cm.view.EditorView({
        state,
        parent: host,
    });

    _instances.set(hostElementId, { view, cm, dotNetRef });
}

/** Return the current document text. */
export function getText(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return '';
    return inst.view.state.doc.toString();
}

/** Replace the entire document. */
export function setText(hostElementId, newText) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    inst.view.dispatch({
        changes: { from: 0, to: inst.view.state.doc.length, insert: newText ?? '' },
    });
}

/** Select the given (1-based) line and scroll it into view. */
export function gotoLine(hostElementId, lineNumber1Based) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const doc = inst.view.state.doc;
    const line = Math.max(1, Math.min(lineNumber1Based, doc.lines));
    const lineInfo = doc.line(line);
    // Select the whole line range so the user sees the offending span highlighted,
    // not just an invisible caret at column 0.
    inst.view.dispatch({
        selection: { anchor: lineInfo.from, head: lineInfo.to },
        effects: inst.cm.view.EditorView.scrollIntoView(lineInfo.from, { y: 'center' }),
    });
    inst.view.focus();
}

/** Replace the selection range with the cursor at offset = anchor. */
export function setSelection(hostElementId, anchor, head) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    inst.view.dispatch({ selection: { anchor, head } });
}

/** Dispose the editor and free the DotNetObjectReference. */
export function dispose(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    inst.view.destroy();
    if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
        inst.dotNetRef.dispose();
    }
    _instances.delete(hostElementId);
}

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
        }
    });

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
            cm.autocomplete.autocompletion(),
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

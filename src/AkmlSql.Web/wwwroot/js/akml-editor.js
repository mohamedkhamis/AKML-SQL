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
    //
    // PR #236 review: regex hoisted above updateListener (was previously below
    // the listener and worked only because the closure binding resolved at
    // first-firing time — a code smell that would break if the listener could
    // fire synchronously during view construction).
    // `in` intentionally omitted: typing "WHERE x IN " almost always wants a
    // subquery / value list, not a column name; the false-positive popup was
    // more noise than help.
    const POST_KEYWORD_TRIGGER = /\b(?:where|and|or|from|join|on|set|having|select|group\s+by|order\s+by|by|when|then|else)\s+$/i;

    const updateListener = cm.view.EditorView.updateListener.of((update) => {
        if (!update.docChanged || !dotNetRef) return;

        const text = update.state.doc.toString();
        // Fire-and-forget; the C# side debounces.
        dotNetRef.invokeMethodAsync('OnTextChangedFromJs', text);

        // PR #236 review: only consider user-typed transactions for the
        // post-keyword trigger. Programmatic dispatches (Format → setText,
        // refactoring previews, etc.) shouldn't pop the autocomplete just
        // because the formatted SQL happens to end with "WHERE foo = 1 AND ".
        // CM transactions carry a userEvent annotation when they originate
        // from user input ("input.type" / "input.paste" / "delete.*").
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;

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
    });

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
            // Pass the LIVE document text so the offline smart GROUP BY action parses what the
            // user is actually editing (the debounced session would be stale during fast typing).
            // The online path ignores it; it never reaches the engine wire.
            const items = await dotNetRef.invokeMethodAsync(
                'RequestCompletionsFromJs', context.pos, context.state.doc.toString());
            if (!items || items.length === 0) return null;
            return {
                from: wordValid ? word.from : context.pos,
                options: items.map(i => {
                    const type = i.type || 'text';
                    // Smart-action items (SQL-Prompt-style "▶ Add columns from SELECT") must sort
                    // to the top: CM6 orders an empty-prefix popup by label, and "▶" (U+25B6)
                    // sorts after letters, which would otherwise bury the action at the bottom.
                    const boost = (i.label && i.label.charCodeAt(0) === 0x25B6) ? 99 : undefined;
                    // Spec 027 T011: snippet items expand (with tab-stops) on accept instead
                    // of inserting their text literally. A function `apply` is CM6's accept
                    // hook; non-snippet items keep the plain-string `apply` (literal insert).
                    if (type === 'snippet') {
                        return {
                            label: i.label,
                            type,
                            detail: i.detail || undefined,
                            boost,
                            apply: (view, _completion, from, to) =>
                                applySnippetBody(cm, view, i.insertText, from, to),
                        };
                    }
                    return {
                        label: i.label,
                        apply: i.insertText,
                        type,
                        detail: i.detail || undefined,
                        boost,
                    };
                }),
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

    // ── Spec 028 (M6) US5 — inline ghost text (hand-rolled, no new package) ───────────────
    // Per-instance config; mutated by setGhostTextConfig from EditorComponent.
    const ghost = { enabled: false, delayMs: 350, timer: null, reqId: 0 };

    class GhostWidget extends cm.view.WidgetType {
        constructor(text) { super(); this.text = text; }
        eq(other) { return other.text === this.text; }
        toDOM() {
            const span = document.createElement('span');
            span.className = 'akml-ghost-text';
            span.textContent = this.text;
            span.style.opacity = '0.45';
            span.style.fontStyle = 'italic';
            span.style.pointerEvents = 'none';
            span.style.whiteSpace = 'pre';
            return span;
        }
    }

    const setGhost = cm.state.StateEffect.define();
    const ghostField = cm.state.StateField.define({
        create() { return cm.view.Decoration.none; },
        update(deco, tr) {
            if (tr.docChanged) deco = cm.view.Decoration.none;   // dismiss-on-type
            deco = deco.map(tr.changes);
            for (const e of tr.effects) {
                if (e.is(setGhost)) {
                    deco = e.value
                        ? cm.view.Decoration.set([
                            cm.view.Decoration.widget({ widget: new GhostWidget(e.value.text), side: 1 }).range(e.value.pos),
                          ])
                        : cm.view.Decoration.none;
                }
            }
            return deco;
        },
        provide: f => cm.view.EditorView.decorations.from(f),
    });

    function currentGhost(view) {
        const deco = view.state.field(ghostField, false);
        if (!deco || deco.size === 0) return null;
        let found = null;
        deco.between(0, view.state.doc.length, (from, _to, value) => {
            const w = value.widget;
            if (w && typeof w.text === 'string') found = { from, text: w.text };
        });
        return found;
    }

    function acceptGhost(view) {
        const g = currentGhost(view);
        if (!g) return false;   // fall through to autocomplete-accept / snippet-tab / indentWithTab
        view.dispatch({
            changes: { from: g.from, insert: g.text },
            selection: { anchor: g.from + g.text.length },
            effects: setGhost.of(null),
        });
        return true;
    }

    function dismissGhost(view) {
        if (!currentGhost(view)) return false;
        view.dispatch({ effects: setGhost.of(null) });
        return true;
    }

    function ghostShouldFire(state) {
        const pos = state.selection.main.head;
        try { if (cm.autocomplete.completionStatus(state) !== null) return false; } catch { /* ignore */ }
        const line = state.doc.lineAt(pos);
        if (line.text.trim().length === 0) return false;   // empty line
        if (pos !== line.to) return false;                 // only at end of line
        try {
            let node = cm.language.syntaxTree(state).resolveInner(pos, -1);
            while (node) {
                if (node.name === 'LineComment' || node.name === 'BlockComment' ||
                    node.name === 'String' || node.name === 'QuotedIdentifier') return false;
                node = node.parent;
            }
        } catch { /* tree not ready -> don't suppress on that basis */ }
        return true;
    }

    function requestGhost(view) {
        if (!ghost.enabled || !dotNetRef) return;
        if (!ghostShouldFire(view.state)) return;
        const pos = view.state.selection.main.head;
        const docText = view.state.doc.toString();
        const reqId = ++ghost.reqId;
        dotNetRef.invokeMethodAsync('RequestGhostTextFromJs', pos, docText).then(suggestion => {
            if (reqId !== ghost.reqId || !suggestion) return;      // superseded or empty
            const inst = _instances.get(hostElementId);
            if (!inst) return;
            if (inst.view.state.selection.main.head !== pos) return; // staleness check
            inst.view.dispatch({ effects: setGhost.of({ text: suggestion, pos }) });
        }).catch(() => { /* ghost text never surfaces an error */ });
    }

    const ghostListener = cm.view.EditorView.updateListener.of((update) => {
        if (!ghost.enabled || !update.docChanged) return;
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;
        if (ghost.timer) clearTimeout(ghost.timer);
        ghost.timer = setTimeout(() => requestGhost(update.view), ghost.delayMs);
    });

    // Prec.highest so Tab/Escape are consulted first; the handlers return false (fall through)
    // unless a ghost suggestion is actually showing, leaving autocomplete + snippet Tab intact.
    const ghostKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: acceptGhost },
        { key: 'Escape', run: dismissGhost },
    ]));

    const state = cm.state.EditorState.create({
        doc: initialText ?? '',
        extensions: [
            cm.view.lineNumbers(),
            cm.view.highlightActiveLine(),
            ghostKeymap,
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
            ghostField,
            ghostListener,
            updateListener,
        ],
    });

    const view = new cm.view.EditorView({
        state,
        parent: host,
    });

    _instances.set(hostElementId, { view, cm, dotNetRef, ghost });
}

/**
 * Spec 028 (M6) US5 — enable/disable inline ghost text and set its debounce. Called from
 * EditorComponent after create() with the persisted AiFeatureSettings.
 */
export function setGhostTextConfig(hostElementId, enabled, delayMs) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.ghost) return;
    inst.ghost.enabled = !!enabled;
    if (typeof delayMs === 'number' && delayMs > 0) inst.ghost.delayMs = delayMs;
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

/**
 * Spec 025 (M3 bridge closure) US4 — insert text at the current caret. Used by
 * SchemaTreeComponent's click-to-insert: clicking [dbo].[Customer] drops that
 * literal at the caret as-typed; CodeMirror's standard Ctrl+Z undoes it.
 */
export function insertAtCaret(hostElementId, text) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof text !== 'string' || text.length === 0) return;
    const head = inst.view.state.selection.main.head;
    inst.view.dispatch({
        changes: { from: head, insert: text },
        selection: { anchor: head + text.length },
    });
    inst.view.focus();
}

// ── Spec 027 (M5 offline closure) T010/T011 — snippet expansion + surround-with ──────────
//
// Snippet bodies are authored in the ENGINE-NATIVE placeholder syntax (so a web-authored
// .akmlsnippet ALSO expands on the SSMS/WPF surface — FR-006): a tab-stop is `$Name$`
// (must start with a letter/underscore), the final caret is `$CURSOR$`, and the selection
// slot for surround-with is `$SELECTEDTEXT$`. These helpers translate that into CodeMirror 6's
// `${...}` snippet template at expand time, falling back to a literal insertion if CM6's
// snippet() is unavailable (the function is feature-detected — it cannot be verified headless).

// Engine-native named token, e.g. `$table$` / `$CURSOR$` (AkmlSql.Engine PlaceholderParser:
// regex \$([A-Za-z_]\w*)\$). Numbered `$1`/`$2` tokens (the shared SnippetProvider dialect)
// are handled separately — both normalise to CM6 `${...}` fields so the one expander serves
// every in-repo snippet source.
const SNIPPET_NAMED = /\$([A-Za-z_]\w*)\$/g;
const SNIPPET_NUMBERED = /\$(\d+)/g;

/**
 * Translate a snippet body into a CodeMirror 6 snippet template.
 * `$Name$`   -> `${Name}`  (CM6 tab-stop; repeated names link, matching the engine)
 * `$CURSOR$` -> `${}`       (CM6 final cursor stop)
 * `$1`,`$2`  -> `${1}`,`${2}` (the SnippetProvider numbered-stop dialect)
 * Escapes any pre-existing `${` so literal text is not mis-read as a CM6 field.
 */
function toCmTemplate(body) {
    return String(body)
        .replace(/\$\{/g, '\\${')                          // protect literal ${ in user text
        .replace(SNIPPET_NAMED, (_m, name) =>
            name.toUpperCase() === 'CURSOR' ? '${}' : '${' + name + '}')
        .replace(SNIPPET_NUMBERED, (_m, n) => '${' + n + '}');
}

/**
 * Strip snippet tokens to plain text for the no-CM6-snippet fallback. Named stops become
 * their name, numbered stops vanish, `$CURSOR$` is removed. The caret lands at end of the
 * inserted text (an exact mid-body caret is not worth reconstructing for the rare fallback).
 */
function toLiteral(body) {
    return String(body)
        .replace(SNIPPET_NAMED, (_m, name) => name.toUpperCase() === 'CURSOR' ? '' : name)
        .replace(SNIPPET_NUMBERED, '');
}

function applySnippetBody(cm, view, body, from, to) {
    if (typeof body !== 'string' || body.length === 0) return;
    const snippetFn = cm.autocomplete && cm.autocomplete.snippet;
    if (typeof snippetFn === 'function') {
        try {
            // CM6: snippet(template) -> (view, completion, from, to) => void
            snippetFn(toCmTemplate(body))(view, null, from, to);
            view.focus();
            return;
        } catch { /* fall through to literal */ }
    }
    const text = toLiteral(body);
    view.dispatch({
        changes: { from, to, insert: text },
        selection: { anchor: from + text.length },
    });
    view.focus();
}

/**
 * Spec 027 T010 — expand a snippet body at the caret (replacing any current selection),
 * driving CM6 tab-stops. Body is engine-native (`$Name$` / `$CURSOR$`).
 */
export function expandSnippet(hostElementId, body) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const sel = inst.view.state.selection.main;
    applySnippetBody(inst.cm, inst.view, body, sel.from, sel.to);
}

/**
 * Spec 027 T010 — surround the current selection: the snippet body's `$SELECTEDTEXT$` token
 * is replaced by the selected text, then the body expands as a normal snippet at the
 * selection range. With no selection, `$SELECTEDTEXT$` resolves to empty (caret lands there).
 */
export function surroundSelection(hostElementId, body) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof body !== 'string') return;
    const sel = inst.view.state.selection.main;
    const selected = inst.view.state.sliceDoc(sel.from, sel.to);
    // Substitute the selection slot first (case-insensitive, engine-native token), then let
    // the standard snippet path handle any remaining $Name$ / $CURSOR$ tab-stops.
    const resolved = body.replace(/\$SELECTEDTEXT\$/gi, () => selected);
    applySnippetBody(inst.cm, inst.view, resolved, sel.from, sel.to);
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

/**
 * Spec 027 T025 (US4) — append text at the end of a 1-based line. Used by the suppression
 * "this line" action to insert ` -- noqa: RULEID` at the finding's line end (matching the
 * WPF FixAction). Single dispatch ⇒ one undoable edit. Returns the resulting full text via
 * the caller's getText if needed; here we just mutate.
 */
export function insertAtLineEnd(hostElementId, lineNumber1Based, text) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof text !== 'string' || text.length === 0) return;
    const doc = inst.view.state.doc;
    const line = Math.max(1, Math.min(lineNumber1Based, doc.lines));
    const lineInfo = doc.line(line);
    inst.view.dispatch({ changes: { from: lineInfo.to, insert: text } });
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
    // Spec 028 (M6) US5: cancel any pending ghost-text debounce so it can't fire requestGhost
    // on a destroyed view / disposed DotNetObjectReference, and supersede any in-flight response.
    if (inst.ghost) {
        if (inst.ghost.timer) { clearTimeout(inst.ghost.timer); inst.ghost.timer = null; }
        inst.ghost.reqId++;
    }
    inst.view.destroy();
    if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
        inst.dotNetRef.dispose();
    }
    _instances.delete(hostElementId);
}

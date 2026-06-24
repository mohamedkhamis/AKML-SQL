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
        import('https://esm.sh/@lezer/highlight@1'),   // `tags` for the token-driven syntax theme
    ]).then(([state, view, commands, language, langSql, autocomplete, search, lint, highlight]) => ({
        state, view, commands, language, langSql, autocomplete, search, lint, highlight,
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

    // Spec 030 web redesign — token-driven SQL syntax theme. Colours resolve from the --akml-syntax-*
    // CSS custom properties (generated from docs/theme-tokens.json), so the editor follows the active
    // light/dark/HC theme automatically instead of CM6's hardcoded defaultHighlightStyle.
    const t = cm.highlight.tags;
    const akmlHighlightStyle = cm.language.HighlightStyle.define([
        { tag: t.keyword, color: 'var(--akml-syntax-keyword)' },
        { tag: [t.string, t.special(t.string), t.character], color: 'var(--akml-syntax-string)' },
        { tag: [t.number, t.bool, t.null], color: 'var(--akml-syntax-number)' },
        { tag: [t.comment, t.lineComment, t.blockComment], color: 'var(--akml-syntax-comment)', fontStyle: 'italic' },
        { tag: [t.function(t.variableName), t.function(t.propertyName), t.standard(t.name)], color: 'var(--akml-syntax-function)' },
        { tag: [t.typeName, t.propertyName, t.className, t.attributeName], color: 'var(--akml-syntax-tablecolumn)' },
    ]);

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
                    // Map the engine's SortPriority (ASCENDING — lower ranks first) to CM6's
                    // boost (DESCENDING — higher ranks first) so the web popup order matches the
                    // desktop (e.g. data types before constraint keywords in a CREATE TABLE column).
                    // ▶ smart-actions keep the top slot. The /10 scale keeps boost a gentle
                    // tiebreaker: it fully orders the empty-prefix popup but does not override CM's
                    // fuzzy-match score once a prefix is typed (mirrors the engine: score, then
                    // SortPriority).
                    const boost = (i.label && i.label.charCodeAt(0) === 0x25B6)
                        ? 99
                        : (typeof i.sortPriority === 'number' ? (100 - i.sortPriority) / 10 : undefined);
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

    // ── Spec 030 (web Phase 1) — Quick Info on hover ─────────────────────────────────────────
    // CM6 hoverTooltip extension: asks the .NET IQuickInfoService for the object/column under the
    // hovered position and renders a small themed tooltip. Returns null = no tooltip.
    const quickInfoHover = cm.view.hoverTooltip(async (view, pos) => {
        if (!dotNetRef) return null;
        let info;
        try { info = await dotNetRef.invokeMethodAsync('RequestQuickInfoFromJs', pos, view.state.doc.toString()); }
        catch { return null; }
        if (!info) return null;
        const wr = view.state.wordAt(pos);
        const from = wr ? wr.from : pos;
        const to = wr ? wr.to : pos;
        return {
            pos: from, end: to, above: true,
            create() {
                const dom = document.createElement('div');
                dom.className = 'akml-qi-tooltip';
                const header = document.createElement('div');
                header.className = 'akml-qi-header';
                header.textContent = (info.objectType ? `[${info.objectType}] ` : '') + (info.header || '');
                dom.appendChild(header);
                if (info.description) {
                    const desc = document.createElement('div');
                    desc.className = 'akml-qi-desc';
                    desc.textContent = info.description;
                    dom.appendChild(desc);
                }
                if (info.details && info.details.length) {
                    const list = document.createElement('div');
                    list.className = 'akml-qi-details';
                    for (const d of info.details) {
                        const row = document.createElement('div');
                        row.textContent = d.value ? `${d.label}: ${d.value}` : d.label;
                        list.appendChild(row);
                    }
                    dom.appendChild(list);
                }
                return { dom };
            },
        };
    }, { hoverTime: 350 });

    // ── Spec 030 (web Phase 1) — Signature help (parameter hints) ─────────────────────────────
    // A single themed tooltip driven by a StateField, shown when the caret is inside a proc/function
    // call. Re-requested when the user types '(' or ',' (or types further while one is showing);
    // dismissed on ')' / Escape / when the service reports no call site.
    const setSig = cm.state.StateEffect.define();
    const sigField = cm.state.StateField.define({
        create() { return null; },
        update(value, tr) {
            for (const e of tr.effects) if (e.is(setSig)) value = e.value;
            return value;
        },
        provide: f => cm.view.showTooltip.from(f),
    });

    // Monotonic request sequence: each requestSignature claims the next value; its async response
    // is applied only if no newer request OR dismissal happened meanwhile — otherwise a slow
    // round-trip could re-show a stale tooltip after the user already typed ')' / moved on.
    let sigSeq = 0;

    async function requestSignature(view) {
        if (!dotNetRef) return;
        const seq = ++sigSeq;
        const pos = view.state.selection.main.head;
        let sig;
        try { sig = await dotNetRef.invokeMethodAsync('RequestSignatureHelpFromJs', pos, view.state.doc.toString()); }
        catch { return; }
        if (seq !== sigSeq) return;   // superseded by a newer request or a dismissal
        const inst = _instances.get(hostElementId);
        if (!inst) return;
        if (!sig) { inst.view.dispatch({ effects: setSig.of(null) }); return; }
        const tooltip = {
            pos: inst.view.state.selection.main.head,
            above: true,
            create() {
                const dom = document.createElement('div');
                dom.className = 'akml-sig-tooltip';
                const label = document.createElement('div');
                label.className = 'akml-sig-label';
                label.textContent = sig.label || '';
                dom.appendChild(label);
                if (sig.parameters && sig.parameters.length) {
                    const ap = Math.max(0, Math.min(sig.activeParameter, sig.parameters.length - 1));
                    const p = sig.parameters[ap];
                    const active = document.createElement('div');
                    active.className = 'akml-sig-active';
                    active.textContent = (p.name || '') + (p.type ? ' ' + p.type : '');
                    dom.appendChild(active);
                }
                if (sig.documentation) {
                    const doc = document.createElement('div');
                    doc.className = 'akml-sig-doc';
                    doc.textContent = sig.documentation;
                    dom.appendChild(doc);
                }
                return { dom };
            },
        };
        inst.view.dispatch({ effects: setSig.of(tooltip) });
    }

    function dismissSignature(view) {
        sigSeq++;   // invalidate any in-flight requestSignature so its stale response is dropped
        if (view.state.field(sigField, false)) { view.dispatch({ effects: setSig.of(null) }); return true; }
        return false;
    }

    const signatureListener = cm.view.EditorView.updateListener.of((update) => {
        if (!update.docChanged || !dotNetRef) return;
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;
        let lastChar = '';
        update.changes.iterChanges((_fa, _ta, _fb, _tb, inserted) => {
            const s = inserted.toString();
            if (s.length > 0) lastChar = s[s.length - 1];
        });
        if (lastChar === ')') { dismissSignature(update.view); return; }
        const showing = update.state.field(sigField, false) != null;
        if (lastChar === '(' || lastChar === ',' || showing) {
            requestSignature(update.view);
        }
    });

    // ── Spec 030 (web Phase 1) — Go to definition (F12) → peek panel ──────────────────────────
    function removePeekPanel(host) {
        if (host && host._akmlPeek) { host._akmlPeek.remove(); host._akmlPeek = null; }
    }

    function showPeekPanel(host, def) {
        removePeekPanel(host);
        const panel = document.createElement('div');
        panel.className = 'akml-peek-panel';
        const header = document.createElement('div');
        header.className = 'akml-peek-header';
        const title = document.createElement('span');
        title.textContent = (def.found
            ? ((def.objectType ? def.objectType + ': ' : '') + (def.fullName || 'Definition'))
            : (def.message || 'No definition found.'));
        const close = document.createElement('button');
        close.className = 'akml-peek-close';
        close.type = 'button';
        close.textContent = '✕';
        close.onclick = () => removePeekPanel(host);
        header.appendChild(title);
        header.appendChild(close);
        panel.appendChild(header);
        if (def.found && def.definition) {
            const pre = document.createElement('pre');
            pre.className = 'akml-peek-body';
            pre.textContent = def.definition;
            panel.appendChild(pre);
        }
        host.appendChild(panel);
        host._akmlPeek = panel;
    }

    function gotoDefinition(view) {
        if (!dotNetRef) return true;
        const pos = view.state.selection.main.head;
        dotNetRef.invokeMethodAsync('RequestGoToDefinitionFromJs', pos, view.state.doc.toString())
            .then(def => {
                const host = document.getElementById(hostElementId);
                if (host && def) showPeekPanel(host, def);
            })
            .catch(() => { /* go-to-def never surfaces an error into typing */ });
        return true;   // handled (the work is async)
    }

    // Spec 030 — Phase 5: Ctrl/Cmd+Enter runs the query. Registered INSIDE the CM6 keymap (not just
    // the Blazor page handler) because CM6 has focus and swallows Mod-Enter. The Blazor side surfaces
    // it via EditorComponent.ExecuteFromJs → OnExecute → Editor.ExecuteAsync. F5 is deliberately NOT
    // bound (browser refresh) — the toolbar Execute button covers that muscle memory.
    function runExecute(view) {
        if (!dotNetRef) return false;
        dotNetRef.invokeMethodAsync('ExecuteFromJs')
            .catch(() => { /* execution errors surface in the results pane, never into the editor */ });
        return true;   // handled (the work is async); stops CM inserting a newline.
    }

    const navKeymap = cm.view.keymap.of([
        { key: 'F12', run: gotoDefinition },
        { key: 'Escape', run: dismissSignature },   // also clears a stray signature tooltip
        { key: 'Mod-Enter', run: runExecute },      // Spec 030 Phase 5 — Execute query.
    ]);

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
            // Token-driven SQL syntax theme (--akml-syntax-*). fallback:true keeps any tag we didn't
            // map rendering via CM6's default so nothing goes uncoloured.
            cm.language.syntaxHighlighting(akmlHighlightStyle, { fallback: true }),
            cm.language.syntaxHighlighting(cm.language.defaultHighlightStyle, { fallback: true }),
            cm.autocomplete.autocompletion({
                override: [completionSource],
                activateOnTyping: true,
                maxRenderedOptions: 50,
            }),
            cm.lint.lintGutter(),
            ghostField,
            ghostListener,
            // Spec 030 (web Phase 1) — IntelliSense parity: hover quick-info, signature help, F12 goto.
            quickInfoHover,
            sigField,
            signatureListener,
            navKeymap,
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

/**
 * Spec 028 (M6) — return the currently selected text (empty string when there is no
 * selection). The AI dock (Editor.razor) feeds this to the action panel so Explain / Fix /
 * etc. operate on the user's selection, falling back to the whole document when empty.
 */
export function getSelectedText(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return '';
    const sel = inst.view.state.selection.main;
    return inst.view.state.sliceDoc(sel.from, sel.to);
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
    const host = document.getElementById(hostElementId);
    if (host && host._akmlPeek) { host._akmlPeek.remove(); host._akmlPeek = null; }
    inst.view.destroy();
    if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
        inst.dotNetRef.dispose();
    }
    _instances.delete(hostElementId);
}

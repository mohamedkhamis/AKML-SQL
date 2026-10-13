// Spec 021 (web edition) -- M2 task T032. CodeMirror 6 wrapper exposing a Blazor-facing
// API. The Blazor EditorComponent.razor calls into this module via IJSRuntime; it does
// not know that the underlying editor is CM6 -- it could be swapped for Monaco or any
// other editor by replacing this file.
//
// CodeMirror 6 is loaded from a locally-vendored bundle (wwwroot/lib/codemirror/akml-cm.js),
// NOT a CDN. This keeps the web edition working on-prem / offline / behind a strict CSP — the
// esm.sh CDN was unreliable (500s / firewall blocks) and required loosening script-src.
// The bundle is produced by src/AkmlSql.Web/tools/codemirror (npm install && npm run build); it
// re-exports each package as a namespace matching the shape destructured below. It MUST stay one
// bundle so @codemirror/state is a single shared instance (per-package files would break facets).
// Resolved relative to this module's URL (/js/akml-editor.js -> /lib/codemirror/akml-cm.js) so it
// is independent of the app's <base href>.
const CM_BUNDLE_URL = new URL('../lib/codemirror/akml-cm.js', import.meta.url).href;
let _cmModulesPromise = null;

function loadCm() {
    if (_cmModulesPromise) return _cmModulesPromise;
    _cmModulesPromise = import(CM_BUNDLE_URL).then(m => ({
        state: m.state,
        view: m.view,
        commands: m.commands,
        language: m.language,
        langSql: m.langSql,
        autocomplete: m.autocomplete,
        search: m.search,
        lint: m.lint,
        highlight: m.highlight,   // `tags` for the token-driven syntax theme
    }));
    return _cmModulesPromise;
}

// One editor instance per hosting element. Keyed by hostElementId.
const _instances = new Map();

/**
 * Spec 041 (R65) — pure: the 1-based line and column of offset `head` in a CodeMirror Text, as the status bar
 * shows them. The column counts characters (a tab is one), as SSMS's "Col" does.
 */
export function caretPosition(doc, head) {
    const at = Math.max(0, Math.min(head, doc.length));
    const line = doc.lineAt(at);
    return { line: line.number, column: at - line.from + 1 };
}

// ── Spec 041 (research R53, FR-053) — editor options ─────────────────────────────────────────────────────

/** The editor options within their limits (Settings › Editor): font size 9–32 px, tab size 1–8. */
export function normaliseEditorOptions(opts) {
    const o = opts || {};
    const int = (v, d) => (typeof v === 'number' && Number.isFinite(v) ? Math.round(v) : d);
    return {
        fontSize: Math.min(32, Math.max(9, int(o.fontSize, 13))),
        wordWrap: !!o.wordWrap,
        tabSize: Math.min(8, Math.max(1, int(o.tabSize, 4))),
    };
}

/** The CodeMirror extensions for the options, one per compartment (pure; `cm` is the bundle). */
export function optionExtensions(cm, opts) {
    const o = normaliseEditorOptions(opts);
    return {
        wrap: o.wordWrap ? cm.view.EditorView.lineWrapping : [],
        tab: [cm.state.EditorState.tabSize.of(o.tabSize), cm.language.indentUnit.of(' '.repeat(o.tabSize))],
    };
}

/** The font size is CSS only: a variable on the host the editor's font-size reads. */
function applyFontSize(host, opts) {
    if (host && host.style) host.style.setProperty('--akml-editor-font-size', `${normaliseEditorOptions(opts).fontSize}px`);
}

/**
 * Create a CodeMirror editor inside the given host element.
 * @param {string} hostElementId  The DOM id of the container div.
 * @param {string} initialText    Initial document text.
 * @param {object} dotNetRef      Blazor DotNetObjectReference for OnTextChanged callback.
 * @param {{fontSize?:number, wordWrap?:boolean, tabSize?:number}} [options]  Spec 041 — the editor options.
 * @returns {Promise<void>}
 */
export async function create(hostElementId, initialText, dotNetRef, options) {
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
    //      JOIN / ON / SET / HAVING / SELECT / GROUP BY / ORDER BY, and — spec
    //      032 FR-002 — the DML verbs UPDATE / INSERT [INTO] / DELETE [FROM] /
    //      EXEC(UTE)): show the full candidate list anchored at the caret, no
    //      replacement range. This is the case the user reported -- typing
    //      "... AND " (trailing space) should suggest columns / tables without
    //      forcing Ctrl+Space.
    //   3. Caret is right after a member-access dot (`o.` / `dbo.` / `[Sales].` /
    //      `"dbo".`) — spec 032 FR-001: the single most common completion gesture.
    //      The leading char class requires an identifier START (letter/_/]/"),
    //      so numeric literals (`3.`) never trigger.
    //   4. context.explicit === true (Ctrl+Space) overrides everything.
    //
    // PR #236 review: regex hoisted above updateListener (was previously below
    // the listener and worked only because the closure binding resolved at
    // first-firing time — a code smell that would break if the listener could
    // fire synchronously during view construction).
    // `in` intentionally omitted: typing "WHERE x IN " almost always wants a
    // subquery / value list, not a column name; the false-positive popup was
    // more noise than help.
    const POST_KEYWORD_TRIGGER = /\b(?:where|and|or|from|join|on|set|having|select|group\s+by|order\s+by|by|when|then|else|update|insert(?:\s+into)?|into|delete(?:\s+from)?|exec(?:ute)?)\s+$/i;
    const DOT_MEMBER_TRIGGER = /(?:[A-Za-z_\]"][\w$]*|\]|")\.$/;

    // Spec 041 (R65, FR-083): the caret for the status bar ("Ln 1, Col 12"), throttled to one report per 100 ms.
    let caretTimer = 0;
    let caretState = null;
    const reportCaret = (state) => {
        caretState = state;
        if (caretTimer) return;
        caretTimer = setTimeout(() => {
            caretTimer = 0;
            const pos = caretPosition(caretState.doc, caretState.selection.main.head);
            dotNetRef.invokeMethodAsync('OnSelectionChangedFromJs', pos.line, pos.column).catch(() => { /* page gone */ });
        }, 100);
    };

    const updateListener = cm.view.EditorView.updateListener.of((update) => {
        if (dotNetRef && (update.selectionSet || update.docChanged)) reportCaret(update.state);
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
                // Spec 032 FR-001: '.' is a non-word char, so activateOnTyping never fires
                // for member access — this manual open is the ONLY dot-trigger path.
                if (POST_KEYWORD_TRIGGER.test(lineUpToCaret) || DOT_MEMBER_TRIGGER.test(lineUpToCaret)) {
                    cm.autocomplete.startCompletion(update.view);
                }
            }
        } catch { /* never let trigger detection break the editor */ }
    });

    const completionSource = async (context) => {
        if (!dotNetRef) return null;

        // Spec 032 FR-005 (C5): include '@'/'#' in the replace span so accepting
        // '@CustomerID' over a typed '@C' (or '#t' over '#') replaces the whole
        // sigil token instead of doubling it ('@@CustomerID').
        const word = context.matchBefore(/[@#\w]+/);
        const wordValid = word && (word.from !== word.to || context.explicit);

        // Detect "after a trigger keyword + whitespace" or "after a member-access
        // dot" (spec 032 FR-001) by looking at the line text leading up to the
        // caret. Cheaper than scanning the whole document.
        let postKeyword = false;
        let dotMember = false;
        if (!wordValid) {
            const lineUpToCaret = context.state.doc.lineAt(context.pos)
                .text.slice(0, context.pos - context.state.doc.lineAt(context.pos).from);
            postKeyword = POST_KEYWORD_TRIGGER.test(lineUpToCaret);
            dotMember = DOT_MEMBER_TRIGGER.test(lineUpToCaret);
        }

        if (!wordValid && !postKeyword && !dotMember && !context.explicit) return null;

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
                    // A bracketed name ("[Order Details]") must not double a bracket the user has
                    // already typed. Plain-string apply replaces only the word CM matched, which
                    // for `[Ord` is "Ord" — leaving the typed "[" in place and producing
                    // "[[Order Details]". applyBracketed widens the replacement to take in an
                    // open "[" (and an auto-closed "]") so the name goes in exactly once.
                    const insertText = i.insertText;
                    return {
                        label: i.label,
                        apply: (typeof insertText === 'string' && insertText.includes('['))
                            ? (view, _completion, from, to) => applyBracketed(view, insertText, from, to)
                            : insertText,
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

    // ── Spec 030 — expand "SELECT *" on Tab (SQL Prompt parity) ──────────────────────────────────
    // When the caret sits right after a wildcard ('*' or 'alias.*') inside a SELECT, Tab asks the
    // engine (via .NET ExpandWildcardFromJs) for the FROM tables' columns and replaces the wildcard
    // with the column list. Returns false (falls through to accept-completion / snippet / indent)
    // whenever it isn't a wildcard, so ordinary Tab is unaffected.
    function detectWildcardAtCaret(state) {
        const sel = state.selection.main;
        if (!sel.empty) return null;                                  // bare caret only, not a range
        const pos = sel.head;
        if (pos === 0) return null;
        if (state.doc.sliceString(pos - 1, pos) !== '*') return null; // caret must be just after '*'
        // Cheap SELECT-context gate (the engine still validates the actual star node): a SELECT must
        // appear before the caret in the current statement (nothing after the last ';' rules it out).
        const head = state.doc.sliceString(0, pos);
        const stmt = head.slice(head.lastIndexOf(';') + 1);
        if (!/\bSELECT\b/i.test(stmt)) return null;
        return { caretOffset: pos };
    }

    function expandWildcard(view) {
        if (!dotNetRef) return false;
        // An open completion popup owns Tab (accept the highlighted item) — don't steal it.
        if (cm.autocomplete.completionStatus(view.state) === 'active') return false;
        if (!detectWildcardAtCaret(view.state)) return false;         // not a wildcard → normal Tab

        const pos = view.state.selection.main.head;
        // Snapshot the keypress-time document (CM6 Text is immutable, so this is a cheap reference).
        // The engine computes spanStart/spanLength against THIS text; the guard below re-verifies the
        // span against the live document after the async round-trip.
        const docAtRequest = view.state.doc;
        dotNetRef.invokeMethodAsync('ExpandWildcardFromJs', pos, docAtRequest.toString())
            .then(res => {
                if (!res || !res.ok) return;
                const inst = _instances.get(hostElementId);
                if (!inst) return;
                const end = res.spanStart + res.spanLength;
                if (end > docAtRequest.length) return;                // engine span outside its own snapshot
                const doc = inst.view.state.doc;
                if (end > doc.length) return;                         // document shrank under us
                // Stale-document guard: the expansion replaces the exact text the engine saw at
                // [spanStart, end). If the user typed during the round-trip and that span no longer
                // matches, dispatching would clobber their edit — abort silently instead.
                if (doc.sliceString(res.spanStart, end) !== docAtRequest.sliceString(res.spanStart, end)) return;
                inst.view.dispatch({
                    changes: { from: res.spanStart, to: end, insert: res.text },
                    selection: { anchor: res.spanStart + res.text.length },
                    userEvent: 'input.complete',
                });
            })
            .catch(() => { /* best-effort — a failed expansion just leaves the '*' in place */ });
        return true;   // handled — swallow the Tab (the expansion is applied asynchronously)
    }

    const wildcardKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: expandWildcard },
    ]));

    // Spec 032 (FR-003, campaign finding 3) — Tab accepts the highlighted completion. CM6's
    // completion keymap deliberately ships NO Tab binding, so without this Tab fell through to
    // indentWithTab while the popup was open. acceptCompletion returns false when no completion
    // is active, preserving the chain: ghost-accept → completion-accept → wildcard-expand → indent.
    // Registered between ghostKeymap and wildcardKeymap in the extensions list (same Prec class;
    // earlier registration wins).
    const completionAcceptKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: cm.autocomplete.acceptCompletion },
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
    // it via EditorComponent.ExecuteFromJs → OnExecute → Editor.ExecuteAsync. F5 is NOT bound here:
    // since spec 041 (R37) the editor page claims F5, Ctrl+F5, Ctrl+R and the Ctrl+K chords in
    // akml-workspace.js's document-level capture listener, before CodeMirror or the browser see them.
    function runExecute(view) {
        if (!dotNetRef) return false;
        dotNetRef.invokeMethodAsync('ExecuteFromJs')
            .catch(() => { /* execution errors surface in the results pane, never into the editor */ });
        return true;   // handled (the work is async); stops CM inserting a newline.
    }

    const navKeymap = cm.view.keymap.of([
        { key: 'F12', run: gotoDefinition },
        { key: 'Escape', run: dismissSignature },   // also clears a stray signature tooltip
        // Mod-Enter moved into the main keymap AHEAD of defaultKeymap (spec 032 FR-004):
        // registered here (after the defaultKeymap spread) it was shadowed by defaultKeymap's
        // own Mod-Enter → insertBlankLine, leaving no keyboard execute at all (campaign finding 4).
    ]);

    // Spec 041 (R53): word wrap and tab size live in compartments, so Settings › Editor reconfigures the open
    // editor (setEditorOptions) instead of recreating it.
    const wrapCompartment = new cm.state.Compartment();
    const tabCompartment = new cm.state.Compartment();
    const optionExt = optionExtensions(cm, options);
    applyFontSize(host, options);

    const state = cm.state.EditorState.create({
        doc: initialText ?? '',
        extensions: [
            wrapCompartment.of(optionExt.wrap),
            tabCompartment.of(optionExt.tab),
            cm.view.lineNumbers(),
            cm.view.highlightActiveLine(),
            ghostKeymap,
            completionAcceptKeymap,   // Spec 032 FR-003 — Tab accepts an open completion (falls through when closed)
            wildcardKeymap,   // Tab-expand SELECT * (falls through when not a wildcard)
            cm.view.keymap.of([
                // Spec 032 FR-004 — must precede defaultKeymap's own Mod-Enter (insertBlankLine),
                // which otherwise shadows it: this is the only working keyboard execute.
                { key: 'Mod-Enter', run: runExecute },
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

    const disposeLintKeyboard = installLintKeyboard(view, hostElementId);
    _instances.set(hostElementId, { view, cm, dotNetRef, ghost, wrapCompartment, tabCompartment, disposeLintKeyboard });
}

// CodeMirror's lint gutter opens its own tooltip on mouseover. Give its markers the same
// interaction from the keyboard, and let its existing mouse-leave logic dismiss it on Escape.
function installLintKeyboard(view, hostElementId) {
    if (typeof MutationObserver === 'undefined' || !view.dom) return () => {};
    let focused = null;
    const close = () => {
        focused?.dispatchEvent(new MouseEvent('mouseout'));
        focused?.removeAttribute('aria-describedby');
        window.dispatchEvent(new MouseEvent('mousemove', { clientX: -100, clientY: -100 }));
    };
    const decorate = () => {
        for (const marker of view.dom.querySelectorAll('.cm-lint-marker')) {
            marker.tabIndex = 0;
            marker.setAttribute('aria-label', 'SQL diagnostics');
        }
        const tip = view.dom.querySelector('.cm-tooltip-lint');
        if (focused && tip) {
            tip.id = `${hostElementId}-lint-help`;
            tip.setAttribute('role', 'tooltip');
            focused.setAttribute('aria-describedby', tip.id);
        }
    };
    const focus = e => {
        if (!e.target.matches('.cm-lint-marker')) return;
        focused = e.target;
        focused.dispatchEvent(new MouseEvent('mouseover'));
    };
    const blur = () => { close(); focused = null; };
    const key = e => { if (e.key === 'Escape') close(); };
    const observer = new MutationObserver(decorate);
    observer.observe(view.dom, { childList: true, subtree: true });
    view.dom.addEventListener('focusin', focus);
    view.dom.addEventListener('focusout', blur);
    view.dom.addEventListener('keydown', key);
    return () => {
        close(); observer.disconnect();
        view.dom.removeEventListener('focusin', focus);
        view.dom.removeEventListener('focusout', blur);
        view.dom.removeEventListener('keydown', key);
    };
}

/** Spec 041 (R53) — apply changed editor options to an open editor. */
export function setEditorOptions(hostElementId, options) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const ext = optionExtensions(inst.cm, options);
    inst.view.dispatch({ effects: [inst.wrapCompartment.reconfigure(ext.wrap), inst.tabCompartment.reconfigure(ext.tab)] });
    applyFontSize(document.getElementById(hostElementId), options);
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

/**
 * Spec 041 (R24) — pure: the 1-based document line the executed text starts on. Execute runs the
 * selection, or the whole document when the selection is empty or only whitespace — then line 1.
 * @param {{lineAt:(pos:number)=>{number:number}, sliceString:(from:number,to:number)=>string}} doc  a CM Text
 * @param {{from:number, to:number}} range  the main selection
 */
export function selectionStartLine(doc, range) {
    if (!range || range.to <= range.from) return 1;
    if (doc.sliceString(range.from, range.to).trim().length === 0) return 1;
    return doc.lineAt(range.from).number;
}

/**
 * Spec 041 (R24) — the selected text and the line it starts on, read together so an error's server line
 * can be mapped back to the document line it came from.
 */
export function getSelectionInfo(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return { text: '', startLine: 1 };
    const state = inst.view.state;
    const sel = state.selection.main;
    return { text: state.sliceDoc(sel.from, sel.to), startLine: selectionStartLine(state.doc, sel) };
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

/**
 * Inserts a completion that carries square brackets without doubling one the user already typed.
 *
 * CM's word match (`/[@#\w]+/`) stops at "[" and at spaces, so for `[Order D|` the replaced range
 * is only "D". Replacing just that range with "[Order Details]" gives "[Order [Order Details]".
 * So when the insert BEGINS with "[", scan back from the match start for the "[" that opens the name
 * being typed, and widen the replacement to start there. Only letters, digits, spaces and _ - # @ $
 * may lie between that "[" and the partial (and at most 128 of them): anything else — a "]", a
 * comma, an operator, a quote, a comment — means the "[" belongs to something else and is left alone.
 *
 * Symmetrically, a "]" sitting immediately after the caret (auto-closed brackets) is consumed when
 * the insert ENDS with "]", so the result is never "[Order Details]]".
 *
 * Exported for tests; pure except for the dispatch.
 */
export function bracketedRange(doc, insertText, from, to) {
    let start = from;
    let end = to;

    if (insertText.startsWith('[')) {
        // Only the "[" that opens the name being typed: everything between it and the partial must
        // be what a bracketed name is made of, so a "[" in a string, a comment or another name is
        // never swallowed. Same rule as the SSMS shell's BracketedRange.
        for (let k = from - 1; k >= 0 && from - k <= 128; k--) {
            const c = doc.sliceString(k, k + 1);
            if (c === '[') { start = k; break; }
            if (!/[\p{L}\p{Nd} _\-#@$]/u.test(c)) break;
        }
    }

    if (insertText.endsWith(']') && end < doc.length && doc.sliceString(end, end + 1) === ']') {
        end += 1;
    }

    return { from: start, to: end };
}

function applyBracketed(view, insertText, from, to) {
    const range = bracketedRange(view.state.doc, insertText, from, to);
    view.dispatch({
        changes: { from: range.from, to: range.to, insert: insertText },
        selection: { anchor: range.from + insertText.length },
        // Keep it a single undo step and let CM treat it as a completion pick.
        userEvent: 'input.complete',
    });
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

// ── Spec 041 (R39) — findings in the editor ──────────────────────────────────────────────────────
//
// Analyser findings (FR-087) and the line of an execution error (FR-023) are shown through CodeMirror's
// lint state: lintGutter() is installed at create time; these exports feed it. lintKeymap is NOT
// installed — it binds F8, which the editor page uses for the Schema panel.

const LINT_SEVERITY = ['hint', 'info', 'warning', 'error'];

/**
 * Pure: translate line-based findings into CodeMirror diagnostics, clamped to the document.
 * @param {{lines:number, length:number, line:(n:number)=>{from:number,to:number}}} doc  a CM Text
 * @param {Array<{line:number, startColumn?:number|null, endColumn?:number|null, severity:number, message:string, source?:string}>} diags
 *        1-based line and columns; severity 0..3 = hint/info/warning/error.
 * @returns {Array<{from:number, to:number, severity:string, message:string, source:string}>}
 */
export function toLintDiagnostics(doc, diags) {
    if (!Array.isArray(diags) || !doc) return [];
    const out = [];
    for (const d of diags) {
        if (!d || typeof d.line !== 'number' || d.line < 1 || d.line > doc.lines) continue;
        const line = doc.line(d.line);
        let from = line.from;
        let to = line.to;
        if (typeof d.startColumn === 'number' && d.startColumn >= 1) {
            from = Math.min(line.to, line.from + d.startColumn - 1);
        }
        if (typeof d.endColumn === 'number' && d.endColumn >= 1) {
            to = Math.min(line.to, line.from + d.endColumn - 1);
        }
        // An end at or before the start (or a missing end) underlines the whole line.
        if (to <= from) {
            from = line.from;
            to = line.to;
        }
        out.push({
            from,
            to,
            severity: LINT_SEVERITY[d.severity] ?? 'info',
            message: String(d.message ?? ''),
            source: d.source || 'AKML SQL',
        });
    }
    return out;
}

/** Show `diags` (see toLintDiagnostics) as underlines + gutter markers, replacing any shown before. */
export function setDiagnostics(hostElementId, diags) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.cm.lint || typeof inst.cm.lint.setDiagnostics !== 'function') return;
    const mapped = toLintDiagnostics(inst.view.state.doc, diags);
    inst.view.dispatch(inst.cm.lint.setDiagnostics(inst.view.state, mapped));
}

/** Remove every finding shown in the editor. */
export function clearDiagnostics(hostElementId) {
    setDiagnostics(hostElementId, []);
}

/**
 * Spec 041 (T200) — select the next (forward) or previous finding, wrapping, as the palette's Next/Previous
 * problem. False when the editor shows none.
 */
export function goToProblem(hostElementId, forward) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.cm.lint) return false;
    if (typeof inst.cm.lint.diagnosticCount === 'function' && inst.cm.lint.diagnosticCount(inst.view.state) === 0) return false;
    const command = forward ? inst.cm.lint.nextDiagnostic : inst.cm.lint.previousDiagnostic;
    if (typeof command !== 'function') return false;
    inst.view.focus();
    return command(inst.view) === true;
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
    inst.disposeLintKeyboard?.();
    inst.view.destroy();
    if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
        inst.dotNetRef.dispose();
    }
    _instances.delete(hostElementId);
}

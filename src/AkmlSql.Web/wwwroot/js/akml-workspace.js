// Spec 041 (research R35, R37, X9) — the editor page's workspace interop: page-scoped key capture
// (this file's first half) and, from spec 041 US3, the splitters and the fold-away observer.
//
// WHY keys live here and not in a Blazor @onkeydown: a Blazor handler runs after the event has crossed
// the interop boundary, so it can never call preventDefault in time — F5 and Ctrl+R reloaded the page,
// Ctrl+S opened the browser's save dialog, and the Ctrl+K chords' second stroke reached CodeMirror's
// Find. A document-level CAPTURE listener sees the key first and cancels it synchronously.
//
// The listener is installed when the editor page mounts and removed when it unmounts, so the keys are
// claimed only on the editor page (FR-028). The binding table comes from WorkspaceKeyMap.cs.

const CHORD_MS = 1500;
const MODIFIER_KEYS = new Set(['Control', 'Shift', 'Alt', 'Meta', 'AltGraph', 'OS']);

let _keyListener = null;
let _keyRef = null;
let _bindings = [];
let _keyState = { chord: null, chordExpires: 0 };

function signature(code, ctrl, shift, alt) {
    return (ctrl ? 'Ctrl+' : '') + (shift ? 'Shift+' : '') + (alt ? 'Alt+' : '') + code;
}

function matches(b, code, ctrl, shift, alt) {
    return b.code === code && !!b.ctrl === ctrl && !!b.shift === shift && !!b.alt === alt;
}

/**
 * Pure: decide whether the page claims a keydown and which command it runs.
 *
 * @param {Array<{code:string, ctrl:boolean, shift:boolean, alt:boolean, commandId:string, chordPrefix:string|null}>} bindings
 * @param {{chord:string|null, chordExpires:number, modalOpen?:boolean}} state  never mutated
 * @param {{code:string, key:string, ctrlKey:boolean, shiftKey:boolean, altKey:boolean, metaKey:boolean, repeat:boolean, altGraph:boolean}} evt
 * @param {number} now  milliseconds
 * @returns {{claim:boolean, commandId:string|null, state:{chord:string|null, chordExpires:number}}}
 */
export function resolveKey(bindings, state, evt, now) {
    const next = { chord: state && state.chord ? state.chord : null, chordExpires: state ? state.chordExpires || 0 : 0 };
    const leave = () => ({ claim: false, commandId: null, state: next });

    // A modal dialog owns the keyboard while it is open.
    if (state && state.modalOpen) { next.chord = null; next.chordExpires = 0; return leave(); }
    // A bare modifier press (Ctrl going down for the second stroke) keeps a chord alive.
    if (MODIFIER_KEYS.has(evt.key)) return leave();

    const ctrl = !!(evt.ctrlKey || evt.metaKey);
    const shift = !!evt.shiftKey;
    const alt = !!evt.altKey;

    // AltGr arrives as Ctrl+Alt on Windows; on layouts like Polish it types characters (ó, ń) that
    // must reach the editor. Claim a Ctrl+Alt key only when it would type its plain letter.
    if (ctrl && alt) {
        if (evt.altGraph) return leave();
        const letter = evt.code && evt.code.startsWith('Key') ? evt.code.slice(3).toLowerCase() : null;
        if (letter && String(evt.key || '').toLowerCase() !== letter) return leave();
    }

    if (next.chord) {
        const prefix = next.chord;
        const live = now <= next.chordExpires;
        next.chord = null;
        next.chordExpires = 0;
        if (live) {
            const b = bindings.find(x => x.chordPrefix === prefix && matches(x, evt.code, ctrl, shift, alt));
            if (b) return { claim: true, commandId: evt.repeat ? null : b.commandId, state: next };
        }
        // An unknown second stroke cancels the chord and is handled as a key of its own.
    }

    const sig = signature(evt.code, ctrl, shift, alt);
    if (bindings.some(x => x.chordPrefix === sig)) {
        if (!evt.repeat) {
            next.chord = sig;
            next.chordExpires = now + CHORD_MS;
        }
        return { claim: true, commandId: null, state: next };
    }

    const b = bindings.find(x => !x.chordPrefix && matches(x, evt.code, ctrl, shift, alt));
    if (!b) return leave();
    // A held key auto-repeats: claim it (a held F5 must not reload) but run the command once.
    return { claim: true, commandId: evt.repeat ? null : b.commandId, state: next };
}

/**
 * Install the editor page's key listener. `bindings` is WorkspaceKeyMap.For(registered commands); the
 * .NET side receives `OnWorkspaceKey(commandId)`.
 */
export function initKeys(dotNetRef, bindings) {
    disposeKeys();
    _keyRef = dotNetRef;
    _bindings = Array.isArray(bindings) ? bindings : [];
    _keyState = { chord: null, chordExpires: 0 };
    _keyListener = (e) => {
        const evt = {
            code: e.code, key: e.key,
            ctrlKey: e.ctrlKey, shiftKey: e.shiftKey, altKey: e.altKey, metaKey: e.metaKey,
            repeat: e.repeat,
            altGraph: typeof e.getModifierState === 'function' && e.getModifierState('AltGraph'),
        };
        const modalOpen = !!document.querySelector('[aria-modal="true"]');
        const r = resolveKey(_bindings, { ..._keyState, modalOpen }, evt, Date.now());
        _keyState = r.state;
        if (!r.claim) return;
        // SYNCHRONOUS — before any await, or the browser acts first.
        e.preventDefault();
        e.stopPropagation();
        if (r.commandId && _keyRef) {
            _keyRef.invokeMethodAsync('OnWorkspaceKey', r.commandId).catch(() => { /* page gone */ });
        }
    };
    document.addEventListener('keydown', _keyListener, true /* capture */);
}

/** Replace the binding table (a command registered or removed after mount). */
export function updateKeys(bindings) {
    _bindings = Array.isArray(bindings) ? bindings : [];
}

/** Remove the listener. Called when the editor page unmounts, so no other page claims these keys. */
export function disposeKeys() {
    if (_keyListener) document.removeEventListener('keydown', _keyListener, true);
    _keyListener = null;
    _keyRef = null;
    _bindings = [];
    _keyState = { chord: null, chordExpires: 0 };
}

// ── Spec 041 US3 (research R35) — splitters and the fold-away observer ─────────────────────────────────
//
// A splitter drag rewrites ONE CSS variable on the workspace element at frame rate (pointer capture, rAF)
// and tells .NET the final size once, on release — a .NET round trip per pointer move could never keep up.
// The keyboard side is Blazor's (SplitterComponent); this module only cancels the browser default of the
// keys it handles (Arrow / Home / End / Space / Enter), never Tab, so focus can always leave.

const SEPARATOR_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', ' ', 'Enter']);
const _splitters = new WeakMap();

/** Pure: `px` limited to [min, max] (a max below the min gives the min). */
export function clamp(min, max, px) {
    return Math.min(Math.max(px, min), Math.max(min, max));
}

/**
 * Pure: a panel's size after the pointer moved (dx, dy) from where the drag began. `side` says where the
 * panel is: 'left' (the Schema panel — grows to the right), 'right' (the AI panel — grows to the left) or
 * 'bottom' (the results pane — grows upwards).
 */
export function dragSize(side, startPx, dx, dy) {
    if (side === 'left') return startPx + dx;
    if (side === 'right') return startPx - dx;
    return startPx - dy;
}

/** Pure: whether a separator cancels this key's browser default (never Tab). */
export function isSeparatorKey(key) {
    return SEPARATOR_KEYS.has(key);
}

/**
 * Wire a splitter: dragging `el` resizes the panel on `side` by rewriting `cssVar` on `hostEl` between
 * `min` and `max`; release calls `OnSplitterCommitted(id, px)`, a double-click `OnSplitterReset(id)`.
 * Without a usable `hostEl` (a Blazor parent's @ref is not set yet on its child's first render) the host is
 * the nearest ancestor marked `data-splitter-host`.
 */
export function attachSplitter(el, hostEl, cssVar, side, min, max, dotNetRef, id) {
    if (!el || _splitters.has(el)) return;
    if (!hostEl || !hostEl.style) hostEl = typeof el.closest === 'function' ? el.closest('[data-splitter-host]') : null;
    if (!hostEl) return;
    const state = { drag: null, raf: 0, pending: null, min, max };
    const read = () => {
        const v = parseFloat(hostEl.style.getPropertyValue(cssVar) || getComputedStyle(hostEl).getPropertyValue(cssVar));
        return Number.isFinite(v) ? v : state.min;
    };
    state.down = (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        state.drag = { x: e.clientX, y: e.clientY, start: read(), px: read(), moved: false };
        try { el.setPointerCapture(e.pointerId); } catch { /* still works without */ }
        hostEl.classList.add('akml-workspace--dragging');
    };
    state.move = (e) => {
        const d = state.drag;
        if (!d) return;
        const dx = e.clientX - d.x;
        const dy = e.clientY - d.y;
        if (!d.moved && Math.abs(dx) < 3 && Math.abs(dy) < 3) return;
        d.moved = true;
        d.px = Math.round(clamp(state.min, state.max, dragSize(side, d.start, dx, dy)));
        state.pending = d.px;
        if (!state.raf) {
            state.raf = requestAnimationFrame(() => {
                state.raf = 0;
                if (state.pending != null) hostEl.style.setProperty(cssVar, `${state.pending}px`);
            });
        }
    };
    state.up = () => {
        const d = state.drag;
        state.drag = null;
        hostEl.classList.remove('akml-workspace--dragging');
        if (!d || !d.moved) return;
        if (state.raf) { cancelAnimationFrame(state.raf); state.raf = 0; }
        hostEl.style.setProperty(cssVar, `${d.px}px`);
        dotNetRef.invokeMethodAsync('OnSplitterCommitted', id, d.px).catch(() => { /* page gone */ });
    };
    state.dbl = () => dotNetRef.invokeMethodAsync('OnSplitterReset', id).catch(() => { /* page gone */ });
    el.addEventListener('pointerdown', state.down);
    el.addEventListener('pointermove', state.move);
    el.addEventListener('pointerup', state.up);
    el.addEventListener('pointercancel', state.up);
    el.addEventListener('dblclick', state.dbl);
    _splitters.set(el, state);
}

/** Update a splitter's limits (the window was resized). */
export function updateSplitter(el, min, max) {
    const state = el && _splitters.get(el);
    if (!state) return;
    state.min = min;
    state.max = max;
}

/** Remove a splitter's listeners. */
export function detachSplitter(el) {
    const state = el && _splitters.get(el);
    if (!state) return;
    el.removeEventListener('pointerdown', state.down);
    el.removeEventListener('pointermove', state.move);
    el.removeEventListener('pointerup', state.up);
    el.removeEventListener('pointercancel', state.up);
    el.removeEventListener('dblclick', state.dbl);
    if (state.raf) cancelAnimationFrame(state.raf);
    _splitters.delete(el);
}

/** Cancel the default of the separator's own keys; Tab is left alone so focus can leave (FR-103). */
export function trapSeparatorKeys(el) {
    if (!el || el.__akmlSeparatorKeys) return;
    el.__akmlSeparatorKeys = (e) => { if (isSeparatorKey(e.key)) e.preventDefault(); };
    el.addEventListener('keydown', el.__akmlSeparatorKeys);
}

let _observer = null;

/** Report the workspace's size to .NET (rAF-debounced) so it can fold side panels away (FR-046). */
export function observeWorkspace(hostEl, dotNetRef) {
    unobserveWorkspace();
    if (!hostEl || typeof ResizeObserver !== 'function') return;
    let raf = 0;
    let last = '';
    const report = () => {
        raf = 0;
        const w = Math.round(hostEl.clientWidth);
        const h = Math.round(hostEl.clientHeight);
        const key = `${w}x${h}`;
        if (key === last) return;
        last = key;
        dotNetRef.invokeMethodAsync('OnWorkspaceResized', w, h).catch(() => { /* page gone */ });
    };
    _observer = new ResizeObserver(() => { if (!raf) raf = requestAnimationFrame(report); });
    _observer.observe(hostEl);
    report();
}

export function unobserveWorkspace() {
    if (_observer) _observer.disconnect();
    _observer = null;
}

/**
 * Move keyboard focus to the first focusable element inside `el` (F6 pane cycling); a pane with nothing
 * focusable in it (an empty Schema tree) takes focus itself when it has a tabindex.
 */
export function focusRegion(el) {
    if (!el) return false;
    const inner = el.querySelector('.cm-content, [tabindex="0"], button:not([disabled]), input:not([disabled]), textarea, [contenteditable="true"]');
    const target = inner ?? (el.hasAttribute('tabindex') ? el : null);
    if (!target || typeof target.focus !== 'function') return false;
    target.focus({ preventScroll: false });
    return true;
}

/** Focus the element with this data-testid (a splitter or the edge strip it leaves). */
export function focusTestId(id) {
    if (typeof document === 'undefined') return false;
    const el = document.querySelector(`[data-testid="${CSS.escape(id)}"]`);
    if (!el || typeof el.focus !== 'function') return false;
    el.focus();
    return true;
}

/** F6 order (contracts/ui.md §2): editor → results → Schema → AI → toolbar. */
export const PANE_ORDER = Object.freeze(['centre', 'results', 'schema', 'ai', 'toolbar']);

/**
 * The pane F6 (Shift+F6 when `backwards`) moves to from `current` (a PANE_ORDER name, or null when focus is in
 * no pane), skipping panes `canFocus` rejects (hidden or folded). Null when no pane can take focus.
 */
export function nextPane(current, backwards, canFocus) {
    const n = PANE_ORDER.length;
    let i = PANE_ORDER.indexOf(current);
    if (i < 0) i = backwards ? 0 : n - 1;   // outside every pane: start at the editor (or the toolbar going back)
    for (let step = 1; step <= n; step++) {
        const p = PANE_ORDER[(i + (backwards ? -step : step) + n * 2) % n];
        if (canFocus(p)) return p;
    }
    return null;
}

const PANE_SELECTORS = {
    centre: '#akml-ws-centre',
    results: '#akml-ws-results .akml-ws-results-host:not(.akml-ws-hidden)',
    schema: '#akml-ws-schema',
    ai: '#akml-ws-ai',
    toolbar: '.akml-editor-toolbar',
};

function paneEl(name) {
    return typeof document === 'undefined' ? null : document.querySelector(PANE_SELECTORS[name]);
}

/** F6 / Shift+F6 on the editor page: focus the next pane that is on screen and has something to focus. */
export function cyclePanes(backwards) {
    const back = !!backwards;
    const active = document.activeElement;
    const current = PANE_ORDER.find(p => {
        const el = paneEl(p);
        return el && active && el.contains(active);
    }) ?? null;
    const tried = new Set();
    const usable = p => {
        const el = paneEl(p);
        return !tried.has(p) && !!el && el.getClientRects().length > 0;
    };
    // A pane with nothing focusable in it (an empty results pane) passes the turn to the next one.
    for (let target = nextPane(current, back, usable); target; target = nextPane(target, back, usable)) {
        if (focusRegion(paneEl(target))) return target;
        tried.add(target);
    }
    return null;
}

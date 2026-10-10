// Spec 041 (research R2, R3, R4, R7, N22) — the results grid's pointer and keyboard interop. All grid
// STATE (widths, selection, sort, edits) lives in .NET (Shared/ResultSetGrid.razor); this module does
// only what JS must do:
//   * measure the few strings auto-fit needs with a canvas (font read from the live cells, so zoom and
//     font fallback are honoured);
//   * column resize by drag — pointer capture and live rewriting of the grid's one --akml-grid-cols
//     custom property at frame rate, one .NET call on release (a .NET round trip per pointer move could
//     never keep up);
//   * cell presses and clicks — reported to .NET from one delegated listener, so the grid needs no
//     Blazor handler per cell (thousands of them would be re-diffed on every render);
//   * drag-select — report the cell under the pointer only when it changes;
//   * cancel the browser's default for the grid's keys (arrows scrolling, Ctrl+A selecting the page,
//     Ctrl+C copying page text, Ctrl+0 resetting zoom) synchronously, which a Blazor handler cannot;
//   * the context-menu event, routed to .NET with the cell it is for. The Menu key and Shift+F10 fire
//     contextmenu on the focused element — the grid root — (at clientX/Y = 0 in some browsers), so those
//     open at the focused cell instead (N22). Their keydown needs no handling: the event is what opens it.
// One root element per result set; state is kept per root in a WeakMap.

const _grids = new WeakMap();
const CLICK_SLOP_PX = 3;
const MIN_COLUMN_PX = 48;
const ROW_HEIGHT_PX = 24;

// ── Pure helpers (unit-tested) ────────────────────────────────────────────────────────────────────

/** Rewrite track `index` (0 = the row-number track) of a CSS track list. */
export function replaceTrack(tracks, index, px) {
    const parts = String(tracks || '').trim().split(/\s+/).filter(p => p.length > 0);
    if (index < 0 || index >= parts.length) return parts.join(' ');
    parts[index] = `${Math.round(px)}px`;
    return parts.join(' ');
}

/** Read track `index` of a CSS track list in pixels; null when absent. */
export function readTrack(tracks, index) {
    const parts = String(tracks || '').trim().split(/\s+/).filter(p => p.length > 0);
    if (index < 0 || index >= parts.length) return null;
    const v = parseFloat(parts[index]);
    return Number.isFinite(v) ? v : null;
}

const NAV_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown', ' ']);

/**
 * Whether the grid cancels a key's browser default. With the inline cell editor focused, only Tab
 * (next editable cell), Enter (commit) and Escape (cancel) are claimed — arrows and Ctrl+A belong to
 * the input. Tab is never claimed on the grid itself: it leaves the grid. The Menu key and Shift+F10
 * are not cancelled either: the contextmenu event they produce is what opens the grid's menu.
 * A printable character on the grid is: .NET starts the edit WITH it, and the inline input is focused
 * while this keydown is still being handled, so the browser would otherwise type it a second time.
 */
export function shouldTrapKey(evt, isInlineInput) {
    const k = evt.key;
    const ctrl = !!(evt.ctrlKey || evt.metaKey);
    if (isInlineInput) return k === 'Tab' || k === 'Enter' || k === 'Escape';
    if (NAV_KEYS.has(k)) return true;
    if (ctrl && !evt.altKey && (k === 'a' || k === 'A' || k === 'c' || k === 'C' || k === '0')) return true;
    if (typeof k === 'string' && k.length === 1 && !ctrl && !evt.altKey) return true;
    return false;
}

/**
 * Whether a contextmenu event came from the keyboard (the Menu key, Shift+F10): it targets the focused
 * element — the grid root, never a cell — and some browsers report it at clientX/Y = 0.
 */
export function isKeyboardMenu(targetIsRoot, clientX, clientY) {
    return !!targetIsRoot || (clientX === 0 && clientY === 0);
}

/** A press that moved less than the slop is a click (the edge double-click must still arrive). */
export function isClick(dx, dy) {
    return Math.abs(dx) < CLICK_SLOP_PX && Math.abs(dy) < CLICK_SLOP_PX;
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────────────────────────

/** Attach the grid's listeners to its root element (the role=grid div). */
export function init(rootEl, dotNetRef) {
    if (!rootEl || _grids.has(rootEl)) return;
    const state = { ref: dotNetRef, drag: null, resize: null, raf: 0, pendingTracks: null };
    _grids.set(rootEl, state);

    state.onPointerDown = (e) => onPointerDown(rootEl, state, e);
    state.onPointerMove = (e) => onPointerMove(rootEl, state, e);
    state.onPointerUp = (e) => onPointerUp(rootEl, state, e);
    state.onKeyDown = (e) => {
        if (shouldTrapKey(e, isInlineInput(e.target))) e.preventDefault();
    };
    state.onContextMenu = (e) => onContextMenu(rootEl, state, e);
    state.onClick = (e) => onClick(state, e);

    rootEl.addEventListener('pointerdown', state.onPointerDown);
    rootEl.addEventListener('pointermove', state.onPointerMove);
    rootEl.addEventListener('pointerup', state.onPointerUp);
    rootEl.addEventListener('pointercancel', state.onPointerUp);
    rootEl.addEventListener('keydown', state.onKeyDown);
    rootEl.addEventListener('contextmenu', state.onContextMenu);
    rootEl.addEventListener('click', state.onClick);
}

/** Remove the listeners (the set left the page). */
export function dispose(rootEl) {
    const state = rootEl && _grids.get(rootEl);
    if (!state) return;
    rootEl.removeEventListener('pointerdown', state.onPointerDown);
    rootEl.removeEventListener('pointermove', state.onPointerMove);
    rootEl.removeEventListener('pointerup', state.onPointerUp);
    rootEl.removeEventListener('pointercancel', state.onPointerUp);
    rootEl.removeEventListener('keydown', state.onKeyDown);
    rootEl.removeEventListener('contextmenu', state.onContextMenu);
    rootEl.removeEventListener('click', state.onClick);
    if (state.raf) cancelAnimationFrame(state.raf);
    _grids.delete(rootEl);
}

// ── Pointer: column resize and drag-select ────────────────────────────────────────────────────────

function onPointerDown(rootEl, state, e) {
    if (e.button !== 0) return;
    const handle = e.target && e.target.closest ? e.target.closest('.akml-col-resizer') : null;
    if (handle) {
        e.preventDefault();
        const col = parseInt(handle.getAttribute('data-col'), 10);
        const tracks = rootEl.style.getPropertyValue('--akml-grid-cols');
        const start = readTrack(tracks, col + 1) ?? handle.parentElement.getBoundingClientRect().width;
        state.resize = { col, startX: e.clientX, startY: e.clientY, startPx: start, px: start, moved: false };
        try { handle.setPointerCapture(e.pointerId); } catch { /* not capturable — still works without */ }
        rootEl.classList.add('akml-resizing');
        return;
    }
    if (isInlineInput(e.target) || (e.target.closest && e.target.closest('button'))) return;
    const cell = closestCell(e.target);
    if (!cell) return;
    const r = +cell.getAttribute('data-r');
    const c = +cell.getAttribute('data-c');
    if (!e.shiftKey) state.drag = { r, c };
    // Data cells only; a row number (c = -1) is a Blazor handler of its own.
    if (c >= 0) {
        state.ref.invokeMethodAsync('OnCellPointerDownFromJs', r, c, !!e.shiftKey, !!(e.ctrlKey || e.metaKey))
            .catch(() => { /* grid gone */ });
    }
}

function onClick(state, e) {
    // A click that ends a drag across cells lands on their common ancestor, never on a cell.
    if (e.button !== 0 || e.shiftKey || e.ctrlKey || e.metaKey || isInlineInput(e.target)) return;
    const cell = closestCell(e.target);
    if (!cell) return;
    const c = +cell.getAttribute('data-c');
    if (c < 0) return;
    state.ref.invokeMethodAsync('OnCellClickFromJs', +cell.getAttribute('data-r'), c).catch(() => { /* grid gone */ });
}

function closestCell(target) {
    return target && target.closest ? target.closest('[data-r][data-c]') : null;
}

function isInlineInput(target) {
    return !!(target && target.classList && target.classList.contains('akml-cell-input'));
}

function onPointerMove(rootEl, state, e) {
    if (state.resize) {
        const r = state.resize;
        const dx = e.clientX - r.startX;
        if (!r.moved && isClick(dx, e.clientY - r.startY)) return;
        r.moved = true;
        r.px = Math.max(MIN_COLUMN_PX, r.startPx + dx);
        state.pendingTracks = replaceTrack(rootEl.style.getPropertyValue('--akml-grid-cols'), r.col + 1, r.px);
        if (!state.raf) {
            state.raf = requestAnimationFrame(() => {
                state.raf = 0;
                if (state.pendingTracks) rootEl.style.setProperty('--akml-grid-cols', state.pendingTracks);
            });
        }
        return;
    }
    if (state.drag && (e.buttons & 1)) {
        const under = document.elementFromPoint(e.clientX, e.clientY);
        const cell = closestCell(under);
        if (!cell || !rootEl.contains(cell)) return;
        const r = +cell.getAttribute('data-r');
        const c = +cell.getAttribute('data-c');
        if (r === state.drag.r && c === state.drag.c) return;
        state.drag = { r, c };
        state.ref.invokeMethodAsync('OnDragSelectFromJs', r, c).catch(() => { /* grid gone */ });
    }
}

function onPointerUp(rootEl, state) {
    state.drag = null;
    const r = state.resize;
    if (!r) return;
    state.resize = null;
    rootEl.classList.remove('akml-resizing');
    if (r.moved) {
        if (state.raf) { cancelAnimationFrame(state.raf); state.raf = 0; }
        if (state.pendingTracks) rootEl.style.setProperty('--akml-grid-cols', state.pendingTracks);
        state.ref.invokeMethodAsync('OnColumnResizedFromJs', r.col, Math.round(r.px)).catch(() => { /* grid gone */ });
    }
    // A cancelled pointerdown suppresses the browser's own focus change: put focus back on the grid.
    try { rootEl.focus({ preventScroll: true }); } catch { /* detached */ }
}

// ── Context menu (N22) ────────────────────────────────────────────────────────────────────────────

function onContextMenu(rootEl, state, e) {
    if (isInlineInput(e.target)) return; // native paste menu
    e.preventDefault();
    let x = e.clientX;
    let y = e.clientY;
    let r = -1;
    let c = -1;
    let header = false;
    const keyboard = isKeyboardMenu(e.target === rootEl, x, y);
    const target = keyboard ? activeCell(rootEl) : (e.target && e.target.closest ? e.target.closest('[data-r][data-c], [data-header-c]') : null);
    if (target) {
        if (target.hasAttribute('data-header-c')) {
            header = true;
            c = +target.getAttribute('data-header-c');
        } else {
            r = +target.getAttribute('data-r');
            c = +target.getAttribute('data-c');
        }
        if (keyboard) {
            const rect = target.getBoundingClientRect();
            x = rect.left + 8;
            y = rect.bottom;
        }
    } else if (keyboard) {
        const rect = rootEl.getBoundingClientRect();
        x = rect.left + 16;
        y = rect.top + 16;
    }
    state.ref.invokeMethodAsync('OnContextMenuFromJs', x, y, r, c, header).catch(() => { /* grid gone */ });
}

function activeCell(rootEl) {
    return byId(rootEl, rootEl.getAttribute('aria-activedescendant'));
}

// Ids are looked up inside this grid: another document's grid may use the same ones.
function byId(rootEl, id) {
    if (!rootEl || !id) return null;
    if (globalThis.CSS && typeof globalThis.CSS.escape === 'function') return rootEl.querySelector('#' + globalThis.CSS.escape(id));
    for (const el of rootEl.querySelectorAll('[id]')) if (el.id === id) return el;
    return null;
}

// ── Measurement (R2) ──────────────────────────────────────────────────────────────────────────────

let _canvas = null;

function fontOf(el, fallback) {
    if (!el) return fallback;
    const s = getComputedStyle(el);
    return `${s.fontStyle} ${s.fontWeight} ${s.fontSize} ${s.fontFamily}`;
}

/**
 * Measure, per column, the header text and the widest of its candidate values, in CSS pixels.
 * @param {HTMLElement} rootEl
 * @param {string[]} headers
 * @param {string[][]} candidates  per column, the few longest display texts
 * @returns {{header:number, value:number}[]}
 */
export function measureColumns(rootEl, headers, candidates) {
    _canvas = _canvas || document.createElement('canvas');
    const ctx = _canvas.getContext('2d');
    const headerFont = fontOf(rootEl.querySelector('.akml-col-name'), '600 12px "Segoe UI", sans-serif');
    const bodyFont = fontOf(rootEl.querySelector('.akml-results-cell[data-c]'), '12px Consolas, monospace');
    const out = [];
    for (let i = 0; i < headers.length; i++) {
        ctx.font = headerFont;
        const header = ctx.measureText(headers[i] || '').width;
        ctx.font = bodyFont;
        let value = 0;
        for (const text of (candidates[i] || [])) value = Math.max(value, ctx.measureText(text || '').width);
        out.push({ header, value });
    }
    return out;
}

// ── Focus and scrolling ───────────────────────────────────────────────────────────────────────────

/**
 * Bring the focused cell (element id `id`, display row `r`) into view; a virtualised row not yet
 * rendered is scrolled to by estimate.
 */
export function scrollCellIntoView(rootEl, id, r) {
    if (!rootEl) return;
    const el = byId(rootEl, id);
    if (el && typeof el.scrollIntoView === 'function') {
        el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        return;
    }
    const target = (r + 1) * ROW_HEIGHT_PX;
    if (target < rootEl.scrollTop + ROW_HEIGHT_PX) rootEl.scrollTop = Math.max(0, target - ROW_HEIGHT_PX * 2);
    else if (target > rootEl.scrollTop + rootEl.clientHeight) rootEl.scrollTop = target - rootEl.clientHeight + ROW_HEIGHT_PX;
}

/** Put keyboard focus on the grid root without scrolling. */
export function focusRoot(rootEl) {
    try { if (rootEl) rootEl.focus({ preventScroll: true }); } catch { /* detached */ }
}


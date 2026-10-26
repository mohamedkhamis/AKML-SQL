// Spec 041 (research X9, R5, R7, R58, N21) — the browser helpers every modal or keyboard-driven surface
// shares: the dialog host, the context menu, the cell-value dialog, the tab strip and the connection
// manager (whose module re-exports the focus helpers from here, so its callers did not change).
//
// WHY one module: the focus lifecycle has to be one STACK for the whole page. A menu that opens a
// dialog stashes focus twice; with the old single slot (akml-connection-manager.js) the dialog
// overwrote the menu's return target and focus was lost when both closed.
//
// Every export is a safe no-op when its element is gone (components unmount on close).

// ── Focus lifecycle ─────────────────────────────────────────────────────────────────────────────

const _focusStack = [];

let _helpCleanup = null;

/** Native titles remain available to the pointer; keyboard users get dismissible help too. */
export function installHelpTooltips() {
    disposeHelpTooltips();
    let owner = null;
    let tip = null;
    let previousDescription = null;
    const close = () => {
        tip?.remove();
        if (owner) {
            if (previousDescription) owner.setAttribute('aria-describedby', previousDescription);
            else owner.removeAttribute('aria-describedby');
        }
        tip = owner = null;
    };
    const focus = e => {
        close();
        // Spec 043 (FR-012): a query tab's help names the keys that move between tabs and close one.
        const el = e.target.closest?.('.akml-editor-toolbar [title], .cm-lint-marker[title], .akml-doctabs [role="tab"][title]');
        if (!el || !el.title) return;
        owner = el;
        previousDescription = el.getAttribute('aria-describedby');
        tip = document.createElement('div');
        tip.id = 'akml-keyboard-help';
        tip.className = 'akml-keyboard-help';
        tip.setAttribute('role', 'tooltip');
        tip.textContent = el.title;
        document.body.appendChild(tip);
        el.setAttribute('aria-describedby', [previousDescription, tip.id].filter(Boolean).join(' '));
        const box = el.getBoundingClientRect();
        tip.style.left = `${Math.max(4, Math.min(box.left, window.innerWidth - tip.offsetWidth - 4))}px`;
        tip.style.top = `${Math.min(box.bottom + 4, window.innerHeight - tip.offsetHeight - 4)}px`;
    };
    const key = e => { if (e.key === 'Escape') close(); };
    document.addEventListener('focusin', focus);
    document.addEventListener('focusout', close);
    document.addEventListener('keydown', key, true);
    _helpCleanup = () => {
        close();
        document.removeEventListener('focusin', focus);
        document.removeEventListener('focusout', close);
        document.removeEventListener('keydown', key, true);
    };
}

export function disposeHelpTooltips() {
    _helpCleanup?.();
    _helpCleanup = null;
}

/**
 * Remember the element that has focus, to hand focus back to it on close. Call once when a modal
 * surface opens. With `panelSelector`, an element already inside that panel is NOT stashed (a second
 * call while the panel holds focus must not overwrite the real return target). Returns true when an
 * element was stashed.
 */
export function stashFocus(panelSelector) {
    const active = document.activeElement;
    if (panelSelector && active && typeof active.closest === 'function' && active.closest(panelSelector)) {
        return false;
    }
    _focusStack.push(active);
    return true;
}

/** Return focus to the most recently stashed element. Skips (and drops) an element that has left
 *  the document. Safe when nothing is stashed. */
export function restoreFocus() {
    while (_focusStack.length > 0) {
        const el = _focusStack.pop();
        try {
            if (el && typeof el.focus === 'function' && document.contains(el)) {
                el.focus();
                return;
            }
        } catch { /* detached — try the next one */ }
    }
}

/** Focus one element (an ElementReference from Blazor). No-op when it is gone. */
export function focusElement(el) {
    try { if (el && typeof el.focus === 'function') el.focus(); } catch { /* detached */ }
}

/** Move focus into a panel: `preferSelector` first when it matches a visible element, else the first
 *  focusable element, else the panel itself (tabindex=-1 so it can still receive Escape). */
export function focusFirst(panelEl, preferSelector) {
    if (preferSelector && panelEl && typeof panelEl.querySelector === 'function') {
        const preferred = panelEl.querySelector(preferSelector);
        if (preferred && typeof preferred.focus === 'function' && preferred.offsetParent !== null) {
            try { preferred.focus(); return; } catch { /* detached — fall through */ }
        }
    }
    const focusables = getFocusable(panelEl);
    if (focusables.length > 0) {
        try { focusables[0].focus(); } catch { /* detached */ }
    } else if (panelEl && typeof panelEl.focus === 'function') {
        try { panelEl.focus(); } catch { /* detached */ }
    }
}

/** Keep Tab inside a modal panel: Tab past the last focusable wraps to the first, Shift+Tab past the
 *  first wraps to the last. Idempotent per element; the listener leaves with the element. */
export function trapTab(panelEl) {
    if (!panelEl || panelEl.dataset.akmlTrapped === '1') return;
    panelEl.dataset.akmlTrapped = '1';
    panelEl.addEventListener('keydown', (e) => {
        if (e.key !== 'Tab') return;
        const focusables = getFocusable(panelEl);
        if (focusables.length === 0) {
            e.preventDefault();
            return;
        }
        const first = focusables[0];
        const last = focusables[focusables.length - 1];
        const active = document.activeElement;
        if (e.shiftKey) {
            if (active === first || !panelEl.contains(active)) {
                e.preventDefault();
                try { last.focus(); } catch { /* ignore */ }
            }
        } else if (active === last || !panelEl.contains(active)) {
            e.preventDefault();
            try { first.focus(); } catch { /* ignore */ }
        }
    });
}

function getFocusable(panelEl) {
    if (!panelEl || typeof panelEl.querySelectorAll !== 'function') return [];
    const selector = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])',
    ].join(',');
    const nodes = Array.prototype.slice.call(panelEl.querySelectorAll(selector));
    return nodes.filter((el) => {
        if (el.getAttribute('tabindex') === '-1') return false;
        return el.offsetParent !== null || el === document.activeElement;
    });
}

// ── Tab strips (Shared/TabStrip.razor) ──────────────────────────────────────────────────────────

const TAB_STRIP_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End']);

/** Cancel the browser's default for the keys a tab strip owns (Arrow scrolling the page, Home/End
 *  jumping it). Only these: an unconditional preventDefault would also cancel Tab and trap focus.
 *  TabStrip's own @onkeydown still runs and moves the selection. Idempotent per element.
 *  Spec 043: a closable strip's tabs are divs, so Space (which would scroll the page) is theirs too; a strip of
 *  button tabs keeps Space for the button's own click. A closable strip also cancels the middle-button press. */
export function trapTabStripKeys(el, closable) {
    if (!el || el.dataset.akmlTabTrapped === '1') return;
    el.dataset.akmlTabTrapped = '1';
    el.addEventListener('keydown', (e) => {
        if (TAB_STRIP_KEYS.has(e.key) || (closable && e.key === ' ' && e.target?.getAttribute?.('role') === 'tab')) e.preventDefault();
    });
    // A closable strip closes a tab on a middle-button release; cancelling the press stops the browser's autoscroll.
    if (closable) el.addEventListener('mousedown', (e) => { if (e.button === 1) e.preventDefault(); });
}

/**
 * Spec 043 (review of US1): after a query tab was closed, give focus to the tab now active (element id `tabId`) when the closed
 * tab had it — its element is gone, so focus fell to the page body. Focus anywhere else (the editor, a dialog) stays put.
 */
export function refocusTabStrip(list, tabId, doc = (typeof document === 'undefined' ? null : document)) {
    if (!list || !doc) return false;
    const active = doc.activeElement;
    if (active && active !== doc.body) return false;
    const tab = doc.getElementById(tabId);
    if (!tab || !list.contains(tab) || typeof tab.focus !== 'function') return false;
    tab.focus();
    return true;
}

/** Spec 043: scroll the tab with element id `tabId` into view inside the strip `list` (the active query tab). */
export function revealTab(list, tabId) {
    const tab = list && document.getElementById(tabId);
    if (tab && typeof tab.scrollIntoView === 'function') tab.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

// ── Keyboard lists (Pages/History.razor, Pages/Snippets.razor) ──────────────────────────────────────

const LIST_KEYS = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End']);

/** Review WEB-13: cancel the browser's default for the keys a list owns (Arrow scrolling, Home/End jumping), and for Space
 *  when the list itself has focus (a listbox: Space selects, it does not scroll). Tab is left alone, so focus can always
 *  leave. The list's own @onkeydown still runs. Idempotent per element. */
export function trapListKeys(el) {
    if (!el || el.dataset.akmlListTrapped === '1') return;
    el.dataset.akmlListTrapped = '1';
    el.addEventListener('keydown', (e) => {
        if (LIST_KEYS.has(e.key) || (e.key === ' ' && e.target === el)) e.preventDefault();
    });
}

/** Re-review REV-08: scroll the list so the option with this id is fully visible, the least distance needed, without
 *  scrolling the page or moving focus (a listbox with aria-activedescendant keeps focus on itself). */
export function revealInList(listEl, optionId) {
    const option = typeof document !== 'undefined' ? document.getElementById(optionId) : null;
    if (!listEl || !option) return;
    const list = listEl.getBoundingClientRect();
    const box = option.getBoundingClientRect();
    if (box.top < list.top) listEl.scrollTop -= (list.top - box.top);
    else if (box.bottom > list.bottom) listEl.scrollTop += (box.bottom - list.bottom);
}

/** Focus the element with this id, if it is there (a list moving its one tab stop). */
export function focusById(id) {
    const el = document.getElementById(id);
    if (el && typeof el.focus === 'function') el.focus();
}

// ── Context menus (Shared/ContextMenu.razor) ────────────────────────────────────────────────────

/**
 * Pure: where to put a menu of size (w, h) opened at (x, y) in a viewport (vw, vh) so it stays
 * visible. Overflowing the right edge opens it to the left of the pointer; overflowing the bottom
 * opens it above; a menu bigger than the window is pinned to the top-left margin.
 */
export function clampToViewport(x, y, w, h, vw, vh, margin = 4) {
    let left = x;
    if (left + w > vw - margin) left = x - w;
    if (left < margin) left = margin;
    let top = y;
    if (top + h > vh - margin) top = y - h;
    if (top < margin) top = margin;
    return { left, top };
}

/** Position a rendered menu panel at (x, y), kept inside the viewport, and focus its first item. */
export function placeMenu(panelEl, x, y) {
    if (!panelEl || typeof panelEl.getBoundingClientRect !== 'function') return;
    const r = panelEl.getBoundingClientRect();
    const pos = clampToViewport(x, y, r.width, r.height, window.innerWidth, window.innerHeight);
    panelEl.style.left = `${pos.left}px`;
    panelEl.style.top = `${pos.top}px`;
    panelEl.style.visibility = 'visible';
}

// ── Clipboard (research R5, N14) ────────────────────────────────────────────────────────────────

/**
 * Write text to the clipboard. Uses the Clipboard API in a secure context; otherwise — the web
 * edition is served over plain http on a LAN, where navigator.clipboard is unavailable — a hidden
 * <textarea> + document.execCommand('copy'). Focus goes back to the element that had it, so a
 * keyboard user stays in the grid. Resolves true when the text was copied.
 */
export async function copyText(text) {
    const value = text == null ? '' : String(text);
    try {
        if (globalThis.isSecureContext && globalThis.navigator && globalThis.navigator.clipboard
            && typeof globalThis.navigator.clipboard.writeText === 'function') {
            await globalThis.navigator.clipboard.writeText(value);
            return true;
        }
    } catch { /* permission denied or not focused — fall through to the legacy path */ }
    return legacyCopy(value);
}

function legacyCopy(value) {
    const doc = globalThis.document;
    if (!doc || !doc.body || typeof doc.createElement !== 'function') return false;
    const previous = doc.activeElement;
    const ta = doc.createElement('textarea');
    ta.value = value;
    ta.setAttribute('readonly', '');
    ta.setAttribute('aria-hidden', 'true');
    ta.style.position = 'fixed';
    ta.style.top = '0';
    ta.style.left = '0';
    ta.style.opacity = '0';
    doc.body.appendChild(ta);
    try {
        ta.select();
        return doc.execCommand('copy') === true;
    } catch {
        return false;
    } finally {
        ta.remove();
        try { if (previous && typeof previous.focus === 'function') previous.focus(); } catch { /* detached */ }
    }
}

/** Scroll an element to the top of its scroll container (the results pane's "jump to set"). */
export function scrollToElement(el) {
    try { if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'start', behavior: 'smooth' }); } catch { /* detached */ }
}

/** Spec 041 (T175) — click an element by id (the editor's hidden file input, for the Open command). */
export function clickElement(id) {
    const el = typeof document !== 'undefined' ? document.getElementById(id) : null;
    if (!el || typeof el.click !== 'function') return false;
    el.click();
    return true;
}

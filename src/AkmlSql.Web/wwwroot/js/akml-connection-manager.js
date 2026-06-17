// Phase 4 (web connection manager) — focus-lifecycle interop for ConnectionManagerModal.razor.
//
// WHY a dedicated module (not akml-command-palette.js):
//   * The palette module registers a GLOBAL capture-phase Ctrl/Cmd+P listener on init — we must
//     not double-register or own that here.
//   * The palette's trapKeys only preventDefaults a SINGLE input (it has one focusable field). The
//     connection manager is a two-pane form with many focusables (list rows, inputs, radios,
//     buttons), so it needs a REAL Tab cycle scoped to the panel (Tab on the last element wraps to
//     the first; Shift+Tab on the first wraps to the last), keeping focus inside the aria-modal.
//
// All exports are JSDisconnected-safe no-ops when their element is gone (the modal unmounts on
// close, so the panel-scoped keydown listener is GC'd with the element — no manual removal).

// Captured synchronously when the modal opens, so focus can be handed back exactly there on close.
let _focusBeforeOpen = null;

/** Capture the element that had focus before the modal opened (typically a button on the page or
 *  the editor). Called from OnAfterRenderAsync before focusFirst. Idempotent within one open: a
 *  second call while the panel already holds focus must not overwrite the stashed element. */
export function stashFocus() {
    const active = document.activeElement;
    // Don't stash an element that lives inside our own panel (would lose the real return target).
    if (active && typeof active.closest === 'function' && active.closest('.akml-connmgr-panel')) {
        return;
    }
    _focusBeforeOpen = active;
}

/** Move focus into the modal so keyboard/SR users land inside it and the panel-level @onkeydown
 *  (Esc) receives keystrokes. Prefers `preferSelector` (e.g. the first form FIELD) when it matches
 *  a focusable element — otherwise a screen reader would announce the header "Close, button" as the
 *  entry point of a freshly-opened connect dialog. Falls back to the first focusable, then the
 *  panel itself. No-op if the panel is gone/empty. */
export function focusFirst(panelEl, preferSelector) {
    if (preferSelector && panelEl && typeof panelEl.querySelector === 'function') {
        const preferred = panelEl.querySelector(preferSelector);
        if (preferred && typeof preferred.focus === 'function' && preferred.offsetParent !== null) {
            try { preferred.focus(); return; } catch { /* detached — fall through */ }
        }
    }
    const focusables = getFocusable(panelEl);
    if (focusables.length > 0) {
        try { focusables[0].focus(); } catch { /* detached — ignore */ }
    } else if (panelEl && typeof panelEl.focus === 'function') {
        // The panel itself is tabindex=-1, so it can hold focus and receive Esc even when empty.
        try { panelEl.focus(); } catch { /* ignore */ }
    }
}

/** Install a panel-scoped Tab trap: Tab past the last focusable wraps to the first; Shift+Tab past
 *  the first wraps to the last. Scoped to THIS panel element; when the modal closes the element
 *  leaves the DOM and the listener is GC'd with it. Idempotent per element via a dataset guard. */
export function trapTab(panelEl) {
    if (!panelEl || panelEl.dataset.akmlTrapped === '1') return;
    panelEl.dataset.akmlTrapped = '1';
    panelEl.addEventListener('keydown', (e) => {
        if (e.key !== 'Tab') return;
        const focusables = getFocusable(panelEl);
        if (focusables.length === 0) {
            // Nothing to move to — keep focus on the panel so the modal can't tab out behind the scrim.
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
        } else {
            if (active === last || !panelEl.contains(active)) {
                e.preventDefault();
                try { first.focus(); } catch { /* ignore */ }
            }
        }
    });
}

/** Return focus to whatever held it when the modal opened. Safe no-op if that element is gone. */
export function restoreFocus() {
    const el = _focusBeforeOpen;
    _focusBeforeOpen = null;
    try {
        if (el && typeof el.focus === 'function' && document.contains(el)) {
            el.focus();
        }
    } catch { /* element detached — nothing to restore */ }
}

/** Enumerate the panel's focusable, visible, enabled elements in DOM order. */
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
        // Skip hidden elements (display:none / not laid out): offsetParent is null for those.
        return el.offsetParent !== null || el === document.activeElement;
    });
}

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
// Spec 041 (research X9, N21): the helpers themselves moved to akml-ui.js, which every modal surface
// shares, so the page has ONE focus stack — a dialog opened over the connection manager hands focus
// back to the manager, and the manager back to whatever opened it. This module keeps its exports, so
// ConnectionManagerModal.razor is unchanged.
import * as ui from './akml-ui.js';

/** Remember the element that had focus before the modal opened. An element already inside the
 *  connection-manager panel is not stashed (a second call while the panel holds focus must not
 *  overwrite the real return target). */
export function stashFocus() {
    return ui.stashFocus('.akml-connmgr-panel');
}

/** Move focus into the modal, preferring `preferSelector` (the first form field). */
export const focusFirst = ui.focusFirst;

/** Keep Tab inside the panel. */
export const trapTab = ui.trapTab;

/** Return focus to whatever held it when the modal opened. */
export function restoreFocus() {
    ui.restoreFocus();
}

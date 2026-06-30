// Spec 030 (web edition) — the ⌘P command palette's invocation shim. Consumed by
// AkmlSql.Web.Shared.CommandPalette.razor via IJSRuntime.
//
// WHY a document-level CAPTURE-phase listener (and not Blazor @onkeydown):
//   * @onkeydown is element-scoped. When CodeMirror owns focus the keystroke never reaches a
//     Blazor element handler, so Ctrl/Cmd+P would not fire.
//   * The browser's Print dialog is bound to Ctrl/Cmd+P. To beat it we MUST call
//     preventDefault() SYNCHRONOUSLY inside the native keydown handler — before the await on
//     invokeMethodAsync. A Blazor handler runs after the event has already crossed the
//     async/interop boundary, by which point preventDefault is too late.
//   * Capture phase (addEventListener(..., true)) means we see the event before CodeMirror's
//     own keymap can swallow or act on it.

let _dotNetRef = null;
let _listener = null;
// Captured synchronously at keydown time (focus is still wherever the user was — CodeMirror,
// an input, etc.) so the palette can hand focus back exactly there when it closes.
let _focusBeforeOpen = null;

/**
 * Register the global capture-phase keydown listener.
 * @param {object} dotNetRef  DotNetObjectReference whose .NET object exposes [JSInvokable] Open(bool).
 */
export function init(dotNetRef) {
    if (_listener) return;   // idempotent — never double-register
    _dotNetRef = dotNetRef;

    _listener = (e) => {
        // Ctrl+P (Windows/Linux) or Cmd+P (macOS). Use e.code, NOT e.key: with Shift held,
        // e.key becomes 'P' and an e.key==='p' test would silently miss Ctrl/Cmd+Shift+P
        // (our command-mode). e.code is layout-stable as 'KeyP' regardless of modifiers.
        if (!(e.ctrlKey || e.metaKey) || e.code !== 'KeyP') return;
        if (e.altKey) return;           // leave Ctrl+Alt+P etc. alone
        if (e.repeat) return;           // ignore auto-repeat while the key is held

        // SYNCHRONOUS — must run before the await below to beat the native Print dialog.
        e.preventDefault();
        e.stopPropagation();

        const commandMode = e.shiftKey;            // Shift = Actions-first command mode
        // Stash focus ONLY when it's outside the palette. Re-pressing Ctrl/Cmd+P while the palette
        // is already open must NOT overwrite the stashed element with the palette's own input —
        // otherwise the editor never gets focus back on close.
        const active = document.activeElement;
        if (!active || typeof active.closest !== 'function' || !active.closest('.akml-cmd-panel')) {
            _focusBeforeOpen = active;
        }

        if (_dotNetRef) {
            // Fire-and-forget across the interop boundary — the palette opens on the .NET side.
            _dotNetRef.invokeMethodAsync('Open', commandMode);
        }
    };

    document.addEventListener('keydown', _listener, true /* capture */);
}

/**
 * Restore focus to whatever held it when the palette opened (typically the CodeMirror editor).
 * Called by CommandPalette.razor on close. Safe no-op if the element is gone.
 */
export function restoreFocus() {
    const el = _focusBeforeOpen;
    _focusBeforeOpen = null;
    try {
        if (el && typeof el.focus === 'function' && document.contains(el)) {
            el.focus();
        }
    } catch { /* element detached — nothing to restore */ }
}

/** Trap the palette's non-printable navigation keys ON THE SEARCH INPUT so the browser's own
 *  default actions don't fight the palette: Tab/Shift+Tab would walk focus out of the aria-modal
 *  dialog (no other focusable element inside, so it'd land behind the scrim); ArrowUp/ArrowDown
 *  would jump the text caret to the start/end of the query. We preventDefault ONLY these keys —
 *  printable characters are left untouched so typing still flows to Blazor's @oninput. (An
 *  unconditional @onkeydown:preventDefault would also suppress keydown→beforeinput and break
 *  typing, which is why this is a targeted native listener, not a Blazor directive.)
 *  Attached to the specific input element passed in; when the palette closes the element leaves
 *  the DOM and the listener is GC'd with it — no leak, no manual removal. Idempotent per element. */
export function trapKeys(inputEl) {
    if (!inputEl || inputEl.dataset.akmlTrapped === '1') return;
    inputEl.dataset.akmlTrapped = '1';
    inputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Tab' || e.key === 'ArrowUp' || e.key === 'ArrowDown') {
            e.preventDefault();   // Blazor's @onkeydown handler still runs and owns the behaviour
        }
    });
}

/** Scroll the selected result row into view (block:'nearest') as arrow-key selection moves it
 *  past the height-capped list edge. Called from CommandPalette.razor after Move(). */
export function scrollIntoView(elementId) {
    try {
        const el = document.getElementById(elementId);
        if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'nearest' });
    } catch { /* element gone — no-op */ }
}

/** Remove the listener and drop the DotNetObjectReference. Called from CommandPalette.razor dispose. */
export function dispose() {
    if (_listener) {
        document.removeEventListener('keydown', _listener, true);
        _listener = null;
    }
    _dotNetRef = null;
    _focusBeforeOpen = null;
}

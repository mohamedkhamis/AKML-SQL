// Unit tests for akml-ui.js (spec 041 T013, research X9/R5/N21). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// akml-ui.js holds the focus helpers every modal surface shares (dialog host, context menu, cell
// value dialog, connection manager), the tab-strip key trap and the clipboard write. These pin:
//   * the focus stash is a STACK — a menu that opens a dialog must hand focus back to the menu's
//     opener, not lose it (the old single slot in akml-connection-manager.js did);
//   * copyText falls back to execCommand('copy') when the Clipboard API is unavailable (the web
//     edition is served over plain http on a LAN, which is not a secure context);
//   * trapTabStripKeys cancels only the keys the tab strip owns.
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// ── Minimal DOM stand-ins ─────────────────────────────────────────────────────────────────────
function element(name, inside = null) {
    return {
        name,
        focused: 0,
        focus() { this.focused++; globalThis.document.activeElement = this; },
        closest(sel) { return inside && inside === sel ? {} : null; },
    };
}

let copied;          // what execCommand('copy') "copied"
let execResult;      // what execCommand returns
let appended;        // elements appended to body (and still attached)

beforeEach(() => {
    copied = null;
    execResult = true;
    appended = [];
    globalThis.document = {
        activeElement: null,
        contains(el) { return el !== null && el.detached !== true; },
        body: {
            appendChild(el) { appended.push(el); el.attached = true; },
        },
        createElement(tag) {
            assert.equal(tag, 'textarea');
            return {
                value: '', style: {}, attrs: {},
                setAttribute(k, v) { this.attrs[k] = v; },
                select() { globalThis.document._selected = this; },
                remove() { this.attached = false; },
            };
        },
        execCommand(cmd) {
            assert.equal(cmd, 'copy');
            copied = globalThis.document._selected ? globalThis.document._selected.value : null;
            if (execResult === 'throw') throw new Error('denied');
            return execResult;
        },
    };
    delete globalThis.isSecureContext;
    delete globalThis.navigator;
});

const ui = await import('../../../src/AkmlSql.Web/wwwroot/js/akml-ui.js');

// ── Focus stash stack ─────────────────────────────────────────────────────────────────────────
test('the focus stash is a stack: the last stashed element is restored first', () => {
    const a = element('a');
    const b = element('b');
    document.activeElement = a;
    assert.equal(ui.stashFocus(), true);
    document.activeElement = b;
    assert.equal(ui.stashFocus(), true);

    ui.restoreFocus();
    assert.equal(document.activeElement, b);
    ui.restoreFocus();
    assert.equal(document.activeElement, a);
    ui.restoreFocus();                       // empty stack: a harmless no-op
    assert.equal(document.activeElement, a);
});

test('an element inside the panel is not stashed, so the real return target survives', () => {
    const opener = element('opener');
    const insidePanel = element('field', '.akml-dialog');
    document.activeElement = opener;
    ui.stashFocus('.akml-dialog');
    document.activeElement = insidePanel;
    assert.equal(ui.stashFocus('.akml-dialog'), false);

    ui.restoreFocus();
    assert.equal(document.activeElement, opener);
});

test('a detached element is skipped on restore', () => {
    const gone = element('gone');
    document.activeElement = gone;
    ui.stashFocus();
    gone.detached = true;
    document.activeElement = null;
    ui.restoreFocus();
    assert.equal(gone.focused, 0);
});

// ── copyText ──────────────────────────────────────────────────────────────────────────────────
test('copyText uses the Clipboard API in a secure context', async () => {
    let written = null;
    globalThis.isSecureContext = true;
    globalThis.navigator = { clipboard: { writeText: async (t) => { written = t; } } };

    assert.equal(await ui.copyText('a\tb'), true);
    assert.equal(written, 'a\tb');
    assert.equal(copied, null, 'the legacy path must not run');
});

test('copyText falls back to execCommand when the context is not secure (http on a LAN)', async () => {
    globalThis.isSecureContext = false;
    globalThis.navigator = { clipboard: { writeText: async () => { throw new Error('should not be called'); } } };
    const grid = element('grid');
    document.activeElement = grid;

    assert.equal(await ui.copyText('NULL\t1'), true);
    assert.equal(copied, 'NULL\t1');
    assert.equal(appended.length, 1);
    assert.equal(appended[0].attached, false, 'the helper textarea is removed');
    assert.equal(document.activeElement, grid, 'focus goes back to the element that had it');
});

test('copyText falls back when the Clipboard API rejects', async () => {
    globalThis.isSecureContext = true;
    globalThis.navigator = { clipboard: { writeText: async () => { throw new Error('denied'); } } };

    assert.equal(await ui.copyText('x'), true);
    assert.equal(copied, 'x');
});

test('copyText reports false when every path fails', async () => {
    globalThis.isSecureContext = false;
    execResult = 'throw';
    assert.equal(await ui.copyText('x'), false);

    execResult = false;
    assert.equal(await ui.copyText('x'), false);
});

test('copyText copies null as an empty string', async () => {
    globalThis.isSecureContext = false;
    assert.equal(await ui.copyText(null), true);
    assert.equal(copied, '');
});

// ── trapTabStripKeys ──────────────────────────────────────────────────────────────────────────
function stripElement() {
    const listeners = [];
    return {
        dataset: {},
        addEventListener(type, fn) { assert.equal(type, 'keydown'); listeners.push(fn); },
        fire(key) {
            const e = { key, prevented: false, preventDefault() { this.prevented = true; } };
            listeners.forEach(fn => fn(e));
            return e.prevented;
        },
        count: () => listeners.length,
    };
}

test('trapTabStripKeys cancels only the keys the tab strip owns', () => {
    const el = stripElement();
    ui.trapTabStripKeys(el);
    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
        assert.equal(el.fire(key), true, `${key} is owned by the tab strip`);
    }
    for (const key of ['Tab', 'Enter', ' ', 'ArrowDown', 'ArrowUp', 'a', 'Escape']) {
        assert.equal(el.fire(key), false, `${key} keeps its default`);
    }
});

test('trapTabStripKeys is idempotent per element', () => {
    const el = stripElement();
    ui.trapTabStripKeys(el);
    ui.trapTabStripKeys(el);
    assert.equal(el.count(), 1);
});

test('trapTabStripKeys tolerates a missing element', () => {
    assert.doesNotThrow(() => ui.trapTabStripKeys(null));
});

// ── clampToViewport (context menus) ───────────────────────────────────────────────────────────
test('clampToViewport keeps a menu inside the window, flipping at the right and bottom edges', () => {
    assert.deepEqual(ui.clampToViewport(10, 20, 200, 100, 1000, 800), { left: 10, top: 20 });
    // Would overflow right: open to the left of the pointer instead.
    assert.deepEqual(ui.clampToViewport(950, 20, 200, 100, 1000, 800), { left: 750, top: 20 });
    // Would overflow bottom: open above the pointer.
    assert.deepEqual(ui.clampToViewport(10, 780, 200, 100, 1000, 800), { left: 10, top: 680 });
    // Bigger than the window: pinned to the top-left margin.
    assert.deepEqual(ui.clampToViewport(10, 10, 2000, 2000, 1000, 800), { left: 4, top: 4 });
});

// Spec 041 (T175) — the Open command clicks the editor's hidden file input, as the toolbar's Open label does.
test('clickElement clicks the element with that id, and is quiet when there is none', () => {
    let clicks = 0;
    globalThis.document.getElementById = (id) => (id === 'akml-doc-open-input' ? { click() { clicks++; } } : null);

    assert.equal(ui.clickElement('akml-doc-open-input'), true);
    assert.equal(clicks, 1);
    assert.equal(ui.clickElement('nothing-here'), false);
});

// Unit tests for F6 pane cycling in akml-workspace.js (spec 041 T126, contracts/ui.md §2). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// F6 moves focus editor → results → Schema → AI → toolbar and round again; Shift+F6 goes back. A region
// that is hidden (or folded) is skipped; from outside every region, F6 starts at the editor.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PANE_ORDER, nextPane, focusRegion } from '../../../src/AkmlSql.Web/wwwroot/js/akml-workspace.js';

const all = () => true;

test('the panes cycle editor, results, Schema, AI, toolbar', () => {
    assert.deepEqual(PANE_ORDER, ['centre', 'results', 'schema', 'ai', 'toolbar']);
    assert.equal(nextPane('centre', false, all), 'results');
    assert.equal(nextPane('results', false, all), 'schema');
    assert.equal(nextPane('schema', false, all), 'ai');
    assert.equal(nextPane('ai', false, all), 'toolbar');
    assert.equal(nextPane('toolbar', false, all), 'centre');
});

test('Shift+F6 goes back', () => {
    assert.equal(nextPane('centre', true, all), 'toolbar');
    assert.equal(nextPane('results', true, all), 'centre');
    assert.equal(nextPane('toolbar', true, all), 'ai');
});

test('a hidden region is skipped', () => {
    const shown = new Set(['centre', 'results', 'toolbar']);
    assert.equal(nextPane('results', false, p => shown.has(p)), 'toolbar');
    assert.equal(nextPane('toolbar', true, p => shown.has(p)), 'results');
});

test('from outside every region F6 starts at the editor, Shift+F6 at the toolbar', () => {
    assert.equal(nextPane(null, false, all), 'centre');
    assert.equal(nextPane(null, true, all), 'toolbar');
});

test('nothing focusable: no pane', () => {
    assert.equal(nextPane('centre', false, () => false), null);
});

function fakeEl({ inner = null, tabindex = false } = {}) {
    const el = {
        focused: false,
        querySelector: () => inner,
        hasAttribute: name => name === 'tabindex' && tabindex,
        focus() { el.focused = true; },
    };
    return el;
}

test('a pane focuses its first focusable control', () => {
    const button = fakeEl();
    const pane = fakeEl({ inner: button, tabindex: true });
    assert.equal(focusRegion(pane), true);
    assert.equal(button.focused, true);
    assert.equal(pane.focused, false);
});

test('a pane with nothing focusable in it (an empty Schema tree) takes focus itself', () => {
    const pane = fakeEl({ tabindex: true });
    assert.equal(focusRegion(pane), true);
    assert.equal(pane.focused, true);
});

test('a plain element with nothing focusable is passed over', () => {
    assert.equal(focusRegion(fakeEl()), false);
    assert.equal(focusRegion(null), false);
});

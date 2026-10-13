// Unit tests for the splitter helpers in akml-workspace.js (spec 041 T114, research R35). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// A splitter drag resizes the panel on its side between the panel's limits; the keyboard side cancels only
// the keys the separator handles, never Tab, so keyboard focus can always leave (FR-103).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clamp, dragSize, isSeparatorKey, trapSeparatorKeys, attachSplitter, detachSplitter } from '../../../src/AkmlSql.Web/wwwroot/js/akml-workspace.js';

test('clamp keeps a size between the limits', () => {
    assert.equal(clamp(180, 600, 300), 300);
    assert.equal(clamp(180, 600, 40), 180);
    assert.equal(clamp(180, 600, 900), 600);
    assert.equal(clamp(180, 100, 150), 180, 'a max below the min gives the min');
});

test('the Schema panel grows to the right, the AI panel to the left, the results pane upwards', () => {
    assert.equal(dragSize('left', 260, 40, 0), 300);
    assert.equal(dragSize('left', 260, -60, 0), 200);
    assert.equal(dragSize('right', 380, 40, 0), 340);
    assert.equal(dragSize('right', 380, -20, 0), 400);
    assert.equal(dragSize('bottom', 300, 0, -50), 350);
    assert.equal(dragSize('bottom', 300, 99, 30), 270, 'a vertical splitter ignores dx');
});

test('the separator cancels Arrow, Home, End, Space and Enter — never Tab', () => {
    for (const k of ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', ' ', 'Enter']) {
        assert.equal(isSeparatorKey(k), true, k);
    }
    for (const k of ['Tab', 'Escape', 'F6', 'a']) {
        assert.equal(isSeparatorKey(k), false, k);
    }
});

test('trapSeparatorKeys prevents exactly those keys', () => {
    let listener = null;
    const el = { addEventListener: (type, fn) => { if (type === 'keydown') listener = fn; } };
    trapSeparatorKeys(el);

    const press = (key) => {
        let prevented = false;
        listener({ key, preventDefault: () => { prevented = true; } });
        return prevented;
    };
    assert.equal(press('ArrowLeft'), true);
    assert.equal(press('Enter'), true);
    assert.equal(press('Tab'), false);

    trapSeparatorKeys(el);   // a second call installs nothing more
});

test('a splitter whose host is not known yet finds it as its nearest data-splitter-host ancestor', () => {
    // Blazor hands a child component its parent's @ref only after the first render, so the splitter's first
    // attach gets a null host; without the fallback the drag never worked in a browser.
    const host = { style: { setProperty() {}, getPropertyValue: () => '260px' }, classList: { add() {}, remove() {} } };
    const listeners = {};
    const el = {
        closest: (sel) => (sel === '[data-splitter-host]' ? host : null),
        addEventListener: (type, fn) => { listeners[type] = fn; },
        removeEventListener: (type) => { delete listeners[type]; },
    };

    attachSplitter(el, null, '--akml-ws-left', 'left', 180, 600, { invokeMethodAsync: () => Promise.resolve() }, 'left');

    assert.equal(typeof listeners.pointerdown, 'function');
    assert.equal(typeof listeners.dblclick, 'function');
    detachSplitter(el);
    assert.equal(listeners.pointerdown, undefined);
});

test('a splitter with no host anywhere attaches nothing', () => {
    const listeners = {};
    const el = { closest: () => null, addEventListener: (type, fn) => { listeners[type] = fn; } };
    attachSplitter(el, null, '--x', 'left', 0, 10, null, 'x');
    assert.deepEqual(Object.keys(listeners), []);
});

// Unit tests for the pure helpers in akml-results-grid.js (spec 041 T053, research R2-R4/R7). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// The results grid does pointer work in JS (live column drag, drag-select, the context-menu event) and
// keeps all state in .NET. These pin the decisions that are easy to get subtly wrong: rewriting one
// track of the shared --akml-grid-cols list, which keys the grid claims (and which it must leave to the
// inline cell editor and to Tab), and when a press on a column edge is a click rather than a drag.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { replaceTrack, readTrack, shouldTrapKey, isClick, isKeyboardMenu } from '../../../src/AkmlSql.Web/wwwroot/js/akml-results-grid.js';

const key = (k, mods = {}) => ({ key: k, ctrlKey: !!mods.ctrl, shiftKey: !!mods.shift, altKey: !!mods.alt, metaKey: !!mods.meta });

test('replaceTrack rewrites one track and keeps the others', () => {
    assert.equal(replaceTrack('36px 72px 236px 480px', 2, 300), '36px 72px 300px 480px');
    assert.equal(replaceTrack('36px 72px', 0, 44), '44px 72px');
});

test('replaceTrack rounds and ignores an index out of range', () => {
    assert.equal(replaceTrack('36px 72px', 1, 99.6), '36px 100px');
    assert.equal(replaceTrack('36px 72px', 5, 99), '36px 72px');
});

test('readTrack reads one track in pixels', () => {
    assert.equal(readTrack('36px 72px 236px', 2), 236);
    assert.equal(readTrack('36px 72px', 9), null);
    assert.equal(readTrack('', 0), null);
});

test('the grid claims its navigation, selection and copy keys', () => {
    for (const k of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown']) {
        assert.equal(shouldTrapKey(key(k), false), true, k);
        assert.equal(shouldTrapKey(key(k, { shift: true }), false), true, `Shift+${k}`);
    }
    assert.equal(shouldTrapKey(key('a', { ctrl: true }), false), true, 'Ctrl+A selects the grid, not the page');
    assert.equal(shouldTrapKey(key('c', { ctrl: true }), false), true, 'Ctrl+C copies the selection');
    assert.equal(shouldTrapKey(key('C', { ctrl: true, shift: true }), false), true, 'Ctrl+Shift+C (DevTools inspect)');
    assert.equal(shouldTrapKey(key('0', { ctrl: true }), false), true, 'Ctrl+0 sets NULL (browser zoom reset)');
    assert.equal(shouldTrapKey(key('ArrowRight', { shift: true, alt: true }), false), true, 'Shift+Alt+Arrow resizes');
    assert.equal(shouldTrapKey(key(' '), false), true, 'Space must not scroll the grid');
});

test('the Menu key and Shift+F10 are left alone: the contextmenu event they fire opens the menu', () => {
    assert.equal(shouldTrapKey(key('F10', { shift: true }), false), false);
    assert.equal(shouldTrapKey(key('ContextMenu'), false), false);
});

test('a contextmenu event aimed at the grid root, or at 0,0, came from the keyboard', () => {
    assert.equal(isKeyboardMenu(true, 412, 230), true, 'the focused element is the root, never a cell');
    assert.equal(isKeyboardMenu(false, 0, 0), true, 'some browsers report keyboard menus at 0,0');
    assert.equal(isKeyboardMenu(false, 412, 230), false, 'a right-click on a cell');
});

test('the grid leaves Tab, F5, Enter and Ctrl+V alone', () => {
    assert.equal(shouldTrapKey(key('Tab'), false), false, 'Tab leaves the grid');
    assert.equal(shouldTrapKey(key('Tab', { shift: true }), false), false);
    assert.equal(shouldTrapKey(key('F5'), false), false, 'the page key handler owns F5');
    assert.equal(shouldTrapKey(key('Enter'), false), false);
    assert.equal(shouldTrapKey(key('v', { ctrl: true }), false), false);
});

test('a printable key on the grid is claimed: .NET starts the edit with it, so it must not be typed twice', () => {
    assert.equal(shouldTrapKey(key('x'), false), true);
    assert.equal(shouldTrapKey(key('7'), false), true);
    assert.equal(shouldTrapKey(key('X', { shift: true }), false), true);
    assert.equal(shouldTrapKey(key('x'), true), false, 'inside the inline editor the input types it');
    assert.equal(shouldTrapKey(key('x', { alt: true }), false), false, 'Alt+letter belongs to the browser');
});

test('while the inline editor has focus only Tab, Enter and Escape are claimed', () => {
    assert.equal(shouldTrapKey(key('Tab'), true), true);
    assert.equal(shouldTrapKey(key('Tab', { shift: true }), true), true);
    assert.equal(shouldTrapKey(key('Enter'), true), true);
    assert.equal(shouldTrapKey(key('Escape'), true), true);
    assert.equal(shouldTrapKey(key('ArrowLeft'), true), false, 'arrows move the caret in the input');
    assert.equal(shouldTrapKey(key('a', { ctrl: true }), true), false, 'Ctrl+A selects the input text');
    assert.equal(shouldTrapKey(key('c', { ctrl: true }), true), false);
    assert.equal(shouldTrapKey(key('0', { ctrl: true }), true), false);
});

test('a press that moves under 3 px is a click, so the edge double-click still arrives', () => {
    assert.equal(isClick(0, 0), true);
    assert.equal(isClick(2, -2), true);
    assert.equal(isClick(3, 0), false);
    assert.equal(isClick(0, -5), false);
});

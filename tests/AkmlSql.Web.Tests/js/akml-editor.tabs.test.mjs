// Unit tests for the query tabs' editor states in akml-editor.js (spec 043 T020, research R6, spike S2). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// The page has one CodeMirror view; each query tab keeps its own EditorState (text, selection, undo history, findings),
// stored while another tab is shown and put back with view.setState, which runs no update listener, so a swap never
// reports a text change. These pin the pure parts; the view swap itself is covered by the browser QueryTabsTests.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { stashAndPick, replaceDocIfUnchanged, replaceDoc, withDiagnostics } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

const extensions = [cm.commands.history(), cm.lint.lintGutter()];
const make = (doc) => cm.state.EditorState.create({ doc, extensions });

function type(state, text) {
    const end = state.doc.length;
    return state.update({ changes: { from: end, insert: text }, userEvent: 'input.type' }).state;
}

function undo(state) {
    let next = state;
    cm.commands.undo({ state, dispatch: tr => { next = tr.state; } });
    return next;
}

test('stashing the shown tab and picking another keeps both, and picking back returns the first as it was', () => {
    const tabs = new Map();
    const a = type(make('SELECT 1'), ' -- a');

    const b = stashAndPick(tabs, 'tab-a', { state: a, scrollTop: 40, scrollLeft: 0 }, 'tab-b', () => make('SELECT 2'));
    assert.equal(b.state.doc.toString(), 'SELECT 2');   // a fresh state for a tab never shown
    assert.ok(tabs.has('tab-a'));
    assert.ok(!tabs.has('tab-b'), 'the shown tab lives in the view, not in the map');

    const back = stashAndPick(tabs, 'tab-b', { state: b.state, scrollTop: 0, scrollLeft: 0 }, 'tab-a', () => { throw new Error('tab-a is stored'); });
    assert.equal(back.state, a);
    assert.equal(back.scrollTop, 40);
    assert.ok(tabs.has('tab-b'));
});

test('undo still works on a tab after it was swapped out and back', () => {
    const tabs = new Map();
    const a = type(make('SELECT 1'), ' -- typed');
    stashAndPick(tabs, 'tab-a', { state: a, scrollTop: 0, scrollLeft: 0 }, 'tab-b', () => make(''));
    const back = stashAndPick(tabs, 'tab-b', { state: make(''), scrollTop: 0, scrollLeft: 0 }, 'tab-a', () => make('wrong'));

    assert.equal(undo(back.state).doc.toString(), 'SELECT 1');
});

test('the first pick, with nothing shown before, stores nothing', () => {
    const tabs = new Map();
    const first = stashAndPick(tabs, null, null, 'tab-a', () => make('SELECT 1'));

    assert.equal(first.state.doc.toString(), 'SELECT 1');
    assert.equal(tabs.size, 0);
});

test('a Format reply for a tab not shown replaces its stored text only if it is still what was formatted', () => {
    const state = make('select 1');

    const formatted = replaceDocIfUnchanged(state, 'select 1', 'SELECT 1;');
    assert.equal(formatted.doc.toString(), 'SELECT 1;');
    assert.equal(undo(formatted).doc.toString(), 'select 1', 'one undoable change');

    assert.equal(replaceDocIfUnchanged(state, 'select 2', 'SELECT 2;'), null, 'the text moved on: nothing changes');
});

test('a stored tab takes a whole new text', () => {
    assert.equal(replaceDoc(make('a'), 'b c').doc.toString(), 'b c');
});

test('findings for a stored tab go into its state and come back with it', () => {
    const state = make('SELECT *\nFROM t');
    const shown = withDiagnostics(cm, state, [{ line: 2, severity: 2, message: 'Avoid SELECT *' }]);

    assert.equal(cm.lint.diagnosticCount(shown), 1);
    assert.equal(cm.lint.diagnosticCount(withDiagnostics(cm, shown, [])), 0);
});

// Review WEB-10: a Format reply replaces the document only if it still reads what was sent. Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// The check and the change happen in one turn and as one transaction, so the format stays a single undo step and no
// keystroke can land between them. A view here is the part of CodeMirror's EditorView the helper uses: state + dispatch.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { replaceIfUnchanged } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

function fakeView(doc) {
    const view = {
        state: cm.state.EditorState.create({ doc }),
        transactions: 0,
        dispatch(spec) { this.transactions++; this.state = this.state.update(spec).state; },
    };
    return view;
}

test('an unchanged document is replaced in one transaction', () => {
    const view = fakeView('select 1');

    assert.equal(replaceIfUnchanged(view, 'select 1', 'SELECT 1;'), true);
    assert.equal(view.state.doc.toString(), 'SELECT 1;');
    assert.equal(view.transactions, 1);
});

test('a document typed into since the format was sent is left alone', () => {
    const view = fakeView('select 1 -- and more');

    assert.equal(replaceIfUnchanged(view, 'select 1', 'SELECT 1;'), false);
    assert.equal(view.state.doc.toString(), 'select 1 -- and more');
    assert.equal(view.transactions, 0);
});

test('no view, or a null expectation against a non-empty document, changes nothing', () => {
    assert.equal(replaceIfUnchanged(undefined, 'x', 'y'), false);
    const view = fakeView('text');
    assert.equal(replaceIfUnchanged(view, null, 'y'), false);
    assert.equal(view.state.doc.toString(), 'text');
});

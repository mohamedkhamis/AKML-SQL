// Unit tests for the editor options in akml-editor.js (spec 041 T147, research R53, FR-053). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// Settings › Editor: font size 9–32 (CSS only), word wrap and tab size 1–8 (CodeMirror compartments, so a
// change reconfigures the open editor without recreating it).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normaliseEditorOptions, optionExtensions, caretPosition, editorFontStack, applyEditorAppearance } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

test('missing options are the defaults: 13 px, no wrap, tab 4, the default font', () => {
    assert.deepEqual(normaliseEditorOptions(undefined), { fontSize: 13, wordWrap: false, tabSize: 4, fontFamily: 'default' });
    assert.deepEqual(normaliseEditorOptions({}), { fontSize: 13, wordWrap: false, tabSize: 4, fontFamily: 'default' });
});

test('sizes are kept within their limits', () => {
    assert.deepEqual(normaliseEditorOptions({ fontSize: 2, wordWrap: 1, tabSize: 99 }), { fontSize: 9, wordWrap: true, tabSize: 8, fontFamily: 'default' });
    assert.deepEqual(normaliseEditorOptions({ fontSize: 40, tabSize: 0 }), { fontSize: 32, wordWrap: false, tabSize: 1, fontFamily: 'default' });
    assert.equal(normaliseEditorOptions({ fontSize: 'x' }).fontSize, 13);
});

// Spec 043 (T061, research R12, FR-017) — the font list of Settings › Editor.
test('the font is one of the offered names; anything else is the default', () => {
    for (const name of ['default', 'cascadia-mono', 'consolas', 'system-monospace']) {
        assert.equal(normaliseEditorOptions({ fontFamily: name }).fontFamily, name);
    }
    assert.equal(normaliseEditorOptions({ fontFamily: 'Comic Sans MS' }).fontFamily, 'default');
    assert.equal(normaliseEditorOptions({ fontFamily: 7 }).fontFamily, 'default');
});

test('each offered font is a stack ending in monospace; the default is the theme token (no override)', () => {
    assert.equal(editorFontStack('default'), null);
    assert.equal(editorFontStack('cascadia-mono'), "'Cascadia Mono', 'Cascadia Code', Consolas, monospace");
    assert.equal(editorFontStack('consolas'), "Consolas, 'Cascadia Mono', monospace");
    assert.equal(editorFontStack('system-monospace'), 'ui-monospace, monospace');
    assert.equal(editorFontStack('unknown'), null);
});

test('the appearance sets the size and the font on an element, and the default font removes the override', () => {
    const props = new Map();
    const el = { style: { setProperty: (k, v) => props.set(k, v), removeProperty: k => props.delete(k) } };

    applyEditorAppearance(el, { fontSize: 15, fontFamily: 'consolas' });
    assert.equal(props.get('--akml-editor-font-size'), '15px');
    assert.equal(props.get('--akml-editor-font-family'), "Consolas, 'Cascadia Mono', monospace");

    applyEditorAppearance(el, { fontSize: 15, fontFamily: 'default' });
    assert.equal(props.has('--akml-editor-font-family'), false);
    assert.equal(props.get('--akml-editor-font-size'), '15px');

    applyEditorAppearance(null, { fontSize: 15 });   // no element: nothing to do, no throw
});

test('the tab size sets both CodeMirror tab size and the indent unit', () => {
    const ext = optionExtensions(cm, { tabSize: 2 });
    const state = cm.state.EditorState.create({ doc: '', extensions: [ext.tab] });

    assert.equal(state.tabSize, 2);
    assert.equal(state.facet(cm.language.indentUnit), '  ');
});

test('word wrap is CodeMirror line wrapping, off is nothing', () => {
    assert.equal(optionExtensions(cm, { wordWrap: true }).wrap, cm.view.EditorView.lineWrapping);
    assert.deepEqual(optionExtensions(cm, { wordWrap: false }).wrap, []);
});

// Spec 041 (T195, R65) — the status bar's caret.
test('the caret position is 1-based line and column', () => {
    const doc = cm.state.Text.of(['SELECT 1', '\tFROM t']);
    assert.deepEqual(caretPosition(doc, 0), { line: 1, column: 1 });
    assert.deepEqual(caretPosition(doc, 8), { line: 1, column: 9 });
    assert.deepEqual(caretPosition(doc, 10), { line: 2, column: 2 });   // after the tab
    assert.deepEqual(caretPosition(doc, 999), { line: 2, column: 8 });  // clamped to the end
});

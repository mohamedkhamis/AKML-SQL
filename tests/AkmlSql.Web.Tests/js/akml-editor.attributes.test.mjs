// Unit tests for the writing-assistant opt-out in akml-editor.js (spec 043 T092, research R14, FR-036). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// CodeMirror 6.43 already puts spellcheck="false" autocorrect="off" autocapitalize="off" on .cm-content; what it
// does not set are the opt-outs browser writing assistants read. Grammarly has used three spellings over the years
// (data-gramm, data-gramm_editor, data-enable-grammarly); LanguageTool reads data-lt-active.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WRITING_ASSISTANT_OPT_OUT, writingAssistantOptOut, editorNameAttributes, showTab } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

test('the opt-out names the four assistant attributes, each "false"', () => {
    assert.deepEqual(WRITING_ASSISTANT_OPT_OUT, {
        'data-gramm': 'false',
        'data-gramm_editor': 'false',
        'data-enable-grammarly': 'false',
        'data-lt-active': 'false',
    });
});

test('the extension puts them on the editor content', () => {
    const state = cm.state.EditorState.create({ doc: 'SELECT 1', extensions: [writingAssistantOptOut(cm)] });
    const attrs = Object.assign({}, ...state.facet(cm.view.EditorView.contentAttributes)
        .map(a => (typeof a === 'function' ? a() : a)));

    for (const [name, value] of Object.entries(WRITING_ASSISTANT_OPT_OUT)) {
        assert.equal(attrs[name], value, name);
    }
});

test('the attribute object cannot be changed by a caller', () => {
    assert.ok(Object.isFrozen(WRITING_ASSISTANT_OPT_OUT));
});

// Spec 047 (WEB-007, contracts/cli-docs-a11y.md §4): the editor has an accessible name, which names the query tab shown, so a
// screen reader announces "SQL query editor — SQLQuery1, edit text" instead of an unnamed text box.
function contentAttrs(state) {
    return Object.assign({}, ...state.facet(cm.view.EditorView.contentAttributes).map(a => (typeof a === 'function' ? a() : a)));
}

test('the editor is a named multi-line text box for its query tab', () => {
    const state = cm.state.EditorState.create({ doc: 'SELECT 1', extensions: [editorNameAttributes(cm, 'SQLQuery1')] });
    const attrs = contentAttrs(state);

    assert.equal(attrs['aria-label'], 'SQL query editor — SQLQuery1');
    assert.equal(attrs['aria-multiline'], 'true');
    assert.equal(attrs.role, 'textbox');
});

test('without a tab name it is still named', () => {
    const state = cm.state.EditorState.create({ doc: '', extensions: [editorNameAttributes(cm, '  ')] });
    assert.equal(contentAttrs(state)['aria-label'], 'SQL query editor');
});

test('the name follows the query tab shown', () => {
    const nameCompartment = new cm.state.Compartment();
    const extensions = [nameCompartment.of(editorNameAttributes(cm, 'SQLQuery1'))];
    const view = {
        state: cm.state.EditorState.create({ doc: '', extensions }),
        scrollDOM: { scrollTop: 0, scrollLeft: 0 },
        setState(s) { this.state = s; },
        dispatch(spec) { this.state = this.state.update(spec).state; },
    };
    const inst = {
        view, cm, extensions, tabs: new Map(), currentTab: null, kind: 'sqlserver', nameCompartment,
        dialectCompartment: new cm.state.Compartment(), wrapCompartment: new cm.state.Compartment(), tabCompartment: new cm.state.Compartment(),
        optionExt: { wrap: [], tab: [] },
    };
    // The stored states are built from inst.extensions; the dialect/wrap/tab compartments must be in them too.
    inst.extensions.push(inst.dialectCompartment.of([]), inst.wrapCompartment.of([]), inst.tabCompartment.of([]));

    showTab(inst, 'tab-1', 'SELECT 1', 'sqlserver', 'SQLQuery1');
    assert.equal(contentAttrs(inst.view.state)['aria-label'], 'SQL query editor — SQLQuery1');
    showTab(inst, 'tab-2', 'SELECT 2', 'sqlserver', 'Orders report');
    assert.equal(contentAttrs(inst.view.state)['aria-label'], 'SQL query editor — Orders report');
    showTab(inst, 'tab-1', null, 'sqlserver', 'SQLQuery1');
    assert.equal(contentAttrs(inst.view.state)['aria-label'], 'SQL query editor — SQLQuery1');
});

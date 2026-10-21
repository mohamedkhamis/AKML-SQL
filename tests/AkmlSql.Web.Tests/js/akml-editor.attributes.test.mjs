// Unit tests for the writing-assistant opt-out in akml-editor.js (spec 043 T092, research R14, FR-036). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// CodeMirror 6.43 already puts spellcheck="false" autocorrect="off" autocapitalize="off" on .cm-content; what it
// does not set are the opt-outs browser writing assistants read. Grammarly has used three spellings over the years
// (data-gramm, data-gramm_editor, data-enable-grammarly); LanguageTool reads data-lt-active.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WRITING_ASSISTANT_OPT_OUT, writingAssistantOptOut } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

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

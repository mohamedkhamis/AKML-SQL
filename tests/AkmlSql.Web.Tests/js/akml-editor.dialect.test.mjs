// Unit tests for the SQL dialect of the editor in akml-editor.js (spec 042 T020, FR-060, FR-065). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// The editor language is one CodeMirror compartment. PostgreSQL gets its own dialect; SQL Server, and no
// connection at all, keep exactly the language the editor had before the feature.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { dialectExtension } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

/** The node names of the syntax tree for `doc` under a dialect extension (names only; whitespace is skipped). */
function nodeNames(kind, doc) {
    const state = cm.state.EditorState.create({ doc, extensions: [dialectExtension(cm, kind)] });
    const names = [];
    cm.language.syntaxTree(state).iterate({
        enter: n => { if (n.name !== 'Script' && n.name !== 'Statement') names.push(doc.slice(n.from, n.to) + ':' + n.name); },
    });
    return names;
}

test('the bundle exports the PostgreSQL dialect the editor needs', () => {
    assert.ok(cm.langSql.PostgreSQL, 'langSql.PostgreSQL');
    assert.equal(typeof cm.langSql.sql, 'function');
});

test('PostgreSQL treats a dollar-quoted body as one string, the default language does not', () => {
    const doc = "SELECT $$it's; fine$$";

    assert.ok(nodeNames('postgresql', doc).includes("$$it's; fine$$:String"), nodeNames('postgresql', doc).join(' | '));
    assert.ok(!nodeNames('sqlserver', doc).includes("$$it's; fine$$:String"));
});

test('PostgreSQL knows its own keywords', () => {
    const doc = 'SELECT a FROM t WHERE b ILIKE c';

    assert.ok(nodeNames('postgresql', doc).includes('ILIKE:Keyword'), nodeNames('postgresql', doc).join(' | '));
});

test('anything that is not postgresql is the language the editor already had', () => {
    const baseline = nodeNames(undefined, "SELECT 'x', [col] FROM t");

    for (const kind of ['sqlserver', 'SQLSERVER', '', null, undefined, 'oracle', 42]) {
        assert.deepEqual(nodeNames(kind, "SELECT 'x', [col] FROM t"), baseline, `kind ${String(kind)}`);
    }
});

test('the kind is matched case-insensitively and ignoring surrounding spaces', () => {
    const doc = "SELECT $$a$$";

    assert.deepEqual(nodeNames(' PostgreSQL ', doc), nodeNames('postgresql', doc));
});

test('switching the compartment keeps the document', () => {
    const compartment = new cm.state.Compartment();
    let state = cm.state.EditorState.create({ doc: 'SELECT 1', extensions: [compartment.of(dialectExtension(cm, 'sqlserver'))] });

    state = state.update({ effects: compartment.reconfigure(dialectExtension(cm, 'postgresql')) }).state;

    assert.equal(state.doc.toString(), 'SELECT 1');
});

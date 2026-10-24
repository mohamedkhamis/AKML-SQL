// Unit tests for the wildcard Tab expands in akml-editor.js (spec 030, spec 043 follow-up review). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// Tab on a select-list '*' asks the engine for the columns, and the key is consumed before the reply comes back. On SQL Server
// nothing downstream checks what the star is — the engine takes the FROM tables' columns for any star — so wildcardAtCaret must
// say yes only to a star that starts a select-list item: with the caret right after it or right before it, never a product,
// COUNT's argument or a star in a comment, a string or a quoted name. The states carry the editor's SQL language, so the
// syntax tree is there as it is in the page. A decline that comes back at once still lets Tab indent (declinedTabMayIndent).
// On PostgreSQL (the editor's kind passed as the page passes it) a RETURNING list is a select list too.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
    wildcardAtCaret, selectListStar, declinedTabMayIndent, WILDCARD_TAB_FALLBACK_MS, dialectExtension,
} from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

const cm = await import('../../../src/AkmlSql.Web/wwwroot/lib/codemirror/akml-cm.js');

/** A state for `marked`, the caret where the '|' is (the '|' is not part of the text). */
function at(marked, kind) {
    const anchor = marked.indexOf('|');
    assert.ok(anchor >= 0, `no caret marker in ${marked}`);
    const doc = marked.slice(0, anchor) + marked.slice(anchor + 1);
    return cm.state.EditorState.create({ doc, selection: { anchor }, extensions: [dialectExtension(cm, kind)] });
}

/** The same without a language, so there is no syntax tree: the text alone decides. */
function plain(marked) {
    const anchor = marked.indexOf('|');
    return cm.state.EditorState.create({ doc: marked.replace('|', ''), selection: { anchor } });
}

/** The offset just after the star the caret is at in `marked` (the offset the engine is asked with). */
function afterStar(marked) {
    const caret = marked.indexOf('|');
    const doc = marked.replace('|', '');
    return doc.charAt(caret - 1) === '*' ? caret : caret + 1;
}

/** Both caret positions of the one star in `sql`: just after it and just before it. */
function bothSides(sql) {
    const star = sql.indexOf('*');
    return [sql.slice(0, star + 1) + '|' + sql.slice(star + 1), sql.slice(0, star) + '|' + sql.slice(star)];
}

for (const marked of [
    'SELECT *|',
    'SELECT |*',
    'SELECT |* FROM t',
    'SELECT *| FROM t',
    'SELECT t.|*',
    'SELECT t.*|',
    'SELECT [t].|*',
    'SELECT [t].|* FROM [t]',
    'SELECT "t".|* FROM t',
    'SELECT dbo.Orders.|* FROM dbo.Orders',
    'SELECT a, |* FROM t',
    'SELECT a,|* FROM t',
    'SELECT *|, x FROM t',
    'SELECT DISTINCT |* FROM t',
    'SELECT ALL |* FROM t',
    'SELECT TOP 10 |* FROM t',
    'SELECT TOP @n |* FROM t',
    'SELECT TOP (5) PERCENT |* FROM t',
    'SELECT TOP (@n) PERCENT WITH TIES |* FROM t ORDER BY a',
    'SELECT DISTINCT TOP 5 o.|* FROM dbo.Orders AS o',
    'SELECT\n    *|\nFROM t',
    'SELECT\n|*\nFROM t',
    'SELECT *| -- every column\nFROM t',
    'SELECT /* all */ |* FROM t',
    'SELECT |* INTO #copy FROM t',
    'SELECT |*;',
    'SELECT |*\nGO',
    'SELECT CASE WHEN a = 1 THEN 2 * 3 END, |* FROM t',
    'SELECT (SELECT COUNT(*) FROM u), |* FROM t',
    'SELECT x FROM (SELECT |* FROM t) AS d',
    'SELECT a FROM t UNION SELECT |* FROM u',
    'WITH c AS (SELECT 1 AS x) SELECT |* FROM c',
    'SELECT a FROM t\nSELECT |* FROM u',
    'USE [Northwind]\n\n        SELECT    |* FROM dbo.Customers;',   // the reported script
    // Spec 043 follow-up (review): only a GO line ends a batch; a 'go' anywhere else is a name.
    'SELECT go, |* FROM t',
    'SELECT a,\n    go AS x, |* FROM t',
    'SELECT a,\nGO,\n|* FROM u',
    'SELECT |*\n  GO',
    'SELECT |*\nGO 3 -- again',
    'SELECT a FROM t\nGO 2 -- twice\nSELECT |* FROM u',
]) {
    test(`a select-list star is expanded: ${JSON.stringify(marked)}`, () => {
        assert.deepEqual(wildcardAtCaret(cm, at(marked)), { caretOffset: afterStar(marked) });
    });
}

test('the engine is asked with the caret after the star, whichever side of it the caret is on', () => {
    assert.deepEqual(wildcardAtCaret(cm, at('SELECT |* FROM t')), { caretOffset: 8 });
    assert.deepEqual(wildcardAtCaret(cm, at('SELECT *| FROM t')), { caretOffset: 8 });
});

// Finding 10: on SQL Server the engine expands whatever star it is asked about, so these would have lost their expression.
for (const sql of [
    'SELECT 2 * 3',
    'SELECT price * qty AS total FROM dbo.Orders',
    'SELECT price*qty FROM t',
    'SELECT COUNT(*) FROM t',
    'SELECT a*b FROM t',
    'SELECT a *b',
    'SELECT COALESCE(a, *) FROM t',
    'SELECT a FROM t WHERE x * 2 > 1',
    'SELECT a FROM t WHERE b IN (1, *)',
    'SELECT a FROM t, *',
    'SELECT a, b * FROM t',
    'SELECT * AS x FROM t',
    'SELECT (*) FROM t',
    'SELECT x FROM t ORDER BY a, *',
]) {
    for (const marked of bothSides(sql)) {
        test(`not a select-list star (an expression): ${JSON.stringify(marked)}`, () => {
            assert.equal(wildcardAtCaret(cm, at(marked)), null);
        });
    }
}

for (const [marked, why] of [
    ['SELECT a, b/|*2 FROM t', 'a product: the caret follows an operator'],
    ['SELECT a FROM t /|* x */', 'the caret between / and * of a comment opening'],
    ['SELECT a FROM t\n/|* x */', 'the caret between / and * of a comment opening, at a line start'],
    ['/|* x */ SELECT a FROM t', 'a comment opening before the SELECT'],
    ['SELECT a FROM t\n/* note\n|* more\n */', 'a block-comment line that starts with *'],
    ['SELECT a FROM t\n/* note\n *| more\n */', 'after the star of a block-comment line'],
    ['SELECT a FROM t /* x |*/', 'the caret before the star of a comment ending'],
    ['SELECT a FROM t /* x *|/', 'the caret after the star of a comment ending'],
    ['SELECT /* a /* b */ |* */ a FROM t', 'a star in a nested block comment'],
    ['-- SELECT *|', 'a line comment'],
    ['SELECT a -- *|', 'a line comment after a SELECT'],
    ['SELECT a -- |*\nFROM t', 'a line comment, the caret before the star'],
    ["SELECT 'a *|' FROM t", 'a string'],
    ["SELECT ' |* ' FROM t", 'a string, the caret before the star'],
    ["SELECT N'it''s |*' FROM t", 'a string with a doubled quote'],
    ['SELECT "a *|" FROM t', 'a double-quoted name'],
    ['SELECT " |* " FROM t', 'a double-quoted name, the caret before the star'],
    ['SELECT [a *|] FROM t', 'a bracketed name'],
    ['SELECT [a |*] FROM t', 'a bracketed name, the caret before the star'],
    ['UPDATE t SET a = 1 *|', 'no SELECT in the statement'],
    ['SELECT a FROM t; UPDATE t SET a = 1 *|', 'the SELECT is in an earlier statement'],
    ['SELECT a FROM t\nGO\n|* FROM t', 'the SELECT is in an earlier batch'],
    ['SELECT a,\nGO\n|* FROM u', 'a GO line ends the batch the select list was in'],
    ['SELECT a,\n  go   -- next batch\n|* FROM u', 'a GO line with a comment ends the batch'],
    ['SELECT a FROM t\nGO 2\n|* FROM u', 'a GO line with a count ends the batch'],
    ['SELECT |* GO', 'a GO on the star\'s line is a name, not the end of the batch'],
    ['SELECT |*\nGO x', 'GO followed by a word is not a separator line'],
    ['|* FROM t', 'no SELECT before the star'],
    ['SELECT a FROM t|', 'no star at the caret'],
]) {
    test(`not a wildcard (${why}): ${JSON.stringify(marked)}`, () => {
        assert.equal(wildcardAtCaret(cm, at(marked)), null);
    });
}

test('a long document whose syntax tree has not reached the end: a star on a block-comment line is not a wildcard', () => {
    const body = 'SELECT a, b FROM dbo.T WHERE x = 1;\n'.repeat(200);              // 7,200 characters
    const marked = body + 'SELECT a FROM t\n/* a note\n|* note\n */';
    const state = at(marked);
    assert.ok(state.doc.length > 6000);
    assert.ok(cm.language.syntaxTree(state).length < state.doc.length, 'the tree is lazy: it does not reach the star yet');
    assert.equal(wildcardAtCaret(cm, state), null);
});

test('a long document whose syntax tree has not reached the end: a select-list star there is expanded', () => {
    const body = 'SELECT a, b FROM dbo.T WHERE x = 1;\n'.repeat(200);
    const marked = body + 'SELECT |* FROM t';
    const state = at(marked);
    assert.ok(cm.language.syntaxTree(state).length < state.doc.length);
    assert.deepEqual(wildcardAtCaret(cm, state), { caretOffset: afterStar(marked) });
});

test('a range selection is never a wildcard', () => {
    const state = cm.state.EditorState.create({ doc: 'SELECT * FROM t', selection: { anchor: 7, head: 8 }, extensions: [dialectExtension(cm)] });
    assert.equal(wildcardAtCaret(cm, state), null);
});

/** wildcardAtCaret on a PostgreSQL editor: the state carries its language and the call its kind, as in the page. */
const pg = (marked) => wildcardAtCaret(cm, at(marked, 'postgresql'), 'postgresql');

test('PostgreSQL: a select-list star is expanded, a star in a dollar-quoted string, an array subscript or count(t.*) is not', () => {
    assert.deepEqual(pg('SELECT |* FROM t'), { caretOffset: 8 });
    assert.deepEqual(pg('SELECT DISTINCT ON (a) |* FROM t ORDER BY a'), { caretOffset: 24 });
    assert.deepEqual(pg('SELECT |* FROM t LIMIT 5'), { caretOffset: 8 });
    assert.equal(pg('SELECT $$ a *| $$'), null);
    assert.equal(pg('SELECT $fn$ a |* $fn$ FROM t'), null);
    assert.equal(pg("SELECT E'it\\'s |*' FROM t"), null);
    assert.equal(pg('SELECT arr[2 *|] FROM t'), null);
    assert.equal(pg('SELECT count(t.|*) FROM t'), null);
});

// Spec 043 follow-up (review): PgWildcardExpander expands a RETURNING star to the target's columns, and Tab did before the
// select-list gate came in; RETURNING closed the list and only SELECT opened one, so these never reached the engine.
for (const sql of [
    'INSERT INTO t (a) VALUES (1) RETURNING *',
    'UPDATE t SET a = 1 RETURNING *',
    'DELETE FROM t RETURNING *',
    'INSERT INTO t (a) SELECT a FROM u RETURNING *',
    'DELETE FROM t RETURNING *;',
    'UPDATE t SET a = 1\nRETURNING\n    *',
    'WITH d AS (DELETE FROM t RETURNING *) SELECT a FROM d',
]) {
    for (const marked of bothSides(sql)) {
        test(`PostgreSQL: a RETURNING star is expanded: ${JSON.stringify(marked)}`, () => {
            assert.deepEqual(pg(marked), { caretOffset: afterStar(marked) });
        });
    }
}

test('PostgreSQL: a RETURNING list takes the star after a column or a qualifier, as a select list does', () => {
    for (const marked of [
        'DELETE FROM t RETURNING a, |*',
        'UPDATE t AS x SET a = 1 RETURNING x.*|',
        'INSERT INTO t AS x (a) VALUES (1) RETURNING "x".|*',
    ]) assert.deepEqual(pg(marked), { caretOffset: afterStar(marked) }, marked);
});

test('PostgreSQL: a star in a RETURNING expression is not a wildcard', () => {
    for (const sql of [
        'UPDATE t SET a = 1 RETURNING a * 2',
        'DELETE FROM t RETURNING count(*)',
        'DELETE FROM t RETURNING a *b',
        'UPDATE t SET a = 2 * 3',
        "DELETE FROM t RETURNING ' * '",
    ]) {
        for (const marked of bothSides(sql)) assert.equal(pg(marked), null, marked);
    }
});

test('SQL Server: RETURNING is not a select list (T-SQL has none), so its star is left alone', () => {
    for (const sql of ['UPDATE t SET a = 1 RETURNING *', 'DELETE FROM t RETURNING *', 'SELECT a FROM t RETURNING *']) {
        for (const marked of bothSides(sql)) {
            assert.equal(wildcardAtCaret(cm, at(marked)), null, marked);
            assert.equal(wildcardAtCaret(cm, at(marked), 'sqlserver'), null, marked);
        }
    }
    // The kind decides, not the language alone: a SQL Server editor never takes a RETURNING star.
    assert.equal(wildcardAtCaret(cm, at('DELETE FROM t RETURNING |*', 'postgresql'), 'sqlserver'), null);
});

test('PostgreSQL without a syntax tree: the text alone takes a RETURNING star', () => {
    for (const marked of ['INSERT INTO t (a) VALUES (1) RETURNING |*', 'UPDATE t SET a = 1 RETURNING *|']) {
        assert.deepEqual(wildcardAtCaret(cm, plain(marked), 'postgresql'), { caretOffset: afterStar(marked) }, marked);
    }
    assert.equal(wildcardAtCaret(cm, plain('UPDATE t SET a = 1 RETURNING *|')), null);
    assert.equal(wildcardAtCaret(cm, plain('DELETE FROM t RETURNING count(|*)'), 'postgresql'), null);
});

test('selectListStar: RETURNING opens a list on PostgreSQL only', () => {
    const star = (sql, postgres) => selectListStar(sql.replace('|', ''), sql.indexOf('|'), false, postgres);
    assert.equal(star('DELETE FROM t RETURNING |*', true), true);
    assert.equal(star('DELETE FROM t RETURNING |*', false), false);
    assert.equal(star('INSERT INTO t (a) SELECT a FROM u RETURNING |*', true), true);
    assert.equal(star('INSERT INTO t (a) SELECT a FROM u RETURNING |*', false), false);
    assert.equal(star('SELECT a FROM u RETURNING a, |*', true), true);
    // A SELECT list still closes at a clause word on PostgreSQL; RETURNING does not reopen it inside an expression.
    assert.equal(star('SELECT a FROM t WHERE x = |*', true), false);
    assert.equal(star('DELETE FROM t RETURNING (|*)', true), false);
    assert.equal(star('SELECT |* FROM t', true), true);
});

test('without a syntax tree the text alone decides, comments and strings included', () => {
    assert.deepEqual(wildcardAtCaret(cm, plain('SELECT |* FROM t')), { caretOffset: 8 });
    assert.deepEqual(wildcardAtCaret(cm, plain('SELECT a, t.*| FROM t')), { caretOffset: 13 });
    assert.equal(wildcardAtCaret(cm, plain('SELECT a |*b')), null);
    assert.equal(wildcardAtCaret(cm, plain('SELECT COUNT(|*) FROM t')), null);
    assert.equal(wildcardAtCaret(cm, plain('UPDATE t SET a = 1 *|')), null);
    assert.equal(wildcardAtCaret(cm, plain('SELECT a FROM t /* x |*/')), null);
    assert.equal(wildcardAtCaret(cm, plain('SELECT a FROM t\n/* note\n|* more\n */')), null);
    assert.equal(wildcardAtCaret(cm, plain("SELECT ' |* ' FROM t")), null);
    assert.equal(wildcardAtCaret(cm, plain('SELECT a; -- |*')), null);
});

test('without a syntax tree a star more than a million characters in is left alone (Tab indents)', () => {
    const body = 'SELECT a FROM t;\n'.repeat(70_000);   // 1,190,000 characters
    assert.equal(wildcardAtCaret(cm, plain(body + 'SELECT |* FROM t')), null);
});

test('selectListStar reads the text the way both servers do', () => {
    const star = (sql) => selectListStar(sql.replace('|', ''), sql.indexOf('|'));
    assert.equal(star('SELECT |* FROM t'), true);
    assert.equal(star('SELECT /* x */ |* FROM t'), true);
    assert.equal(star('SELECT /* a /* b */ c */ |* FROM t'), true);      // nested comment closed before the star
    assert.equal(star("SELECT 'a;b', |* FROM t"), true);                 // the ';' is in a string, not the statement's end
    assert.equal(star("SELECT N'x', [a]]b], \"c\"\"d\", |* FROM t"), true);
    assert.equal(star('SELECT 1e-3, |* FROM t'), true);
    assert.equal(star("SELECT 'a |* b' FROM t"), false);
    assert.equal(star('SELECT 1.|*2'), false);
    assert.equal(star('SELECT 2 |* 3'), false);
    assert.equal(selectListStar('SELECT x FROM t', 7), false);           // not a star
    assert.equal(selectListStar('SELECT *', 99), false);                 // out of range
    assert.equal(selectListStar(null, 0), false);
});

test('selectListStar: only a line that holds GO alone (a count and a comment may follow) ends the batch', () => {
    const star = (sql) => selectListStar(sql.replace('|', ''), sql.indexOf('|'));
    // A name, not a separator: the select list stays open.
    assert.equal(star('SELECT go, |* FROM t'), true);
    assert.equal(star('SELECT GO , |* FROM t'), true);
    assert.equal(star('SELECT a, go, |* FROM t'), true);
    assert.equal(star('SELECT a,\ngo, |* FROM t'), true);               // first on its line, but a ',' follows on it
    assert.equal(star('SELECT a,\n/* c */ go AS g, |* FROM t'), true);
    // A separator: the batch, and the select list with it, ends.
    assert.equal(star('SELECT a,\nGO\n|* FROM t'), false);
    assert.equal(star('SELECT a,\n\tgo\t\n|* FROM t'), false);
    assert.equal(star('SELECT a,\r\nGO\r\n|* FROM t'), false);
    assert.equal(star('SELECT a,\nGO 5\n|* FROM t'), false);
    assert.equal(star('SELECT a,\nGO -- next\n|* FROM t'), false);
    assert.equal(star('SELECT a,\nGO /* next */\n|* FROM t'), false);
    assert.equal(star('SELECT a,\nGO 5 -- five times\n|* FROM t'), false);
    // After the star: a GO line, but not a GO on the star's own line.
    assert.equal(star('SELECT |*\nGO'), true);
    assert.equal(star('SELECT |* -- all\ngo 2'), true);
    assert.equal(star('SELECT |* GO'), false);
    assert.equal(star('SELECT |*\nGO 5 6'), false);                      // two counts: not a separator line
});

test('selectListStar: a text cut short does not take a star with nothing after it in the text', () => {
    assert.equal(selectListStar('SELECT *', 7), true);
    assert.equal(selectListStar('SELECT *', 7, true), false);
    assert.equal(selectListStar('SELECT * FROM', 7, true), true);
});

// ── Spec 043 follow-up (review): a declined expansion still lets Tab indent when it comes back at once ──────────────────────

test('a quick decline with nothing moved may do what Tab does', () => {
    const state = at('SELECT |* FROM t');
    const atKey = { time: 1000, tab: 'q1', doc: state.doc, selection: state.selection };
    assert.equal(declinedTabMayIndent(atKey, { ...atKey, time: 1000 + 20 }), true);
    assert.equal(declinedTabMayIndent(atKey, { ...atKey, time: 1000 + WILDCARD_TAB_FALLBACK_MS }), true);
    // The caret set again to where it was: a new selection object, an equal selection, the same document — unmoved.
    const touched = state.update({ selection: { anchor: 7 } }).state;
    assert.equal(declinedTabMayIndent(atKey, { time: 1100, tab: 'q1', doc: touched.doc, selection: touched.selection }), true);
});

test('a late decline, another tab, an edit or a moved caret leaves Tab alone', () => {
    const state = at('SELECT |* FROM t');
    const atKey = { time: 1000, tab: 'q1', doc: state.doc, selection: state.selection };
    assert.equal(declinedTabMayIndent(atKey, { ...atKey, time: 1000 + WILDCARD_TAB_FALLBACK_MS + 1 }), false);
    assert.equal(declinedTabMayIndent(atKey, { ...atKey, time: 1100, tab: 'q2' }), false);

    const edited = state.update({ changes: { from: 0, insert: ' ' } }).state;
    assert.equal(declinedTabMayIndent(atKey, { time: 1100, tab: 'q1', doc: edited.doc, selection: state.selection }), false);
    const sameText = state.update({ changes: { from: 0, to: 1, insert: 'S' } }).state;   // the same text, a new document
    assert.equal(declinedTabMayIndent(atKey, { time: 1100, tab: 'q1', doc: sameText.doc, selection: state.selection }), false);

    const moved = state.update({ selection: { anchor: 8 } }).state;
    assert.equal(declinedTabMayIndent(atKey, { time: 1100, tab: 'q1', doc: moved.doc, selection: moved.selection }), false);
    const ranged = state.update({ selection: { anchor: 7, head: 8 } }).state;
    assert.equal(declinedTabMayIndent(atKey, { time: 1100, tab: 'q1', doc: ranged.doc, selection: ranged.selection }), false);

    assert.equal(declinedTabMayIndent(null, atKey), false);
    assert.equal(declinedTabMayIndent(atKey, { ...atKey, time: Number.NaN }), false);
});

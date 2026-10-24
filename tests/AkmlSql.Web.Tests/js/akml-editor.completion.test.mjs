// Unit tests for how the editor decides what to replace and when to open the completion popup, per database kind
// (spec 042 T108, FR-061, FR-065). Run with: node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// SQL Server words may start with '@' or '#'; PostgreSQL has neither, writes quoted names with double quotes, and
// casts with '::'. The popup must not open on '@', '#' or '[' in PostgreSQL mode.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
    isPostgresKind,
    wordBefore,
    completionValidFor,
    postKeywordTrigger,
    typedTriggerCharacters,
    documentationLink,
    quotedReplaceSpan,
} from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

test('only the postgresql kind is PostgreSQL, whatever its case', () => {
    assert.equal(isPostgresKind('postgresql'), true);
    assert.equal(isPostgresKind(' PostgreSQL '), true);
    assert.equal(isPostgresKind('sqlserver'), false);
    assert.equal(isPostgresKind(undefined), false);
    assert.equal(isPostgresKind(null), false);
});

test('SQL Server words keep their @ and # sigils', () => {
    assert.deepEqual(wordBefore('sqlserver', 'SELECT @Cust'), { from: 7, text: '@Cust' });
    assert.deepEqual(wordBefore('sqlserver', 'INSERT #tmp'), { from: 7, text: '#tmp' });
    assert.deepEqual(wordBefore(undefined, 'SELECT @@ROW'), { from: 7, text: '@@ROW' }, 'no connection is SQL Server');
    assert.equal(wordBefore('sqlserver', 'SELECT '), null);
});

test('a PostgreSQL word is letters, digits, underscore and dollar', () => {
    assert.deepEqual(wordBefore('postgresql', 'SELECT * FROM cust_id$2'), { from: 14, text: 'cust_id$2' });
    assert.equal(wordBefore('postgresql', 'SELECT * FROM '), null);
    assert.equal(wordBefore('postgresql', ''), null);
    assert.equal(wordBefore('postgresql', undefined), null);
});

test('@ and # do not start a PostgreSQL word', () => {
    assert.equal(wordBefore('postgresql', 'SELECT @'), null);
    assert.equal(wordBefore('postgresql', 'SELECT #'), null);
    assert.equal(wordBefore('postgresql', 'SELECT ['), null);
    assert.deepEqual(wordBefore('postgresql', 'SELECT @Cust'), { from: 8, text: 'Cust' }, 'only the letters count, the @ is left alone');
});

test('a quoted name being typed is replaced from its opening quote, spaces included', () => {
    assert.deepEqual(wordBefore('postgresql', 'SELECT * FROM "Audit L'), { from: 14, text: '"Audit L' });
    assert.deepEqual(wordBefore('postgresql', 'SELECT * FROM "Aud'), { from: 14, text: '"Aud' });
    assert.deepEqual(wordBefore('postgresql', 'SELECT * FROM "'), { from: 14, text: '"' });
});

test('a closed quoted name is not a word being typed, and the next quoted name is', () => {
    assert.equal(wordBefore('postgresql', 'SELECT * FROM "Audit Log" '), null);
    assert.deepEqual(wordBefore('postgresql', 'SELECT * FROM "Audit Log" JOIN "Or'), { from: 31, text: '"Or' });
    assert.deepEqual(wordBefore('postgresql', 'SELECT "a""b" , cust'), { from: 16, text: 'cust' }, 'a doubled quote inside a name keeps the count even');
});

test('the popup stays open while the quoted name is open and closes once it is closed', () => {
    const valid = completionValidFor('postgresql');
    assert.equal(valid.test('"Audit L'), true);
    assert.equal(valid.test('"Audit Log"'), false);
    assert.equal(valid.test('abc_1$'), true);
    assert.equal(valid.test('a b'), false);
    assert.equal(completionValidFor('sqlserver').test('abc'), true);
    assert.equal(completionValidFor('sqlserver').test('a b'), false);
});

test('PostgreSQL opens the popup after its clause words', () => {
    const trigger = postKeywordTrigger('postgresql');
    for (const line of [
        'SELECT a FROM ', 'SELECT a FROM t WHERE ', 'SELECT a FROM t JOIN u ON ', 'INSERT INTO t VALUES (1) ON CONFLICT ',
        'INSERT INTO t VALUES (1) RETURNING ', 'DELETE FROM t USING ', 'SELECT * FROM t LIMIT ', 'SELECT * FROM t ORDER BY ',
        'SELECT a FROM t GROUP BY ', 'UPDATE t SET ', 'SELECT * FROM t UNION ALL ', 'CREATE TABLE t (id int REFERENCES ',
        'DO UPDATE SET x = 1 WHERE ', 'CALL ', 'TRUNCATE ', 'SELECT a AS ',
    ]) assert.equal(trigger.test(line), true, line);
});

test('PostgreSQL opens the popup after a cast', () => {
    const trigger = postKeywordTrigger('postgresql');
    assert.equal(trigger.test('SELECT created::'), true);
    assert.equal(trigger.test('SELECT created:: '), true);
    assert.equal(trigger.test('SELECT created:'), false, 'one colon is not a cast');
    assert.equal(postKeywordTrigger('sqlserver').test('SELECT created::'), false);
});

test('nothing else opens it', () => {
    const trigger = postKeywordTrigger('postgresql');
    assert.equal(trigger.test('SELECT 1 '), false);
    assert.equal(trigger.test('SELECT a FROM t x '), false);
    assert.equal(trigger.test('-- '), false);
});

test('SQL Server keeps its own words, EXEC included', () => {
    const trigger = postKeywordTrigger('sqlserver');
    assert.equal(trigger.test('EXEC '), true);
    assert.equal(trigger.test('SELECT * FROM '), true);
    assert.equal(trigger.test('INSERT INTO t VALUES (1) RETURNING '), false);
});

test('typing a colon can open the popup in PostgreSQL only', () => {
    assert.equal(typedTriggerCharacters('postgresql').test(':'), true);
    assert.equal(typedTriggerCharacters('sqlserver').test(':'), false);
    for (const kind of ['postgresql', 'sqlserver']) {
        assert.equal(typedTriggerCharacters(kind).test(' '), true);
        assert.equal(typedTriggerCharacters(kind).test('.'), true);
    }
});

test('typing @, # or [ never opens the PostgreSQL popup by itself', () => {
    const chars = typedTriggerCharacters('postgresql');
    for (const c of ['@', '#', '[']) assert.equal(chars.test(c), false, c);
});

test('only a page of the official PostgreSQL manual is linked from a hover', () => {
    const ok = { label: 'Documentation', value: 'https://www.postgresql.org/docs/18/functions-aggregate.html' };
    assert.equal(documentationLink(ok), ok.value);
    assert.equal(documentationLink({ label: 'Documentation', value: 'https://www.postgresql.org/docs/18/sql-createtable.html' }) !== null, true);
    // A nested section is an anchor on its page (the hover on :: or a $$ string).
    const anchored = { label: 'Documentation', value: 'https://www.postgresql.org/docs/18/sql-expressions.html#SQL-SYNTAX-TYPE-CASTS' };
    assert.equal(documentationLink(anchored), anchored.value);
    for (const bad of [
        { label: 'Documentation', value: 'http://www.postgresql.org/docs/18/x.html' },
        { label: 'Documentation', value: 'https://evil.example/docs/18/x.html' },
        { label: 'Documentation', value: 'https://www.postgresql.org.evil.example/docs/18/x.html' },
        { label: 'Documentation', value: 'javascript:alert(1)' },
        { label: 'Documentation', value: 'https://www.postgresql.org/docs/18/../x.html' },
        { label: 'Documentation', value: 'https://www.postgresql.org/docs/18/x.html#a"onmouseover' },
        { label: 'Documentation', value: 'https://www.postgresql.org/docs/18/x.html#' },
        { label: 'Other', value: 'https://www.postgresql.org/docs/18/x.html' },
        { label: 'Documentation', value: undefined },
        null,
        undefined,
    ]) assert.equal(documentationLink(bad), null, JSON.stringify(bad));
});

test('accepting a quoted name over an opened quote takes the quote the editor closed for the user', () => {
    // `SELECT * FROM "Aud|"` : the caret is before the closing quote the editor added
    const doc = 'SELECT * FROM "Aud"';
    assert.deepEqual(quotedReplaceSpan(doc, 14, 18, '"Audit Log"'), { from: 14, to: 19 });
});

test('no closing quote after the caret leaves the span alone', () => {
    assert.deepEqual(quotedReplaceSpan('SELECT * FROM "Aud', 14, 18, '"Audit Log"'), { from: 14, to: 18 });
});

test('a plain insert, or a replaced word that did not open with a quote, is never widened', () => {
    assert.deepEqual(quotedReplaceSpan('SELECT * FROM Aud"', 14, 17, 'customers'), { from: 14, to: 17 });
    assert.deepEqual(quotedReplaceSpan('SELECT * FROM aud"', 14, 17, '"Audit Log"'), { from: 14, to: 17 }, 'the typed word has no opening quote');
    assert.deepEqual(quotedReplaceSpan(undefined, 0, 0, '"x"'), { from: 0, to: 0 });
    assert.deepEqual(quotedReplaceSpan('"a"', 0, 2, undefined), { from: 0, to: 2 });
});

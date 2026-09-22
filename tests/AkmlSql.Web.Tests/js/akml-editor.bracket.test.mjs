// Unit tests for bracketedRange (akml-editor.js). Run with:  node --test tests/AkmlSql.Web.Tests/js/akml-editor.bracket.test.mjs
//
// Kept OUT of wwwroot on purpose: the installer copies wwwroot recursively into the published
// site, so a test file there would ship to every install.
//
// Completion can now insert a bracketed name ("[Order Details]"). CodeMirror only replaces the word
// it matched, and its word pattern stops at "[" and at spaces -- so without this, a user who types
// the opening bracket themselves gets it twice: `[Ord` + pick "[Order Details]" = "[[Order Details]".
// These pin the range the insert is widened to.
//
// akml-editor.js loads CodeMirror with a dynamic import, so importing it here pulls in nothing else.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bracketedRange } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

/** Minimal stand-in for a CodeMirror Text: the two members bracketedRange uses. */
function doc(text) {
    return { length: text.length, sliceString: (a, b) => text.slice(a, b) };
}

/**
 * Applies the insert the way the editor does and returns the resulting text. `|` in `before`
 * marks the caret; the word CodeMirror matched is the run of [@#\w] immediately before it.
 */
function pick(before, insert) {
    const caret = before.indexOf('|');
    const text = before.replace('|', '');
    let from = caret;
    while (from > 0 && /[@#\w]/.test(text[from - 1])) from--;

    const range = bracketedRange(doc(text), insert, from, caret);
    return text.slice(0, range.from) + insert + text.slice(range.to);
}

test('a plain pick inserts the bracketed name', () => {
    assert.equal(pick('SELECT * FROM Ord|', '[Order Details]'), 'SELECT * FROM [Order Details]');
});

test('a typed opening bracket is reused, not doubled', () => {
    assert.equal(pick('SELECT * FROM [Ord|', '[Order Details]'), 'SELECT * FROM [Order Details]');
});

test('an open bracket with a space already typed inside it is still found', () => {
    // CodeMirror's word match is only "D" here; the scan back has to cross the space to the "[".
    assert.equal(pick('SELECT * FROM [Order D|', '[Order Details]'), 'SELECT * FROM [Order Details]');
});

test('an auto-closed bracket after the caret is consumed', () => {
    assert.equal(pick('SELECT * FROM [Ord|]', '[Order Details]'), 'SELECT * FROM [Order Details]');
});

test('a closed bracket earlier on the line is left alone', () => {
    assert.equal(
        pick('SELECT [a] FROM Ord|', '[Order Details]'),
        'SELECT [a] FROM [Order Details]');
});

test('the scan stops at a line break', () => {
    assert.equal(
        pick('-- see [notes\nFROM Ord|', '[Order Details]'),
        '-- see [notes\nFROM [Order Details]');
});

test('the scan stops at a string quote', () => {
    assert.equal(
        pick("WHERE x = '[' AND y IN (Ord|", '[Order Details]'),
        "WHERE x = '[' AND y IN ([Order Details]");
});

test('a schema-qualified insert that does not start with a bracket is not widened backwards', () => {
    assert.equal(pick('SELECT * FROM Ord|', 'dbo.[Order Details]'), 'SELECT * FROM dbo.[Order Details]');
});

test('a qualified column after a typed bracketed table works', () => {
    assert.equal(
        pick('WHERE [Order Details].Qu|', 'Quantity'),
        'WHERE [Order Details].Quantity');
});

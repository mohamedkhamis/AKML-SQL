// Unit tests for toLintDiagnostics in akml-editor.js (spec 041 T031, research R39). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// Analyser findings (FR-087) and execution errors (FR-023) are shown in the editor through
// CodeMirror's lint state: an underline, a gutter marker, the message on hover. toLintDiagnostics is
// the pure translation from line-based findings to CodeMirror's offset-based diagnostics, clamped to
// the document so a finding for an edited-away line can never throw.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toLintDiagnostics, selectionStartLine } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

/** Minimal stand-in for a CodeMirror Text: `lines`, `length` and `line(n)` → { from, to }. */
function doc(text) {
    const starts = [0];
    for (let i = 0; i < text.length; i++) if (text[i] === '\n') starts.push(i + 1);
    return {
        length: text.length,
        lines: starts.length,
        line(n) {
            const from = starts[n - 1];
            const to = n < starts.length ? starts[n] - 1 : text.length;
            return { from, to };
        },
        lineAt(pos) {
            let n = 1;
            while (n < starts.length && starts[n] <= pos) n++;
            return { number: n };
        },
        sliceString(from, to) { return text.slice(from, to); },
    };
}

const text = 'SELECT a\nFROM t\nWHERE x = 1';   // lines start at 0, 9, 16

test('severity 0-3 maps to hint, info, warning and error', () => {
    const d = toLintDiagnostics(doc(text), [
        { line: 1, severity: 0, message: 'h' },
        { line: 1, severity: 1, message: 'i' },
        { line: 1, severity: 2, message: 'w' },
        { line: 1, severity: 3, message: 'e' },
    ]);
    assert.deepEqual(d.map(x => x.severity), ['hint', 'info', 'warning', 'error']);
});

test('an unknown severity reads as info', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 1, severity: 9, message: 'x' }]);
    assert.equal(d[0].severity, 'info');
});

test('a finding with no columns covers its whole line', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 2, severity: 2, message: 'FROM' }]);
    assert.equal(d[0].from, 9);
    assert.equal(d[0].to, 15);
    assert.equal(d[0].message, 'FROM');
});

test('1-based columns become offsets within the line', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 3, startColumn: 7, endColumn: 8, severity: 3, message: 'x' }]);
    assert.equal(d[0].from, 22);   // 16 + 6
    assert.equal(d[0].to, 23);     // 16 + 7
});

test('an end before the start widens to the whole line', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 3, startColumn: 9, endColumn: 2, severity: 3, message: 'x' }]);
    assert.equal(d[0].from, 16);
    assert.equal(d[0].to, 27);
});

test('columns past the end of the line are clamped to it', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 1, startColumn: 3, endColumn: 500, severity: 2, message: 'x' }]);
    assert.equal(d[0].from, 2);
    assert.equal(d[0].to, 8);
});

test('a line beyond the document, or below 1, is dropped', () => {
    const d = toLintDiagnostics(doc(text), [
        { line: 4, severity: 2, message: 'gone' },
        { line: 0, severity: 2, message: 'none' },
        { line: 2, severity: 2, message: 'kept' },
    ]);
    assert.deepEqual(d.map(x => x.message), ['kept']);
});

test('an empty document keeps a finding on line 1', () => {
    const d = toLintDiagnostics(doc(''), [{ line: 1, severity: 3, message: 'x' }]);
    assert.equal(d.length, 1);
    assert.equal(d[0].from, 0);
    assert.equal(d[0].to, 0);
});

test('each diagnostic names its source', () => {
    const d = toLintDiagnostics(doc(text), [{ line: 1, severity: 2, message: 'x', source: 'Execution' }, { line: 1, severity: 2, message: 'y' }]);
    assert.equal(d[0].source, 'Execution');
    assert.equal(d[1].source, 'AKML SQL');
});

test('null or missing input yields no diagnostics', () => {
    assert.deepEqual(toLintDiagnostics(doc(text), null), []);
    assert.deepEqual(toLintDiagnostics(doc(text), undefined), []);
});

// Spec 041 (T102, R24): where the executed text starts, so an error's line maps back to the document.
test('a selection starts on the line of its first character', () => {
    assert.equal(selectionStartLine(doc(text), { from: 9, to: 15 }), 2);
    assert.equal(selectionStartLine(doc(text), { from: 18, to: 27 }), 3);
    assert.equal(selectionStartLine(doc(text), { from: 0, to: 4 }), 1);
});

test('no selection, or only whitespace, runs the whole document: line 1', () => {
    assert.equal(selectionStartLine(doc(text), { from: 12, to: 12 }), 1);
    assert.equal(selectionStartLine(doc('SELECT 1\n   \nSELECT 2'), { from: 9, to: 13 }), 1);
    assert.equal(selectionStartLine(doc(text), null), 1);
});

// Unit tests for downloadText in akml-download.js (spec 041 T015, research R12). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// downloadText saves text the page built (a result set as CSV, the settings export, a .sql document)
// without the 4/3 Base64 inflation downloadBase64 costs on a large payload. A UTF-8 byte-order mark
// is optional: Excel needs it to read a CSV as UTF-8, SSMS opens a .sql with it correctly.
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

let created;
let revoked;
let clicked;
let anchors;

beforeEach(() => {
    created = [];
    revoked = [];
    clicked = [];
    anchors = [];
    globalThis.Blob = class {
        constructor(parts, options) { this.parts = parts; this.type = options && options.type; }
    };
    globalThis.URL = {
        createObjectURL(blob) { created.push(blob); return `blob:${created.length}`; },
        revokeObjectURL(url) { revoked.push(url); },
    };
    globalThis.setTimeout = (fn) => { fn(); return 0; };
    globalThis.document = {
        createElement(tag) {
            assert.equal(tag, 'a');
            const a = { href: '', download: '', click() { clicked.push(this.download); }, remove() { this.removed = true; } };
            anchors.push(a);
            return a;
        },
        body: { appendChild() { } },
    };
});

const { downloadText } = await import('../../../src/AkmlSql.Web/wwwroot/js/akml-download.js');

test('downloads the text under the given name and type', () => {
    downloadText('orders-audit.csv', 'text/csv', 'a,b\r\n1,2\r\n', false);

    assert.deepEqual(clicked, ['orders-audit.csv']);
    assert.equal(created.length, 1);
    assert.equal(created[0].type, 'text/csv');
    assert.deepEqual(created[0].parts, ['a,b\r\n1,2\r\n']);
    assert.equal(anchors[0].href, 'blob:1');
    assert.equal(anchors[0].removed, true);
    assert.deepEqual(revoked, ['blob:1']);
});

test('withBom prefixes a UTF-8 byte-order mark', () => {
    downloadText('q.sql', 'application/sql', 'SELECT 1', true);

    assert.deepEqual(created[0].parts, ['\uFEFF', 'SELECT 1']);
});

test('null text downloads an empty file and a missing type falls back to text/plain', () => {
    downloadText('empty.txt', null, null, false);

    assert.deepEqual(created[0].parts, ['']);
    assert.equal(created[0].type, 'text/plain;charset=utf-8');
});

// Unit tests for akml-indexeddb.js's open and upgrade handling (spec 041 T116, research R36, N26). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// A stubbed indexedDB records how the module opens the database. Version 4 adds the workspaceLayout
// store; when another tab upgrades the database, this tab closes its connection, forgets it, and reopens
// WITHOUT a version — asking for its own (lower) version would fail with a VersionError.
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { openDb, DB_VERSION, STORES, _resetForTests } from '../../../src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js';

let opens;
let created;

function fakeDb() {
    return {
        closed: false,
        onversionchange: null,
        objectStoreNames: { contains: (name) => created.includes(name) },
        createObjectStore: (name) => { created.push(name); },
        close() { this.closed = true; },
    };
}

beforeEach(() => {
    _resetForTests();
    opens = [];
    created = [];
    globalThis.indexedDB = {
        open(...args) {
            const req = { result: fakeDb(), onupgradeneeded: null, onsuccess: null, onerror: null, onblocked: null };
            opens.push({ args, req });
            queueMicrotask(() => {
                if (args.length > 1 && req.onupgradeneeded) req.onupgradeneeded({ target: req });
                req.onsuccess();
            });
            return req;
        },
    };
});

test('version 4 creates the workspaceLayout store', async () => {
    assert.equal(DB_VERSION, 4);
    assert.ok(STORES.includes('workspaceLayout'));

    await openDb();

    assert.deepEqual(opens[0].args, ['AkmlSqlWeb', 4]);
    assert.ok(created.includes('workspaceLayout'));
    assert.ok(created.includes('savedSqlConnections'), 'existing stores are kept');
});

test('the connection is opened once and shared', async () => {
    const a = await openDb();
    const b = await openDb();

    assert.equal(a, b);
    assert.equal(opens.length, 1);
});

test('after another tab upgrades, the connection is closed, forgotten and reopened without a version', async () => {
    const first = await openDb();

    first.onversionchange();
    assert.equal(first.closed, true);

    const second = await openDb();
    assert.notEqual(second, first, 'the closed connection is not reused');
    assert.equal(opens.length, 2);
    assert.deepEqual(opens[1].args, ['AkmlSqlWeb'], 'no version: the database is newer than this tab');
});

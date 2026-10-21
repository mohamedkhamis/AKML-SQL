// Review findings WEB-04 and WEB-15 for akml-crypto.js. Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// WEB-04: concurrent first wraps used to generate one key each and store whichever finished last, so every other record
// became undecryptable. Now one key is created and every caller — and every other "tab" — uses it.
// WEB-15: the module's own database connections are closed when their transaction completes and close themselves on a
// version change, so they never block another tab's upgrade.
//
// Node has Web Crypto; IndexedDB is a small in-memory fake that runs readwrite transactions one at a time, as browsers do.
// A real browser check lives in the Web E2E suite (SecretStorageTests).
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { wrap, unwrap, _resetForTests } from '../../../src/AkmlSql.Web/wwwroot/js/akml-crypto.js';

let records;          // the keyMaterial store's contents
let connections;      // every connection opened
let writeQueue;       // readwrite transactions run in order

function fakeIndexedDb() {
    return {
        open() {
            const db = {
                closed: false,
                onversionchange: null,
                objectStoreNames: { contains: name => name === 'keyMaterial' },
                close() { this.closed = true; },
                transaction(_store, mode) { return transaction(mode); },
            };
            connections.push(db);
            const req = { result: db, onsuccess: null, onerror: null };
            queueMicrotask(() => req.onsuccess());
            return req;
        },
    };
}

function transaction(mode) {
    const tx = { oncomplete: null, onerror: null, onabort: null, error: null };
    const pending = [];
    let started = false;
    const run = async () => {
        // Run each request (and those its callbacks add) in turn, then complete.
        while (pending.length) {
            const op = pending.shift();
            await new Promise(r => setTimeout(r, 1));   // let other work interleave
            op();
        }
        tx.oncomplete?.();
    };
    const start = () => {
        if (started) return;
        started = true;
        if (mode === 'readwrite') {
            const previous = writeQueue;
            writeQueue = previous.then(run);
        } else {
            queueMicrotask(run);
        }
    };
    tx.objectStore = () => ({
        get(key) {
            const req = { result: undefined, onsuccess: null };
            pending.push(() => { req.result = records.get(key); req.onsuccess?.(); });
            start();
            return req;
        },
        add(value, key) {
            const req = { onsuccess: null };
            pending.push(() => {
                assert.ok(!records.has(key), 'add() over an existing key would abort the transaction');
                records.set(key, value);
                req.onsuccess?.();
            });
            return req;
        },
    });
    return tx;
}

beforeEach(() => {
    records = new Map();
    connections = [];
    writeQueue = Promise.resolve();
    globalThis.indexedDB = fakeIndexedDb();
    _resetForTests();
});

test('twelve concurrent first wraps all decrypt after a reload', async () => {
    const values = Array.from({ length: 12 }, (_, i) => `synthetic-secret-${i}`);
    const wrapped = await Promise.all(values.map(v => wrap(v, 'aad')));

    _resetForTests();   // a fresh page: only what the store kept is known
    const unwrapped = await Promise.all(wrapped.map(w => unwrap(w.ciphertext, w.iv, w.aad)));

    assert.deepEqual(unwrapped, values);
    assert.equal(records.size, 1, 'one wrapping key');
});

test('two tabs creating the key at once settle on the one the store kept', async () => {
    // Each "tab" is a separate module cache; both find no key and generate one.
    const a = wrap('from tab A', 'aad');
    _resetForTests();
    const b = wrap('from tab B', 'aad');
    const [wa, wb] = await Promise.all([a, b]);

    _resetForTests();
    assert.equal(await unwrap(wa.ciphertext, wa.iv, wa.aad), 'from tab A');
    assert.equal(await unwrap(wb.ciphertext, wb.iv, wb.aad), 'from tab B');
});

test('an established key is never replaced', async () => {
    const first = await wrap('before', 'aad');
    const key = records.get('primaryWrap');

    _resetForTests();
    await wrap('after', 'aad');

    assert.equal(records.get('primaryWrap'), key);
    assert.equal(await unwrap(first.ciphertext, first.iv, first.aad), 'before');
});

test('every connection the module opens is closed and yields to a version change', async () => {
    await wrap('x', 'aad');
    _resetForTests();
    await wrap('y', 'aad');

    assert.ok(connections.length > 0);
    for (const db of connections) {
        assert.equal(db.closed, true, 'closed once its transaction completed');
        assert.equal(typeof db.onversionchange, 'function', 'closes itself if another tab upgrades meanwhile');
    }
});

test('without a secure context nothing is stored and the reason is given', async () => {
    const original = Object.getOwnPropertyDescriptor(globalThis, 'isSecureContext');
    Object.defineProperty(globalThis, 'isSecureContext', { value: false, configurable: true });
    try {
        await assert.rejects(wrap('secret', 'aad'), /HTTPS/);
        assert.equal(records.size, 0);
    } finally {
        if (original) Object.defineProperty(globalThis, 'isSecureContext', original);
        else delete globalThis.isSecureContext;
    }
});

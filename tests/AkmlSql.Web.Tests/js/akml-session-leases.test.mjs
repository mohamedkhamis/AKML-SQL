// Spec 043 (review of US1) — akml-session-leases.js: one window's lease on an engine session id.
//   * with the Web Locks API: a free id is claimed and stays held until released; an id another window holds is refused;
//   * without it (a page that is not a secure context), the windows ask each other over a BroadcastChannel: a live window that
//     holds an id says so, ids asked about together are asked in one question, an id a window has just made needs no question;
//   * with neither, the claim says it cannot tell;
//   * the session a window uses instead of a tab's stored one is kept in sessionStorage, for that window's reloads.
// Every test sets the globals it relies on: Node has a real BroadcastChannel, which would keep the test process alive.
import { test } from 'node:test';
import assert from 'node:assert/strict';

// A minimal navigator.locks: exclusive, ifAvailable, held while the callback's promise is pending.
function fakeLocks() {
    const held = new Set();
    return {
        held,
        request(name, options, callback) {
            if (held.has(name)) return Promise.resolve(callback(null));
            held.add(name);
            const result = callback({ name, mode: options.mode });
            return Promise.resolve(result).finally(() => held.delete(name));
        },
    };
}

// BroadcastChannels of one browser: a message reaches every other channel of the same name, later, never the sender.
function fakeBroadcast() {
    const channels = new Set();
    const posted = [];
    class FakeChannel {
        constructor(name) {
            this.name = name;
            this.listeners = new Set();
            channels.add(this);
        }
        addEventListener(type, fn) { if (type === 'message') this.listeners.add(fn); }
        removeEventListener(type, fn) { if (type === 'message') this.listeners.delete(fn); }
        postMessage(data) {
            posted.push(data);
            for (const other of channels) {
                if (other === this || other.name !== this.name) continue;
                setTimeout(() => { for (const fn of [...other.listeners]) fn({ data }); }, 0);
            }
        }
        close() { channels.delete(this); }
    }
    return { FakeChannel, posted };
}

function fakeSessionStorage() {
    const items = new Map();
    return {
        items,
        getItem: (k) => (items.has(k) ? items.get(k) : null),
        setItem: (k, v) => items.set(k, String(v)),
        removeItem: (k) => items.delete(k),
    };
}

// Node has a read-only global navigator (and its own BroadcastChannel): replace them for a test.
function setGlobal(name, value) {
    Object.defineProperty(globalThis, name, { value, configurable: true, writable: true });
}

let windows = 0;
async function openWindow() {
    // A fresh module per window: its leases are module state.
    return import(`../../../src/AkmlSql.Web/wwwroot/js/akml-session-leases.js?window=${++windows}`);
}

test('with Web Locks a free session id is claimed and held until it is released', async () => {
    const locks = fakeLocks();
    setGlobal('navigator', { locks });
    setGlobal('BroadcastChannel', undefined);
    const leases = await openWindow();

    assert.equal(await leases.claimSession('s1'), 'held');
    assert.equal(await leases.claimSession('s1'), 'held');   // already held by this window
    assert.deepEqual(leases.heldSessions(), ['s1']);
    assert.ok(locks.held.has('akml-engine-session-s1'));

    leases.releaseSession('s1');
    await new Promise(r => setTimeout(r, 0));
    assert.equal(locks.held.has('akml-engine-session-s1'), false);
    assert.deepEqual(leases.heldSessions(), []);
});

test('with Web Locks an id another window holds is refused', async () => {
    const locks = fakeLocks();
    locks.held.add('akml-engine-session-s2');   // the other window
    setGlobal('navigator', { locks });
    setGlobal('BroadcastChannel', undefined);
    const leases = await openWindow();

    assert.equal(await leases.claimSession('s2'), 'refused');
    assert.deepEqual(leases.heldSessions(), []);
});

test('without Web Locks a live window that holds an id says so, and a released id is free again', async () => {
    const { FakeChannel } = fakeBroadcast();
    setGlobal('navigator', {});
    setGlobal('BroadcastChannel', FakeChannel);
    const first = await openWindow();
    const second = await openWindow();

    assert.equal(await first.claimSession('s1'), 'held');     // nobody answered
    assert.equal(await second.claimSession('s1'), 'refused'); // the first window said it holds s1
    assert.equal(await second.claimSession('s2'), 'held');

    first.releaseSession('s1');
    assert.equal(await second.claimSession('s1'), 'held');
});

test('ids asked about together are asked in one question, and an id a window has just made is not asked about', async () => {
    const { FakeChannel, posted } = fakeBroadcast();
    setGlobal('navigator', {});
    setGlobal('BroadcastChannel', FakeChannel);
    const first = await openWindow();
    const second = await openWindow();

    assert.equal(await first.claimSession('fresh-1', true), 'held');
    assert.equal(posted.length, 0);   // made here: nobody else can hold it

    const answers = await Promise.all([second.claimSession('a'), second.claimSession('fresh-1'), second.claimSession('b')]);
    assert.deepEqual(answers, ['held', 'refused', 'held']);
    const questions = posted.filter(m => m.type === 'who-holds');
    assert.equal(questions.length, 1);
    assert.deepEqual([...questions[0].ids].sort(), ['a', 'b', 'fresh-1']);
});

test('two windows asking about the same id at once settle it the same way: exactly one holds it', async () => {
    const { FakeChannel } = fakeBroadcast();
    setGlobal('navigator', {});
    setGlobal('BroadcastChannel', FakeChannel);
    const first = await openWindow();
    const second = await openWindow();

    const answers = await Promise.all([first.claimSession('both'), second.claimSession('both')]);

    assert.deepEqual([...answers].sort(), ['held', 'refused']);
    const winner = answers[0] === 'held' ? first : second;
    const loser = answers[0] === 'held' ? second : first;
    assert.equal(await loser.claimSession('both'), 'refused');   // and it stays the winner's
    assert.ok(winner.heldSessions().includes('both'));
});

test('with neither Web Locks nor a BroadcastChannel the claim says it cannot tell', async () => {
    setGlobal('navigator', {});
    setGlobal('BroadcastChannel', undefined);
    const leases = await openWindow();

    assert.equal(await leases.claimSession('s3'), 'unavailable');
    assert.doesNotThrow(() => leases.releaseSession('s3'));
});

test('the session a window uses instead of a stored one is kept for its reloads, and forgotten with its tab', async () => {
    const storage = fakeSessionStorage();
    setGlobal('sessionStorage', storage);
    setGlobal('navigator', {});
    setGlobal('BroadcastChannel', undefined);
    const leases = await openWindow();

    assert.equal(leases.windowSession('tab-1'), null);
    leases.rememberWindowSession('tab-1', 'own-session');
    const reloaded = await openWindow();   // the same browser tab: the same sessionStorage
    assert.equal(reloaded.windowSession('tab-1'), 'own-session');

    reloaded.forgetWindowSession('tab-1');
    assert.equal(reloaded.windowSession('tab-1'), null);
});

test('without sessionStorage nothing is kept and nothing throws', async () => {
    setGlobal('sessionStorage', undefined);
    const leases = await openWindow();

    assert.doesNotThrow(() => leases.rememberWindowSession('tab-1', 'own-session'));
    assert.equal(leases.windowSession('tab-1'), null);
    assert.doesNotThrow(() => leases.forgetWindowSession('tab-1'));
});

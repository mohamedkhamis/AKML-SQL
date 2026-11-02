import { test } from 'node:test';
import assert from 'node:assert/strict';
import { detect } from '../../../src/AkmlSql.Web/wwwroot/js/akml-page-engine.js';

test('HEAD detection completes without aborting a successful fetch', async () => {
    let signal;
    let consumed = false;
    const originalFetch = globalThis.fetch;
    globalThis.fetch = async (url, options) => {
        assert.equal(url, 'https://sql-box:59417/');
        assert.equal(options.method, 'HEAD');
        assert.equal(options.cache, 'no-cache');
        assert.equal(options.headers['Cache-Control'], 'no-cache');
        signal = options.signal;
        return {
            headers: new Headers({ 'X-AKML-Host': 'ENGINE' }),
            arrayBuffer: async () => { consumed = true; return new ArrayBuffer(0); }
        };
    };
    try {
        assert.equal(await detect('https://sql-box:59417/'), true);
        assert.equal(consumed, true);
        await new Promise(resolve => setTimeout(resolve, 10));
        assert.equal(signal.aborted, false);
    } finally { globalThis.fetch = originalFetch; }
});

test('a static host returns false and transport errors remain failures', async () => {
    const originalFetch = globalThis.fetch;
    try {
        globalThis.fetch = async () => new Response(null);
        assert.equal(await detect('http://localhost/'), false);
        globalThis.fetch = async () => { throw new TypeError('network failed'); };
        await assert.rejects(detect('http://localhost/'), /network failed/);
    } finally { globalThis.fetch = originalFetch; }
});

test('a stalled fetch is aborted after five seconds and the timer is released', async () => {
    const originalFetch = globalThis.fetch;
    const originalSetTimeout = globalThis.setTimeout;
    const originalClearTimeout = globalThis.clearTimeout;
    let expire;
    let cleared = false;
    globalThis.setTimeout = (callback, delay) => { assert.equal(delay, 5000); expire = callback; return 123; };
    globalThis.clearTimeout = id => { assert.equal(id, 123); cleared = true; };
    globalThis.fetch = (_, { signal }) => new Promise((_, reject) => {
        signal.addEventListener('abort', () => reject(new Error('timed out')));
    });
    try {
        const pending = detect('http://localhost/');
        expire();
        await assert.rejects(pending, /timed out/);
        assert.equal(cleared, true);
    } finally {
        globalThis.fetch = originalFetch;
        globalThis.setTimeout = originalSetTimeout;
        globalThis.clearTimeout = originalClearTimeout;
    }
});

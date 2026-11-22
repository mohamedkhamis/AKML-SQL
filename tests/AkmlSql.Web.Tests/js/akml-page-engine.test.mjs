import { test } from 'node:test';
import assert from 'node:assert/strict';
import { detect, readManifest } from '../../../src/AkmlSql.Web/wwwroot/js/akml-page-engine.js';

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

// ── Spec 047 (WEB-001): the engine address an installer publishes next to a static (IIS) page ─────────────────────

async function withFetch(fake, body) {
    const originalFetch = globalThis.fetch;
    globalThis.fetch = fake;
    try { await body(); } finally { globalThis.fetch = originalFetch; }
}

test('readManifest returns the published object and asks with GET and no-cache', async () => {
    await withFetch(async (url, options) => {
        assert.equal(url, 'http://localhost:59416/akml-engine.json');
        assert.equal(options.method, 'GET');
        assert.equal(options.cache, 'no-cache');
        return new Response('{"manifestVersion":1,"bridgePort":59417,"scheme":"ws"}', { status: 200 });
    }, async () => {
        assert.deepEqual(await readManifest('http://localhost:59416/akml-engine.json'),
            { manifestVersion: 1, bridgePort: 59417, scheme: 'ws' });
    });
});

test('readManifest returns null for a missing file, invalid JSON, a non-object and a network failure', async () => {
    const answers = [
        async () => new Response('not found', { status: 404 }),
        async () => new Response('{not json', { status: 200 }),
        async () => new Response('[1,2]', { status: 200 }),
        async () => new Response('42', { status: 200 }),
        async () => new Response('null', { status: 200 }),
        async () => { throw new TypeError('network failed'); },
    ];
    for (const answer of answers) {
        await withFetch(answer, async () => {
            assert.equal(await readManifest('http://localhost/akml-engine.json'), null);
        });
    }
});

test('readManifest gives up after five seconds and releases its timer', async () => {
    const originalSetTimeout = globalThis.setTimeout;
    const originalClearTimeout = globalThis.clearTimeout;
    let expire;
    let cleared = false;
    globalThis.setTimeout = (callback, delay) => { assert.equal(delay, 5000); expire = callback; return 456; };
    globalThis.clearTimeout = id => { assert.equal(id, 456); cleared = true; };
    try {
        await withFetch((_, { signal }) => new Promise((_, reject) => {
            signal.addEventListener('abort', () => reject(new Error('timed out')));
        }), async () => {
            const pending = readManifest('http://localhost/akml-engine.json');
            expire();
            assert.equal(await pending, null);
            assert.equal(cleared, true);
        });
    } finally {
        globalThis.setTimeout = originalSetTimeout;
        globalThis.clearTimeout = originalClearTimeout;
    }
});

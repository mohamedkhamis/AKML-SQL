// Unit tests for connect() in akml-bridge.js. Run with:  node --test tests/AkmlSql.Web.Tests/js/akml-bridge.connect.test.mjs
//
// Kept OUT of wwwroot on purpose: the installer copies wwwroot recursively into the published
// site, so a test file there would ship to every install.
//
// A connection the page's own Content-Security-Policy refuses reaches the WebSocket API as one
// bare `error` event -- indistinguishable from "nothing is listening". The browser only says why
// in a separate securitypolicyviolation event, which may arrive before OR after that error. These
// pin that connect() names the policy either way, and still reports a plain failure as one.
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

// --- browser stand-ins ----------------------------------------------------------------------

const listeners = new Map();
globalThis.document = {
    addEventListener: (type, fn) => listeners.set(type, [...(listeners.get(type) ?? []), fn]),
    removeEventListener: (type, fn) => listeners.set(type, (listeners.get(type) ?? []).filter(f => f !== fn)),
};

function dispatchViolation(blockedURI) {
    for (const fn of listeners.get('securitypolicyviolation') ?? []) {
        fn({ effectiveDirective: 'connect-src', violatedDirective: 'connect-src', blockedURI });
    }
}

let lastSocket;
let throwOnConstruct = null;
class FakeWebSocket {
    static OPEN = 1;
    constructor(url) {
        if (throwOnConstruct) throw throwOnConstruct;
        this.url = url;
        this.readyState = 0;
        lastSocket = this;
    }
    close() {}
}
globalThis.WebSocket = FakeWebSocket;

const { connect } = await import('../../../src/AkmlSql.Web/wwwroot/js/akml-bridge.js');

const url = 'wss://203.0.113.10:59417/akmlsql';
const tick = (ms = 0) => new Promise(r => setTimeout(r, ms));

beforeEach(() => {
    listeners.clear();
    throwOnConstruct = null;
});

// --- tests ----------------------------------------------------------------------------------

test('an open socket resolves with its id', async () => {
    const pending = connect(url);
    lastSocket.onopen();
    assert.match(await pending, /^\d+$/);
});

test('a plain failure is reported as a failed connect', async () => {
    const pending = connect(url);
    lastSocket.onerror();
    lastSocket.onclose({ code: 1006, reason: '' });
    await assert.rejects(pending, /WebSocket connect failed \(wss:\/\/203\.0\.113\.10:59417\/akmlsql\)/);
});

test('a policy violation BEFORE the error names the Content-Security-Policy', async () => {
    const pending = connect(url);
    dispatchViolation('wss://203.0.113.10:59417/akmlsql');
    lastSocket.onerror();
    await assert.rejects(pending, /Content-Security-Policy/);
});

test('a policy violation AFTER the error still names the Content-Security-Policy', async () => {
    const pending = connect(url);
    lastSocket.onerror();
    await tick();                          // the error has been seen; the violation comes next
    dispatchViolation('wss://203.0.113.10:59417');
    await assert.rejects(pending, /Content-Security-Policy/);
});

test('a violation for a different address does not change the message', async () => {
    const pending = connect(url);
    dispatchViolation('https://api.example.com');
    lastSocket.onerror();
    await assert.rejects(pending, err => !/Content-Security-Policy/.test(err.message));
});

test('a synchronous SecurityError is reported as the policy', async () => {
    throwOnConstruct = Object.assign(new Error('refused'), { name: 'SecurityError' });
    await assert.rejects(connect(url), /Content-Security-Policy/);
});

test('the violation listener is removed once the connect settles', async () => {
    const pending = connect(url);
    lastSocket.onopen();
    await pending;
    assert.equal((listeners.get('securitypolicyviolation') ?? []).length, 0);

    const failing = connect(url);
    lastSocket.onerror();
    await assert.rejects(failing);
    assert.equal((listeners.get('securitypolicyviolation') ?? []).length, 0);
});

test('error followed by close settles exactly once', async () => {
    const pending = connect(url);
    lastSocket.onerror();
    lastSocket.onclose({ code: 1006, reason: '' });
    await assert.rejects(pending);
    await tick(80);                        // no second settle, no unhandled rejection
});

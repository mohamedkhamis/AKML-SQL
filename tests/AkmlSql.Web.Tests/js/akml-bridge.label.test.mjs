// Spec 047 (ENG-001): the label a page sends with its handshake, so the engine's list of paired browsers can tell
// them apart ("Edge on Windows · http://localhost:59416") instead of 166 rows of "Web edition".
// Run with:  node --test tests/AkmlSql.Web.Tests/js/akml-bridge.label.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { browserLabel } from '../../../src/AkmlSql.Web/wwwroot/js/akml-bridge.js';

const ORIGIN = 'http://localhost:59416';

const UA = {
    edgeWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0',
    chromeMac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
    firefoxLinux: 'Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0',
    safariIos: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1',
    chromeAndroid: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
    operaWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 OPR/115.0.0.0',
    safariMac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15',
};

test('the browser family and operating system come from the user-agent string', () => {
    assert.equal(browserLabel({ userAgent: UA.edgeWindows }, ORIGIN), `Edge on Windows · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.chromeMac }, ORIGIN), `Chrome on macOS · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.firefoxLinux }, ORIGIN), `Firefox on Linux · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.safariIos }, ORIGIN), `Safari on iOS · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.chromeAndroid }, ORIGIN), `Chrome on Android · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.operaWindows }, ORIGIN), `Opera on Windows · ${ORIGIN}`);
    assert.equal(browserLabel({ userAgent: UA.safariMac }, ORIGIN), `Safari on macOS · ${ORIGIN}`);
});

test('client hints win over the user-agent string when the browser offers them', () => {
    const nav = {
        userAgent: UA.chromeMac,
        userAgentData: {
            brands: [{ brand: 'Not_A Brand', version: '8' }, { brand: 'Chromium', version: '131' }, { brand: 'Microsoft Edge', version: '131' }],
            platform: 'Windows',
        },
    };
    assert.equal(browserLabel(nav, ORIGIN), `Edge on Windows · ${ORIGIN}`);
});

test('an unknown browser still gets a label that names the page', () => {
    assert.equal(browserLabel({ userAgent: 'SomethingNew/1.0' }, ORIGIN), `Browser on unknown OS · ${ORIGIN}`);
    assert.equal(browserLabel(undefined, ORIGIN), `Browser on unknown OS · ${ORIGIN}`);
    assert.equal(browserLabel({}, ''), 'Browser on unknown OS');
});

test('the label never exceeds 80 characters and a cut ends with an ellipsis', () => {
    const long = 'https://a-very-long-machine-name.with.a.very.long.domain.example.com:59416';
    const label = browserLabel({ userAgent: UA.edgeWindows }, long);
    assert.ok(label.length <= 80, `length ${label.length}`);
    assert.ok(label.endsWith('…'));
    assert.ok(label.startsWith('Edge on Windows · https://a-very-long'));
});

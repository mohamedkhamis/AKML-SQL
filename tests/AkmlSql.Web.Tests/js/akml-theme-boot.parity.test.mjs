// Unit tests for the first-paint theme (spec 041 T137, research R54, FR-092). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// akml-theme-boot.js is a CLASSIC script that runs synchronously right after the theme <link>, before the body
// is painted: it reads the mode akml-theme.js mirrored to localStorage (falling back to the OS preference) and
// points the link at the theme. It cannot import akml-theme.js, so it carries its own copy of the theme map —
// these tests keep the two the same.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';
import { THEMES, MIRROR_KEY } from '../../../src/AkmlSql.Web/wwwroot/js/akml-theme.js';

const here = dirname(fileURLToPath(import.meta.url));
const wwwroot = join(here, '..', '..', '..', 'src', 'AkmlSql.Web', 'wwwroot');
const bootSource = readFileSync(join(wwwroot, 'js', 'akml-theme-boot.js'), 'utf8');

/** Run the boot script against a fake page; returns what it did to the link and the root element. */
function boot({ stored = null, dark = false, contrast = false, storageThrows = false } = {}) {
    const link = { href: 'css/themes/light.css', setAttribute(name, value) { this[name] = value; } };
    const attrs = {};
    const storage = {
        getItem(key) {
            if (storageThrows) throw new Error('SecurityError');
            return key === MIRROR_KEY ? stored : null;
        },
    };
    const window = {
        localStorage: storage,
        matchMedia: (q) => ({ matches: (q.includes('prefers-contrast') && contrast) || (q.includes('prefers-color-scheme: dark') && dark) }),
    };
    const document = {
        getElementById: (id) => (id === 'akml-theme-css' ? link : null),
        documentElement: { setAttribute(name, value) { attrs[name] = value; }, dataset: {} },
    };
    vm.runInNewContext(bootSource, { window, document, localStorage: storage });
    return { href: link.href, theme: attrs['data-akml-theme'] ?? document.documentElement.dataset.akmlTheme };
}

test('it is a classic script: no import or export', () => {
    assert.doesNotMatch(bootSource, /^\s*(import|export)\s/m);
});

test('every remembered mode gets the same stylesheet as akml-theme.js gives it', () => {
    for (const [mode, href] of Object.entries(THEMES)) {
        const r = boot({ stored: mode });
        assert.equal(r.href, href, mode);
        assert.equal(r.theme, mode, mode);
    }
});

test('System, nothing remembered, or an unknown mode follows the OS', () => {
    assert.equal(boot({ stored: 'system', dark: true }).href, THEMES.dark);
    assert.equal(boot({ stored: null, dark: true }).href, THEMES.dark);
    assert.equal(boot({ stored: null }).href, THEMES.light);
    assert.equal(boot({ stored: 'sepia', contrast: true }).href, THEMES['high-contrast']);
});

test('blocked storage still gives the OS theme', () => {
    assert.equal(boot({ storageThrows: true, dark: true }).theme, 'dark');
});

test('index.html loads it as a classic script right after the theme link', () => {
    const html = readFileSync(join(wwwroot, 'index.html'), 'utf8');
    const link = html.indexOf('id="akml-theme-css"');
    const script = html.indexOf('<script src="js/akml-theme-boot.js"></script>');
    assert.ok(link > 0, 'the theme link');
    assert.ok(script > link, 'the boot script after the link');
    assert.equal(html.slice(link, script).includes('<link rel="stylesheet" href="css/app.css"'), false, 'nothing loads between them');
    assert.doesNotMatch(html, /type="module" src="js\/akml-theme-boot\.js"/);
});

// Spec 041 (research R54, FR-092) — the theme before the first paint. A CLASSIC script (not a module: modules
// are deferred, so the page would paint in the boot default first and then flash), loaded right after the
// theme <link> in index.html and external so the CSP script-src needs no 'unsafe-inline'. It reads the mode
// akml-theme.js mirrored to localStorage — IndexedDB is asynchronous and cannot be waited for here — and falls
// back to the OS preference. The Blazor ThemeService re-applies the stored preference once it starts.
//
// The map and the key are copies of akml-theme.js's THEMES and MIRROR_KEY (a classic script cannot import);
// tests/AkmlSql.Web.Tests/js/akml-theme-boot.parity.test.mjs keeps them the same.
(function () {
    var THEMES = { light: 'css/themes/light.css', dark: 'css/themes/dark.css', 'high-contrast': 'css/themes/high-contrast.css' };
    var MIRROR_KEY = 'akml.theme';

    function detectOsTheme() {
        if (typeof window.matchMedia !== 'function') return 'light';
        if (window.matchMedia('(prefers-contrast: more)').matches) return 'high-contrast';
        if (window.matchMedia('(prefers-color-scheme: dark)').matches) return 'dark';
        return 'light';
    }

    var mode = null;
    try { mode = window.localStorage.getItem(MIRROR_KEY); } catch (e) { /* storage blocked: follow the OS */ }
    var theme = mode && mode !== 'system' && Object.prototype.hasOwnProperty.call(THEMES, mode) ? mode : detectOsTheme();

    var link = document.getElementById('akml-theme-css');
    if (link) link.setAttribute('href', THEMES[theme]);
    document.documentElement.setAttribute('data-akml-theme', theme);
})();

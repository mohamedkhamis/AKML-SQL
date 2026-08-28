/*
 * Spec 034 T028 (US3): no-flash theme boot (FR-009). Lives in an external file (moved out of
 * App.razor) so the strict Content-Security-Policy (script-src 'self', no inline scripts)
 * holds. Loaded as a classic, non-deferred <script> in <head>: it runs before first paint,
 * so there is no flash of the wrong theme.
 * Precedence — shared with js/theme-toggle.js, which writes the same 'akml-theme' key and
 * swaps the same link/attribute: stored localStorage choice > prefers-color-scheme > dark
 * default.
 */
(function () {
    'use strict';

    var theme = null;
    try { theme = localStorage.getItem('akml-theme'); } catch (e) { }
    // The stored value is composed into the theme stylesheet href below, so only known
    // theme names are accepted; anything else falls back to detection (S5).
    if (!/^(dark|light|high-contrast)$/.test(theme)) {
        theme = null;
    }
    if (!theme) {
        theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches
            ? 'light'
            : 'dark';
    }
    var link = document.getElementById('akml-theme-css');
    if (link && theme !== 'dark') { link.href = 'css/themes/' + theme + '.css'; }
    document.documentElement.setAttribute('data-akml-theme', theme);
})();

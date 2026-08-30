/*
 * Spec 034 T028 (US3): no-flash theme boot (FR-009). Lives in an external file (moved out of
 * App.razor) so the strict Content-Security-Policy (script-src 'self', no inline scripts)
 * holds. Loaded as a classic, non-deferred <script> in <head>: it runs before first paint,
 * so there is no flash of the wrong theme.
 *
 * Precedence -- shared with js/theme-toggle.js, which writes the same 'akml-theme' key and
 * swaps the same link/attribute:
 *     stored localStorage choice > prefers-contrast: more > prefers-color-scheme > dark
 *
 * UI-004: high-contrast.css shipped from the start and both scripts already accepted the name,
 * but nothing ever selected it -- the toggle only flipped dark/light and no media query looked
 * for it, so the file was unreachable unless a user hand-edited localStorage. A reader who has
 * asked their OS for more contrast now gets it by default.
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

    function prefers(query) {
        return window.matchMedia && window.matchMedia(query).matches;
    }

    if (!theme) {
        if (prefers('(prefers-contrast: more)') || prefers('(forced-colors: active)')) {
            theme = 'high-contrast';
        } else if (prefers('(prefers-color-scheme: light)')) {
            theme = 'light';
        } else {
            theme = 'dark';
        }
    }

    var link = document.getElementById('akml-theme-css');
    if (link && theme !== 'dark') { link.href = 'css/themes/' + theme + '.css'; }
    document.documentElement.setAttribute('data-akml-theme', theme);
})();

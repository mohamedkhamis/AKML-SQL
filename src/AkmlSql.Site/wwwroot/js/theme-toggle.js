/*
 * Spec 034 T028 (US3): header theme toggle (contracts/site-routes.md theming clause).
 * Precedence — shared with the external boot script (js/theme-boot.js), which runs first:
 *   stored localStorage choice > prefers-color-scheme > dark default (FR-009).
 * Clicking the header button flips dark <-> light, persists the explicit choice to
 * localStorage, swaps the id="akml-theme-css" stylesheet href, and updates the
 * data-akml-theme attribute (the same mechanism the boot script uses, so no flash).
 * Deferred; enhanced-nav safe (delegated click from document). Without JS the header
 * button is hidden by CSS (U24) and the boot-script theme still applies; once this
 * script is wired it reveals the button via the 'js-theme-toggle' class on <html>.
 */
(function () {
    'use strict';

    var KEY = 'akml-theme';
    var BUTTON_ID = 'theme-toggle';
    // Same allowlist the boot script enforces — anything else falls back to dark.
    var THEME_RE = /^(dark|light|high-contrast)$/;

    function currentTheme() {
        var theme = document.documentElement.getAttribute('data-akml-theme') || 'dark';
        if (!THEME_RE.test(theme)) {
            theme = 'dark';
        }

        return theme;
    }

    function updateButton() {
        var button = document.getElementById(BUTTON_ID);
        if (!button) {
            return;
        }

        var next = currentTheme() === 'light' ? 'dark' : 'light';
        var label = 'Switch to ' + next + ' theme';
        button.setAttribute('aria-label', label);
        button.setAttribute('title', label);
    }

    function apply(theme) {
        if (!THEME_RE.test(theme)) {
            theme = 'dark';
        }

        var link = document.getElementById('akml-theme-css');
        if (link) {
            link.href = 'css/themes/' + theme + '.css';
        }

        document.documentElement.setAttribute('data-akml-theme', theme);
        try {
            localStorage.setItem(KEY, theme);
        } catch (e) { }

        updateButton();
    }

    document.addEventListener('click', function (event) {
        var button = event.target && event.target.closest
            ? event.target.closest('#' + BUTTON_ID)
            : null;
        if (!button) {
            return;
        }

        apply(currentTheme() === 'light' ? 'dark' : 'light');
    });

    updateButton();
    // The toggle is now functional — reveal it (CSS hides the inert no-JS button).
    document.documentElement.classList.add('js-theme-toggle');
})();

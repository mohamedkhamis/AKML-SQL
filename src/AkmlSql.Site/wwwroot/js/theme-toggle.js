/*
 * Spec 034 T028 (US3): header theme control (contracts/site-routes.md theming clause).
 * Precedence -- shared with the external boot script (js/theme-boot.js), which runs first:
 *   stored localStorage choice > prefers-contrast: more > prefers-color-scheme > dark (FR-009).
 *
 * UI-004: this used to be a two-state dark/light flip, which left high-contrast.css unreachable
 * even though it shipped and both scripts accepted the name. It is now a three-option menu.
 * A11Y-006: each option is a real radio-semantics button carrying aria-checked, so assistive
 * tech reports which theme is active -- the old button only changed its aria-label.
 *
 * Deferred; enhanced-nav safe (delegated click from document). Without JS the control is hidden
 * by CSS (U24) and the boot-script theme still applies; this script reveals it via the
 * 'js-theme-toggle' class on <html>.
 */
(function () {
    'use strict';

    var KEY = 'akml-theme';
    var MENU_ID = 'theme-menu';
    var BUTTON_ID = 'theme-toggle';
    // Same allowlist the boot script enforces -- anything else falls back to dark.
    var THEME_RE = /^(dark|light|high-contrast)$/;

    function currentTheme() {
        var theme = document.documentElement.getAttribute('data-akml-theme') || 'dark';
        return THEME_RE.test(theme) ? theme : 'dark';
    }

    function closeMenu() {
        var menu = document.getElementById(MENU_ID);
        var button = document.getElementById(BUTTON_ID);
        if (menu) { menu.hidden = true; }
        if (button) { button.setAttribute('aria-expanded', 'false'); }
    }

    function syncState() {
        var theme = currentTheme();
        var button = document.getElementById(BUTTON_ID);
        if (button) {
            button.setAttribute('aria-label', 'Colour theme: ' + label(theme) + '. Change theme');
            button.setAttribute('title', 'Colour theme: ' + label(theme));
        }

        var options = document.querySelectorAll('#' + MENU_ID + ' [data-theme-value]');
        for (var i = 0; i < options.length; i++) {
            var value = options[i].getAttribute('data-theme-value');
            options[i].setAttribute('aria-checked', value === theme ? 'true' : 'false');
        }
    }

    function label(theme) {
        if (theme === 'light') { return 'Light'; }
        if (theme === 'high-contrast') { return 'High contrast'; }
        return 'Dark';
    }

    function apply(theme) {
        if (!THEME_RE.test(theme)) { theme = 'dark'; }

        var link = document.getElementById('akml-theme-css');
        if (link) { link.href = 'css/themes/' + theme + '.css'; }

        document.documentElement.setAttribute('data-akml-theme', theme);
        try { localStorage.setItem(KEY, theme); } catch (e) { }

        syncState();
    }

    document.addEventListener('click', function (event) {
        var target = event.target;
        if (!target || !target.closest) { return; }

        var option = target.closest('[data-theme-value]');
        if (option) {
            apply(option.getAttribute('data-theme-value'));
            closeMenu();
            var button = document.getElementById(BUTTON_ID);
            if (button) { button.focus(); }
            return;
        }

        var toggle = target.closest('#' + BUTTON_ID);
        if (toggle) {
            var menu = document.getElementById(MENU_ID);
            if (menu) {
                var opening = menu.hidden;
                menu.hidden = !opening;
                toggle.setAttribute('aria-expanded', opening ? 'true' : 'false');
            }

            return;
        }

        // A click anywhere else dismisses an open menu.
        closeMenu();
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            var menu = document.getElementById(MENU_ID);
            if (menu && !menu.hidden) {
                closeMenu();
                var button = document.getElementById(BUTTON_ID);
                if (button) { button.focus(); }
            }
        }
    });

    syncState();
    // The control is now functional -- reveal it (CSS hides the inert no-JS version).
    document.documentElement.classList.add('js-theme-toggle');
})();

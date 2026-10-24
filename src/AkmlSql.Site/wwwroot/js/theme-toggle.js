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
    // Enhanced navigation replaces the server-rendered html attributes with their defaults.
    // Keep the active choice in memory too, including when localStorage is unavailable.
    var selectedTheme = currentTheme();

    function currentTheme() {
        var theme = document.documentElement.getAttribute('data-akml-theme') || 'dark';
        return THEME_RE.test(theme) ? theme : 'dark';
    }

    // Scrolls to the URL's #fragment (a link into a docs heading), if the page has it.
    function scrollToHash() {
        if (!location.hash || location.hash.length < 2) { return; }
        var target;
        try { target = document.getElementById(decodeURIComponent(location.hash.slice(1))); }
        catch (e) { return; }
        if (target) { target.scrollIntoView(); }
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

    function apply(theme, persist) {
        if (!THEME_RE.test(theme)) { theme = 'dark'; }
        selectedTheme = theme;

        var link = document.getElementById('akml-theme-css');
        if (link) { link.href = 'css/themes/' + theme + '.css'; }

        document.documentElement.setAttribute('data-akml-theme', theme);
        if (persist !== false) {
            try { localStorage.setItem(KEY, theme); } catch (e) { }
        }

        syncState();
        document.documentElement.classList.add('js-theme-toggle');
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
                // Radio-group pattern: opening puts focus on the active option, so the arrow
                // keys work at once instead of only after a Tab into the menu.
                if (opening) {
                    var checked = menu.querySelector('[aria-checked="true"]') || menu.querySelector('[data-theme-value]');
                    if (checked) { checked.focus(); }
                }
            }

            return;
        }

        // A click anywhere else dismisses an open menu.
        closeMenu();
    });

    document.addEventListener('keydown', function (event) {
        var menu = document.getElementById(MENU_ID);
        if (!menu || menu.hidden) { return; }

        if (event.key === 'Escape') {
            closeMenu();
            var button = document.getElementById(BUTTON_ID);
            if (button) { button.focus(); }
            return;
        }

        // Radio-group keys while focus is inside the menu: arrows, Home and End move to an option and
        // choose it, as a radio group does. The menu stays open; Enter, Escape or a click closes it.
        if (!menu.contains(document.activeElement)) { return; }
        var options = Array.prototype.slice.call(menu.querySelectorAll('[data-theme-value]'));
        if (options.length === 0) { return; }
        var index = options.indexOf(document.activeElement);
        var next = -1;
        if (event.key === 'ArrowDown' || event.key === 'ArrowRight') { next = index < 0 ? 0 : (index + 1) % options.length; }
        else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') { next = index < 0 ? options.length - 1 : (index - 1 + options.length) % options.length; }
        else if (event.key === 'Home') { next = 0; }
        else if (event.key === 'End') { next = options.length - 1; }
        if (next >= 0) {
            event.preventDefault();
            options[next].focus();
            apply(options[next].getAttribute('data-theme-value'));
        }
    });

    // Focus leaving the picker closes the menu, as a click elsewhere does -- including focus that
    // leaves the page (Tab past the last control, or F6 into the address bar). That case has no
    // related target, but neither does a click on an option in Safari, which does not focus
    // buttons: closing then would hide the option before its click lands. So without a target the
    // menu closes only once the document itself has lost focus.
    document.addEventListener('focusout', function (event) {
        var menu = document.getElementById(MENU_ID);
        if (!menu || menu.hidden) { return; }
        var picker = menu.closest('.theme-picker');
        if (!picker) { return; }
        if (event.relatedTarget) {
            if (!picker.contains(event.relatedTarget)) { closeMenu(); }
            return;
        }

        setTimeout(function () {
            if (!document.hasFocus()) { closeMenu(); }
        }, 0);
    });

    apply(selectedTheme, false);
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', function () {
            // Restore the stylesheet, screenshot variant and accessible picker state together.
            // Merely navigating must not save an OS-derived default as an explicit preference.
            var link = document.getElementById('akml-theme-css');
            var swappedIn = link ? link.getAttribute('href') : null;
            apply(selectedTheme, false);
            closeMenu();
            // The swapped-in page carries the server's default stylesheet, and Blazor has already
            // scrolled to the link's #fragment with it. When apply() changes the stylesheet, the
            // page reflows once it loads and the heading slid half under the sticky header:
            // scroll to it again then.
            if (link && link.getAttribute('href') !== swappedIn) {
                link.addEventListener('load', scrollToHash, { once: true });
            }
        });
    }
})();

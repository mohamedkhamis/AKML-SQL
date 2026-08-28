/*
 * Site redesign: progressive enhancement for the CSS-only mobile nav. The menu itself is a
 * checkbox + :has() pattern that needs no JS at all — with JavaScript disabled every
 * navigation is a full page load that resets the box, so the menu never sticks open.
 * With JS enabled, Blazor enhanced navigation patches the DOM without a reload, which
 * would preserve the checked state — so this tiny script closes the menu when a nav link
 * is activated, and on Escape. Deferred + external: strict CSP (script-src 'self') holds.
 */
(function () {
    'use strict';

    function closeMenu() {
        var box = document.querySelector('.nav-toggle');
        if (box) {
            box.checked = false;
        }
    }

    document.addEventListener('click', function (event) {
        var link = event.target && event.target.closest
            ? event.target.closest('.site-nav-links a')
            : null;
        if (link) {
            closeMenu();
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            closeMenu();
        }
    });
})();

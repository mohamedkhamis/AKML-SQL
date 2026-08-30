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

    /*
     * A11Y-004: the CSS-only checkbox pattern works without JS, but a checkbox announces as
     * "checkbox", not "menu, collapsed". Mirror its state into aria-expanded on the label so
     * assistive tech reports open/closed. The no-JS path is unchanged — without this script the
     * menu still opens, it just doesn't announce the state.
     */
    function syncExpanded(box) {
        var label = box && box.closest ? box.closest('.nav-toggle-btn') : null;
        if (label) {
            label.setAttribute('aria-expanded', box.checked ? 'true' : 'false');
        }
    }

    function closeMenu() {
        var box = document.querySelector('.nav-toggle');
        if (box) {
            box.checked = false;
            syncExpanded(box);
        }
    }

    document.addEventListener('change', function (event) {
        if (event.target && event.target.classList.contains('nav-toggle')) {
            syncExpanded(event.target);
        }
    });

    var initial = document.querySelector('.nav-toggle');
    if (initial) {
        syncExpanded(initial);
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

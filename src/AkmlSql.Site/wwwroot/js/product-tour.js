/* Progressive enhancement for the static homepage tour. Without this script every
 * example remains visible; ProductScreenshot's native links and existing viewer work.
 * Delegated controls and enhancedload initialization survive Blazor page navigation. */
(function () {
    'use strict';

    function select(tour, button) {
        var id = button.getAttribute('aria-controls');
        tour.querySelectorAll('.studio-tour-button').forEach(function (item) {
            item.setAttribute('aria-pressed', item === button ? 'true' : 'false');
        });
        tour.querySelectorAll('.studio-tour-panel').forEach(function (panel) {
            panel.hidden = panel.id !== id;
        });
    }

    function initialize() {
        document.querySelectorAll('.studio-tour').forEach(function (tour) {
            if (tour.dataset.tourReady) { return; }
            var first = tour.querySelector('.studio-tour-button');
            if (!first) { return; }
            tour.dataset.tourReady = 'true';
            tour.querySelector('.tour-controls').hidden = false;
            select(tour, first);
        });
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest && event.target.closest('.studio-tour-button');
        if (button) { select(button.closest('.studio-tour'), button); }
    });
    initialize();
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', initialize);
    }
})();

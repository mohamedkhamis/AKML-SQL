// DL-004: upgrade tracked download links to their CDN URL so the visitor's click goes
// straight to the CDN (no /dl redirect hop — the hop made the browser start a page
// navigation before the save dialog appeared). The click is still counted server-side
// via a same-origin beacon to /dl-count/{file}. No-JS users keep the /dl link, which
// already 302s to the same CDN URL with the metric logged.
(function () {
    'use strict';

    var links = document.querySelectorAll('a.download-tracked[data-cdn-url]');
    for (var i = 0; i < links.length; i++) {
        upgrade(links[i]);
    }

    function upgrade(a) {
        var cdn = a.getAttribute('data-cdn-url');
        var file = a.getAttribute('data-file');
        if (!cdn || !file) {
            return;
        }

        a.href = cdn;
        a.addEventListener('click', function () {
            try {
                if (navigator.sendBeacon) {
                    navigator.sendBeacon('/dl-count/' + encodeURIComponent(file));
                }
            } catch (e) {
                // Metrics must never block or break the download.
            }
        });
    }
})();

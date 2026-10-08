// Loaded once from App.razor. Delegation survives Blazor's enhanced DOM replacements.
(function () {
    'use strict';
    if (window.akmlDownloadTracking) return;
    window.akmlDownloadTracking = true;
    var busyUntil = new Map();
    document.addEventListener('click', function (event) {
        var a = event.target.closest && event.target.closest('a.download-tracked');
        if (!a || event.button !== 0 || event.defaultPrevented) return;
        var file = a.getAttribute('data-file');
        if (!file) return;
        var now = Date.now();
        if ((busyUntil.get(file) || 0) > now) {
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
        busyUntil.set(file, now + 2000);
        a.setAttribute('aria-disabled', 'true');
        a.setAttribute('aria-busy', 'true');
        var label = a.querySelector('[data-download-label]') || a;
        var original = label.textContent;
        label.textContent = 'Starting download…';
        setTimeout(function () {
            busyUntil.delete(file);
            a.removeAttribute('aria-disabled');
            a.removeAttribute('aria-busy');
            label.textContent = original;
        }, 2000);
        // Queue analytics before native navigation; never await the network.
        // Local legacy links count at /dl, so only direct CDN links send a beacon.
        if (a.getAttribute('data-cdn-url')) {
            var url = '/dl-count/' + encodeURIComponent(file);
            var queued = false;
            try { queued = !!navigator.sendBeacon && navigator.sendBeacon(url); } catch (_) { }
            if (!queued) {
                try { fetch(url, { method: 'POST', keepalive: true, credentials: 'same-origin' }).catch(function () {}); } catch (_) { }
            }
        }
        // href already names the asset in SSR markup, including with JS disabled.
    }, true);
})();

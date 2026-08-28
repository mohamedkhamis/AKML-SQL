/*
 * U22: copy-to-clipboard affordance for the SHA-256 digest on /download.
 * Deferred external script (strict CSP: script-src 'self', no inline handlers).
 * Buttons are hidden by CSS until this script marks <html> with 'js-copy-hash', so
 * no-JS users never see a dead button. Enhanced-nav safe: the click handler is
 * delegated from document, and every button carries a data-copy-target pointing at
 * the element whose text it copies.
 */
(function () {
    'use strict';

    function copyText(text, onDone) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(
                function () { onDone(true); },
                function () { onDone(fallbackCopy(text)); });
            return;
        }

        onDone(fallbackCopy(text));
    }

    // Deprecated but harmless silent fallback for engines without the async clipboard API.
    function fallbackCopy(text) {
        var area = document.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        area.style.position = 'absolute';
        area.style.left = '-9999px';
        document.body.appendChild(area);
        area.select();
        var ok = false;
        try {
            ok = document.execCommand('copy');
        } catch (e) { }
        document.body.removeChild(area);
        return ok;
    }

    document.addEventListener('click', function (event) {
        var button = event.target && event.target.closest
            ? event.target.closest('.copy-hash-btn')
            : null;
        if (!button) {
            return;
        }

        var target = document.getElementById(button.getAttribute('data-copy-target'));
        if (!target) {
            return;
        }

        copyText(target.textContent.trim(), function (ok) {
            if (!ok) {
                // Clipboard unavailable — hide the affordance rather than offer a dead button.
                button.style.display = 'none';
                return;
            }

            var label = button.textContent;
            button.textContent = 'Copied!';
            button.classList.add('copied');
            window.setTimeout(function () {
                button.textContent = label;
                button.classList.remove('copied');
            }, 1500);
        });
    });

    // Copy support is wired — reveal the buttons (CSS hides them by default).
    document.documentElement.classList.add('js-copy-hash');
})();

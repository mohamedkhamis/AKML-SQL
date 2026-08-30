/*
 * Copy-to-clipboard affordance, used in two places:
 *   U22    - the SHA-256 digest on /download (a button with data-copy-target)
 *   DOC-005 - every code block in the documentation (buttons injected below)
 *
 * DOC-005: the docs are full of SQL and shell snippets and offered no way to copy any of them,
 * while this script -- clipboard fallback, hidden-until-JS pattern and all -- already existed
 * for a single hash on one page. Generalised rather than duplicated.
 *
 * Deferred external script (strict CSP: script-src 'self', no inline handlers). Buttons are
 * hidden by CSS until this script marks <html> with 'js-copy-hash', so no-JS users never see a
 * dead button. Enhanced-nav safe: the click handler is delegated from document.
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

    // DOC-005: wrap every documentation code block and give it a copy button. Done in JS rather
    // than in the Markdown renderer so the server keeps emitting plain <pre>, and a reader
    // without JS gets exactly the markup they did before.
    function decorateCodeBlocks() {
        var blocks = document.querySelectorAll('.doc-body pre');
        for (var i = 0; i < blocks.length; i++) {
            var pre = blocks[i];
            if (pre.parentNode && pre.parentNode.classList.contains('code-block')) {
                continue; // already decorated (enhanced navigation re-runs this)
            }

            var wrapper = document.createElement('div');
            wrapper.className = 'code-block';
            pre.parentNode.insertBefore(wrapper, pre);
            wrapper.appendChild(pre);

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'copy-hash-btn copy-code-btn';
            button.textContent = 'Copy';
            button.setAttribute('aria-label', 'Copy code to clipboard');
            wrapper.appendChild(button);
        }
    }

    document.addEventListener('click', function (event) {
        var button = event.target && event.target.closest
            ? event.target.closest('.copy-hash-btn')
            : null;
        if (!button) {
            return;
        }

        // A code-block button copies its sibling <pre>; the digest button names its target by id.
        var target = button.classList.contains('copy-code-btn')
            ? button.parentNode.querySelector('pre')
            : document.getElementById(button.getAttribute('data-copy-target'));
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

    decorateCodeBlocks();
    // Blazor enhanced navigation swaps the article without re-running this script, so re-decorate
    // when the docs body changes. Guarded above, so re-entry is harmless.
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', decorateCodeBlocks);
    }
})();

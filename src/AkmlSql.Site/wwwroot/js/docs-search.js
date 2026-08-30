/*
 * Spec 034 T027 (US2): full-text docs search — progressive enhancement over the no-JS
 * title filter (contracts/docs-content.md FR-007). Deferred script on the docs layout:
 * fetches the startup-generated /search-index.json, builds a MiniSearch index over
 * title/headings/body (prefix + typo tolerance), and renders results next to the sidebar.
 * When JS is absent (or the index fetch fails) the GET-form title filter still works and
 * the search box stays hidden — the 'js' class on <html> is what reveals it.
 *
 * Enhanced-nav safe: input handling is delegated from document, so Blazor enhanced
 * navigation re-rendering the docs layout keeps working without rebinding.
 */
(function () {
    'use strict';

    var BOX_ID = 'docs-search';
    var INPUT_ID = 'docs-search-input';
    var RESULTS_ID = 'docs-search-results';
    var STATUS_ID = 'docs-search-status';
    var MAX_RESULTS = 8;
    var MIN_TERM = 2;
    // DOC-006: excerpt window around the first match.
    var EXCERPT_LEN = 120;
    var EXCERPT_PAD = 60;

    if (typeof MiniSearch === 'undefined') {
        return;
    }

    var mini = null;
    var lastTerm = null;
    var loading = null;
    // A11Y-007: index of the arrow-key-selected result (-1 = none).
    var activeIndex = -1;

    // PERF-002: the index used to be fetched on every docs page load, before the visitor had shown
    // any intent to search. It is now loaded on first contact with the search box (focus, or a
    // keystroke if focus was somehow missed), so a reader who never searches never pays for it.
    // The box is revealed immediately — it is a real control the moment it is focusable, and the
    // first query resolves against the in-flight fetch rather than being dropped.
    document.documentElement.classList.add('js');

    function ensureIndex() {
        if (loading) {
            return loading;
        }

        loading = fetch('/search-index.json')
            .then(function (response) {
                return response.ok ? response.json() : Promise.reject(new Error('search-index.json: ' + response.status));
            })
            .then(function (data) {
                mini = new MiniSearch({
                    idField: 'url',
                    fields: ['title', 'headings', 'body'],
                    // DOC-006: body is stored so a result can show a match excerpt.
                    storeFields: ['title', 'url', 'body'],
                    searchOptions: { prefix: true, fuzzy: 0.2 }
                });
                mini.addAll(data.documents || []);
            })
            .catch(function () {
                // Search unavailable (offline, index missing) — fall back to the no-JS title
                // filter by undoing the reveal above.
                document.documentElement.classList.remove('js');
                var box = document.getElementById(BOX_ID);
                if (box) { box.hidden = true; }
            });

        return loading;
    }

    // Warm the index as soon as the visitor reaches for the box, so the first keystroke is instant.
    document.addEventListener('focusin', function (event) {
        if (event.target && event.target.id === INPUT_ID) {
            ensureIndex();
        }
    });

    function currentResults() {
        return document.getElementById(RESULTS_ID);
    }

    function statusNode() {
        return document.getElementById(STATUS_ID);
    }

    function setStatus(text) {
        var node = statusNode();
        if (node) { node.textContent = text; }
        var input = document.getElementById(INPUT_ID);
        if (input) { input.setAttribute('aria-expanded', text ? 'true' : 'false'); }
    }

    function clearResults() {
        var results = currentResults();
        if (results) {
            results.innerHTML = '';
            results.hidden = true;
        }

        activeIndex = -1;
        setStatus('');
    }

    /*
     * DOC-006: results used to show a bare title. MiniSearch already indexes the body text, so
     * a short window around the first match is free -- and it is what tells a reader whether a
     * hit is the one they want.
     */
    function excerpt(body, term) {
        if (!body) { return ''; }

        var needle = term.toLowerCase();
        var at = body.toLowerCase().indexOf(needle);
        if (at < 0) {
            return body.slice(0, EXCERPT_LEN).trim() + (body.length > EXCERPT_LEN ? '…' : '');
        }

        var from = Math.max(0, at - EXCERPT_PAD);
        var to = Math.min(body.length, at + needle.length + EXCERPT_PAD);
        return (from > 0 ? '…' : '') + body.slice(from, to).trim() + (to < body.length ? '…' : '');
    }

    function showResults(matches, term) {
        var results = currentResults();
        if (!results) { return; }

        results.innerHTML = '';
        activeIndex = -1;

        if (matches.length === 0) {
            var none = document.createElement('li');
            none.className = 'docs-search-empty';
            none.textContent = 'No matches';
            results.appendChild(none);
            setStatus('No matches for “' + term + '”');
            results.hidden = false;
            return;
        }

        matches.forEach(function (match, index) {
            var item = document.createElement('li');
            item.setAttribute('role', 'option');
            item.setAttribute('aria-selected', 'false');
            item.id = RESULTS_ID + '-' + index;

            var link = document.createElement('a');
            link.href = match.url;

            var title = document.createElement('span');
            title.className = 'docs-search-title';
            title.textContent = match.title;
            link.appendChild(title);

            var text = excerpt(match.body, term);
            if (text) {
                var snippet = document.createElement('span');
                snippet.className = 'docs-search-excerpt';
                snippet.textContent = text;
                link.appendChild(snippet);
            }

            item.appendChild(link);
            results.appendChild(item);
        });

        setStatus(matches.length + (matches.length === 1 ? ' result' : ' results'));
        results.hidden = false;
    }

    /* A11Y-007: arrow keys move a roving selection through the results. */
    function moveActive(delta) {
        var results = currentResults();
        if (!results || results.hidden) { return; }

        var items = results.querySelectorAll('li[role="option"]');
        if (items.length === 0) { return; }

        if (activeIndex >= 0 && items[activeIndex]) {
            items[activeIndex].setAttribute('aria-selected', 'false');
            items[activeIndex].classList.remove('is-active');
        }

        activeIndex += delta;
        if (activeIndex < 0) { activeIndex = items.length - 1; }
        if (activeIndex >= items.length) { activeIndex = 0; }

        var active = items[activeIndex];
        active.setAttribute('aria-selected', 'true');
        active.classList.add('is-active');
        active.scrollIntoView({ block: 'nearest' });

        var input = document.getElementById(INPUT_ID);
        if (input) { input.setAttribute('aria-activedescendant', active.id); }
    }

    function activeHref() {
        var results = currentResults();
        if (!results || results.hidden) { return null; }

        var items = results.querySelectorAll('li[role="option"] a');
        if (items.length === 0) { return null; }

        return (activeIndex >= 0 ? items[activeIndex] : items[0]).href;
    }

    document.addEventListener('input', function (event) {
        if (!event.target || event.target.id !== INPUT_ID) {
            return;
        }

        var term = event.target.value.trim();
        if (term === lastTerm) {
            return;
        }

        lastTerm = term;
        if (term.length < MIN_TERM) {
            clearResults();
            return;
        }

        // The index may still be in flight (a fast typist, or focus never fired). Resolve against
        // it, then re-read the field: results must reflect what is in the box now, not the term
        // that started the fetch.
        ensureIndex().then(function () {
            if (!mini) { return; }

            var input = document.getElementById(INPUT_ID);
            var current = input ? input.value.trim() : term;
            if (current.length < MIN_TERM) {
                clearResults();
                return;
            }

            showResults(mini.search(current).slice(0, MAX_RESULTS), current);
        });
    });

    document.addEventListener('keydown', function (event) {
        if (!event.target || event.target.id !== INPUT_ID) {
            return;
        }

        if (event.key === 'ArrowDown') {
            event.preventDefault();
            moveActive(1);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            moveActive(-1);
        } else if (event.key === 'Enter') {
            var href = activeHref();
            if (href) {
                event.preventDefault();
                window.location.href = href;
            }
        } else if (event.key === 'Escape') {
            clearResults();
        }
    });

    // A click outside the search box dismisses the results.
    document.addEventListener('click', function (event) {
        var box = document.getElementById(BOX_ID);
        if (box && event.target && !box.contains(event.target)) {
            clearResults();
        }
    });

})();

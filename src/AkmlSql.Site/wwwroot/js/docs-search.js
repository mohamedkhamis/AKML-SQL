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
    var MAX_RESULTS = 8;
    var MIN_TERM = 2;

    if (typeof MiniSearch === 'undefined') {
        return;
    }

    var mini = null;
    var lastTerm = null;
    var loading = null;

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
                    storeFields: ['title', 'url'],
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

    function clearResults() {
        var results = currentResults();
        if (results) {
            results.innerHTML = '';
            results.hidden = true;
        }
    }

    function showResults(matches) {
        var results = currentResults();
        if (!results) {
            return;
        }

        results.innerHTML = '';
        if (matches.length === 0) {
            var none = document.createElement('li');
            none.className = 'docs-search-empty';
            none.textContent = 'No matches';
            results.appendChild(none);
        } else {
            matches.forEach(function (match) {
                var item = document.createElement('li');
                var link = document.createElement('a');
                link.href = match.url;
                link.textContent = match.title;
                item.appendChild(link);
                results.appendChild(item);
            });
        }

        results.hidden = false;
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
            if (!mini) {
                return;
            }

            var input = document.getElementById(INPUT_ID);
            var current = input ? input.value.trim() : term;
            if (current.length < MIN_TERM) {
                clearResults();
                return;
            }

            showResults(mini.search(current).slice(0, MAX_RESULTS));
        });
    });

    document.addEventListener('keydown', function (event) {
        if (!mini || !event.target || event.target.id !== INPUT_ID) {
            return;
        }

        if (event.key === 'Enter') {
            var first = currentResults();
            first = first && !first.hidden ? first.querySelector('a') : null;
            if (first) {
                event.preventDefault();
                window.location.href = first.href;
            }
        } else if (event.key === 'Escape') {
            clearResults();
        }
    });
})();

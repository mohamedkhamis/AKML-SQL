/*
 * Screenshot viewer for ProductScreenshot (Home hero, /features media rows) and for the images
 * in the docs (MarkdownRenderer wraps each published content image in a .doc-image-link).
 *
 * A click on a screenshot used to open the bare PNG in a new browser tab: the reader left the
 * page, lost the caption and the text explaining it, and the tab ignored their theme. It now
 * opens an in-page gallery (design B of the screenshot-viewer review): the screenshot grows out
 * of the page into a native modal <dialog>, as large as it stays sharp; every screenshot on the
 * page can be stepped through with the arrows, the arrow keys / Home / End, a swipe, or the
 * thumbnail strip, with the section's heading and text beside it. A page with one screenshot
 * (Home) gets the same viewer without the gallery controls.
 *
 * Constraints this honours:
 *  - Strict CSP (script-src 'self'; style-src 'self'): external deferred file, no inline handlers,
 *    no style="" markup; sizes go through the CSSOM and motion through the Web Animations API,
 *    both of which CSP allows.
 *  - Blazor enhanced navigation: listeners are delegated from document; the dialog is created on
 *    first use and re-created if a page swap removed it; an open viewer closes on navigation.
 *    The click listener runs in the CAPTURE phase so it can preventDefault before blazor.web.js's
 *    own link interception (which ignores defaultPrevented clicks) sees the click.
 *  - Progressive enhancement: the links keep their real href, so without JavaScript, or with a
 *    modifier key, the browser opens the PNG itself.
 *  - prefers-reduced-motion: no zoom or slide, only a short fade.
 *  - Theme: shows the capture matching html[data-akml-theme] (dark for high contrast, as the
 *    page does) and follows a theme change made while it is open.
 */
(function () {
    'use strict';

    var root = document.documentElement;
    var VIEWER_ID = 'screenshot-viewer';
    var EASE = 'cubic-bezier(0.2, 0.8, 0.2, 1)';

    var ICON_CLOSE = '<svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true" focusable="false"><path d="M5 5l10 10M15 5L5 15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>';
    var ICON_PREV = '<svg width="22" height="22" viewBox="0 0 22 22" aria-hidden="true" focusable="false"><path d="M13.5 5l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var ICON_NEXT = '<svg width="22" height="22" viewBox="0 0 22 22" aria-hidden="true" focusable="false"><path d="M8.5 5l6 6-6 6" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>';

    // Static, trusted markup — no page text is ever inserted as HTML (textContent only).
    var MARKUP =
        '<div class="viewer-stage">' +
            '<div class="viewer-top">' +
                '<p class="viewer-count" id="' + VIEWER_ID + '-count"></p>' +
                '<p class="visually-hidden viewer-status" aria-live="polite"></p>' +
                '<button type="button" class="viewer-btn viewer-close" data-viewer-close aria-label="Close">' + ICON_CLOSE + '</button>' +
            '</div>' +
            '<button type="button" class="viewer-btn viewer-nav viewer-prev" data-viewer-step="-1" aria-label="Previous screenshot">' + ICON_PREV + '</button>' +
            '<figure class="viewer-figure">' +
                '<div class="viewer-frame"><img class="viewer-img" alt="" /></div>' +
                '<figcaption class="viewer-caption" id="' + VIEWER_ID + '-caption"></figcaption>' +
            '</figure>' +
            '<aside class="viewer-notes" aria-label="About this screenshot">' +
                '<p class="eyebrow viewer-eyebrow"></p>' +
                '<h2 class="viewer-heading"></h2>' +
                '<p class="viewer-text"></p>' +
            '</aside>' +
            '<button type="button" class="viewer-btn viewer-nav viewer-next" data-viewer-step="1" aria-label="Next screenshot">' + ICON_NEXT + '</button>' +
            '<div class="viewer-strip" role="group" aria-label="All screenshots"></div>' +
        '</div>';

    var dlg = null;
    var ui = null;
    var items = [];
    var index = 0;
    var openerFig = null;
    var closing = false;
    var stepToken = 0;
    // One per open: a close still finishing from an earlier open must never shut a new one.
    var session = 0;

    // ─── Helpers ────────────────────────────────────────────────────────────

    function reduced() {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    function variant() {
        return root.getAttribute('data-akml-theme') === 'light' ? 'light' : 'dark';
    }

    function narrow() {
        return window.innerWidth <= 900;
    }

    function animate(el, frames, options) {
        return el && el.animate ? el.animate(frames, options) : null;
    }

    function whenDone(animation, callback) {
        if (animation && animation.finished) {
            animation.finished.then(callback, callback);
        } else {
            callback();
        }
    }

    function cancelAnimations(el) {
        if (el && el.getAnimations) {
            el.getAnimations().forEach(function (a) { a.cancel(); });
        }
    }

    function text(el) {
        return el ? el.textContent.replace(/\s+/g, ' ').trim() : '';
    }

    // What the viewer can open: a ProductScreenshot <figure> (one link per theme variant) or a
    // docs content image, which MarkdownRenderer wraps in a .doc-image-link to itself.
    var VIEWABLE = '.screenshot-frame, .doc-image-link';

    function isDocImage(el) {
        return !!el && el.classList.contains('doc-image-link');
    }

    function shownLink(el) {
        if (!el) {
            return null;
        }
        return isDocImage(el) ? el : el.querySelector('.screenshot-' + variant());
    }

    function shownImg(el) {
        var link = shownLink(el);
        return link ? link.querySelector('img') : null;
    }

    function rectToRect(from, to) {
        var scale = to.width ? from.width / to.width : 1;
        return 'translate(' + (from.left - to.left) + 'px, ' + (from.top - to.top) + 'px) scale(' + scale + ')';
    }

    function inViewport(rect) {
        return rect.width > 0 && rect.bottom > 0 && rect.top < window.innerHeight;
    }

    function describe(fig) {
        if (isDocImage(fig)) {
            // One image for every theme; its size is known only once it has loaded (content
            // images are lazy and carry no width/height) — layout() fills it in.
            var docImg = fig.querySelector('img');
            var href = fig.getAttribute('href');
            return {
                fig: fig,
                kind: 'Image',
                src: { dark: href, light: href },
                w: docImg.naturalWidth || 0,
                h: docImg.naturalHeight || 0,
                alt: docImg.getAttribute('alt') || '',
                caption: docImg.getAttribute('title') || docImg.getAttribute('alt') || 'Image',
                eyebrow: '',
                heading: '',
                body: ''
            };
        }
        var img = fig.querySelector('.screenshot-variant img');
        var row = fig.closest('.media-row');
        var paras = row ? row.querySelectorAll('.media-text p:not(.eyebrow)') : [];
        var dark = fig.querySelector('.screenshot-dark');
        var light = fig.querySelector('.screenshot-light');
        return {
            fig: fig,
            kind: 'Screenshot',
            src: {
                dark: dark ? dark.getAttribute('href') : img.getAttribute('src'),
                light: light ? light.getAttribute('href') : img.getAttribute('src')
            },
            w: parseInt(img.getAttribute('width'), 10) || img.naturalWidth || 800,
            h: parseInt(img.getAttribute('height'), 10) || img.naturalHeight || 500,
            alt: img.getAttribute('alt') || '',
            caption: text(fig.querySelector('.screenshot-caption-text')),
            eyebrow: row ? text(row.querySelector('.media-text .eyebrow')) : '',
            heading: row ? text(row.querySelector('.media-text h2')) : '',
            body: paras.length ? text(paras[paras.length - 1]) : ''
        };
    }

    // ─── The dialog ─────────────────────────────────────────────────────────

    function ensureDialog() {
        if (dlg && dlg.isConnected) {
            return;
        }
        dlg = document.getElementById(VIEWER_ID);
        if (!dlg) {
            dlg = document.createElement('dialog');
            dlg.id = VIEWER_ID;
            dlg.className = 'viewer';
            // Named by the caption, described by "3 of 7": opening announces both.
            dlg.setAttribute('aria-labelledby', VIEWER_ID + '-caption');
            dlg.setAttribute('aria-describedby', VIEWER_ID + '-count');
            dlg.innerHTML = MARKUP;
            document.body.appendChild(dlg);
        }
        ui = {
            figure: dlg.querySelector('.viewer-figure'),
            frame: dlg.querySelector('.viewer-frame'),
            img: dlg.querySelector('.viewer-img'),
            caption: dlg.querySelector('.viewer-caption'),
            count: dlg.querySelector('.viewer-count'),
            status: dlg.querySelector('.viewer-status'),
            eyebrow: dlg.querySelector('.viewer-eyebrow'),
            heading: dlg.querySelector('.viewer-heading'),
            text: dlg.querySelector('.viewer-text'),
            strip: dlg.querySelector('.viewer-strip'),
            prev: dlg.querySelector('.viewer-prev'),
            next: dlg.querySelector('.viewer-next'),
            close: dlg.querySelector('.viewer-close')
        };
        if (!dlg.hasAttribute('data-viewer-bound')) {
            dlg.setAttribute('data-viewer-bound', '');
            bindDialog();
        }
    }

    function isGallery() {
        return dlg.classList.contains('is-gallery');
    }

    function buildStrip() {
        ui.strip.textContent = '';
        if (!isGallery()) {
            return;
        }
        items.forEach(function (item, i) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'viewer-thumb';
            b.setAttribute('aria-label', item.kind + ' ' + (i + 1) + ': ' + item.caption);
            b.setAttribute('data-index', String(i));
            var t = document.createElement('img');
            t.alt = '';
            t.loading = 'lazy';
            t.decoding = 'async';
            b.appendChild(t);
            ui.strip.appendChild(b);
        });
    }

    function render() {
        var item = items[index];
        ui.img.src = item.src[variant()];
        ui.img.alt = item.alt;
        ui.caption.textContent = item.caption;
        // The notes column only when there is text to put in it (Features sections, not docs
        // images) — otherwise the picture gets that width.
        dlg.classList.toggle('has-notes', !!item.body);
        if (!isGallery()) {
            ui.count.textContent = '';
            return;
        }
        ui.count.textContent = (index + 1) + ' of ' + items.length;
        ui.eyebrow.textContent = item.eyebrow;
        ui.heading.textContent = item.heading;
        ui.text.textContent = item.body;
        // aria-disabled, not disabled: a disabled button that has focus drops it to <body>,
        // where the arrow keys no longer reach the dialog. goTo() ignores out-of-range steps.
        ui.prev.setAttribute('aria-disabled', index === 0 ? 'true' : 'false');
        ui.next.setAttribute('aria-disabled', index === items.length - 1 ? 'true' : 'false');
        var thumbs = ui.strip.querySelectorAll('.viewer-thumb');
        for (var i = 0; i < thumbs.length; i++) {
            thumbs[i].setAttribute('aria-current', i === index ? 'true' : 'false');
            thumbs[i].querySelector('img').src = items[i].src[variant()];
        }
        // Scroll the strip itself only — scrollIntoView would also scroll the (narrow-screen)
        // stage down to the strip and push the new screenshot out of view.
        var current = thumbs[index];
        if (current && ui.strip.scrollWidth > ui.strip.clientWidth) {
            ui.strip.scrollLeft = Math.max(0, current.offsetLeft - (ui.strip.clientWidth - current.offsetWidth) / 2);
        }
        // Warm the neighbours so stepping never waits on the network.
        [index - 1, index + 1].forEach(function (n) {
            if (items[n]) {
                new Image().src = items[n].src[variant()];
            }
        });
    }

    // As large as the screen allows, never past the capture's own pixels (that only blurs it) —
    // but never smaller than it was on the page either: small captures are already upscaled there.
    function layout() {
        var item = items[index];
        ui.img.style.width = '';
        ui.frame.style.width = '';
        ui.caption.style.width = '';
        var cell = ui.figure.getBoundingClientRect();
        var captionH = ui.caption.getBoundingClientRect().height || 44;
        var availW = cell.width - 2;
        // The narrow gallery scrolls, so the picture takes the full width there: capping it to a
        // landscape phone's height showed it smaller than it was on the page.
        var availH = isGallery() && narrow()
            ? Infinity
            : cell.height - captionH - 2;
        // offsetWidth, not the bounding box: the Home hero's perspective tilt inflates the latter.
        var onPage = isDocImage(item.fig)
            ? (shownImg(item.fig) || item.fig).offsetWidth
            : item.fig.offsetWidth - 2;
        var natW = item.w || ui.img.naturalWidth;
        var natH = item.h || ui.img.naturalHeight;
        if (!natW || !natH) {
            // A lazy docs image not loaded yet: lay out provisionally, then again once it is.
            natW = Math.max(onPage, 640);
            natH = natW * 0.625;
            if (ui.img.decode) {
                ui.img.decode().then(function () {
                    if (dlg.open && items[index] === item && ui.img.naturalWidth) {
                        item.w = ui.img.naturalWidth;
                        item.h = ui.img.naturalHeight;
                        layout();
                    }
                }, function () { /* broken image: keep the provisional size */ });
            }
        }
        var cap = Math.max(natW, onPage);
        var w = Math.min(availW, cap, availH * natW / natH);
        w = Math.max(120, Math.floor(w));
        ui.img.style.width = w + 'px';
        ui.frame.style.width = (w + 2) + 'px';
        ui.caption.style.width = (w + 2) + 'px';
    }

    function finishClose(closingSession) {
        if (closingSession !== undefined && closingSession !== session) {
            return;
        }
        closing = false;
        cancelAnimations(dlg);
        cancelAnimations(ui.frame);
        cancelAnimations(ui.caption);
        if (dlg.open) {
            dlg.close();
        }
        root.classList.remove('screenshot-viewer-open');
        // Back to the screenshot you opened, without moving the page.
        var back = shownLink(openerFig);
        if (back && back.isConnected) {
            back.focus({ preventScroll: true });
        }
    }

    function open(fig) {
        ensureDialog();
        if (dlg.open) {
            return;
        }
        var all = Array.prototype.slice.call(document.querySelectorAll(VIEWABLE));
        items = all.map(describe);
        index = Math.max(0, all.indexOf(fig));
        openerFig = fig;
        closing = false;
        session++;
        stepToken++;
        dlg.classList.toggle('is-gallery', items.length > 1);
        ui.status.textContent = '';
        buildStrip();
        render();
        root.classList.add('screenshot-viewer-open');
        dlg.showModal();
        layout();

        if (reduced()) {
            animate(dlg, [{ opacity: 0 }, { opacity: 1 }], { duration: 120 });
            return;
        }
        // The screenshot grows out of the one you clicked, so you see where it came from.
        var source = shownImg(fig);
        var from = source ? source.getBoundingClientRect() : null;
        var to = ui.frame.getBoundingClientRect();
        if (from && inViewport(from) && to.width) {
            animate(ui.frame,
                [{ transformOrigin: 'top left', transform: rectToRect(from, to) },
                 { transformOrigin: 'top left', transform: 'none' }],
                { duration: 340, easing: EASE });
            animate(ui.caption, [{ opacity: 0 }, { opacity: 0, offset: 0.6 }, { opacity: 1 }], { duration: 340 });
        }
        animate(dlg, [{ opacity: 0 }, { opacity: 1 }], { duration: 220, easing: 'ease-out' });
    }

    function close() {
        if (!dlg || !dlg.open || closing) {
            return;
        }
        closing = true;
        stepToken++;
        cancelAnimations(ui.frame);
        var done = finishClose.bind(null, session);

        var target = shownImg(openerFig);
        var to = target && target.isConnected ? target.getBoundingClientRect() : null;
        // Shrink back into the page only when that is where you are returning to.
        if (reduced() || !to || !inViewport(to) || items[index].fig !== openerFig) {
            whenDone(animate(dlg, [{ opacity: 1 }, { opacity: 0 }], { duration: reduced() ? 80 : 180 }), done);
            return;
        }
        var from = ui.frame.getBoundingClientRect();
        animate(ui.frame,
            [{ transformOrigin: 'top left', transform: 'none' },
             { transformOrigin: 'top left', transform: rectToRect(to, from) }],
            { duration: 280, easing: EASE, fill: 'forwards' });
        animate(ui.caption, [{ opacity: 1 }, { opacity: 0 }], { duration: 120, fill: 'forwards' });
        whenDone(animate(dlg, [{ opacity: 1 }, { opacity: 1, offset: 0.5 }, { opacity: 0 }], { duration: 280, fill: 'forwards' }), done);
    }

    // Closes at once, without animation — page navigation, bfcache restores.
    function closeNow() {
        if (dlg && dlg.open) {
            finishClose();
        }
        root.classList.remove('screenshot-viewer-open');
    }

    function goTo(i) {
        if (!dlg || !dlg.open || closing || !isGallery() || i < 0 || i >= items.length || i === index) {
            return;
        }
        var dir = i > index ? 1 : -1;
        var token = ++stepToken;
        index = i;
        cancelAnimations(ui.frame);
        // The picture changed under the reader's focus: say which one it is now.
        ui.status.textContent = items[i].kind + ' ' + (i + 1) + ' of ' + items.length + ': ' + items[i].caption;
        if (reduced()) {
            render();
            layout();
            return;
        }
        whenDone(animate(ui.frame,
            [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(' + (-24 * dir) + 'px)' }],
            { duration: 110, easing: 'ease-in', fill: 'forwards' }), function () {
            // A newer step (or a close) superseded this one while it faded out.
            if (token !== stepToken || !dlg.open) {
                return;
            }
            render();
            layout();
            var reveal = function () {
                if (token !== stepToken || !dlg.open) {
                    return;
                }
                cancelAnimations(ui.frame);
                animate(ui.frame,
                    [{ opacity: 0, transform: 'translateX(' + (24 * dir) + 'px)' }, { opacity: 1, transform: 'none' }],
                    { duration: 200, easing: EASE });
            };
            if (ui.img.decode) {
                ui.img.decode().then(reveal, reveal);
            } else {
                reveal();
            }
        });
    }

    function bindDialog() {
        dlg.addEventListener('cancel', function (event) {
            event.preventDefault();
            close();
        });

        dlg.addEventListener('close', function () {
            // The browser may close it itself — a second Esc while ours is still animating:
            // finish now, so the pending close cannot act later.
            if (closing) {
                finishClose(session);
            }
            root.classList.remove('screenshot-viewer-open');
        });

        // A click closes the viewer only when the press also began outside the picture: a text
        // selection dragged out of the notes ends in a click on the stage, and must not close it.
        var INSIDE = '.viewer-frame, .viewer-caption, .viewer-btn, .viewer-notes, .viewer-strip, .viewer-count';
        var pressedOutside = false;
        dlg.addEventListener('pointerdown', function (event) {
            pressedOutside = !event.target.closest(INSIDE);
        });

        dlg.addEventListener('click', function (event) {
            var target = event.target;
            var step = target.closest('[data-viewer-step]');
            if (step) {
                goTo(index + parseInt(step.getAttribute('data-viewer-step'), 10));
                return;
            }
            var thumb = target.closest('.viewer-thumb');
            if (thumb) {
                goTo(parseInt(thumb.getAttribute('data-index'), 10));
                return;
            }
            if (target.closest('[data-viewer-close]')) {
                close();
                return;
            }
            // Anywhere that is not the picture, its text or a control closes the viewer.
            if (pressedOutside && !target.closest(INSIDE)) {
                close();
            }
            pressedOutside = false;
        });

        dlg.addEventListener('keydown', function (event) {
            if (!isGallery()) {
                return;
            }
            var to = event.key === 'ArrowLeft' ? index - 1
                : event.key === 'ArrowRight' ? index + 1
                : event.key === 'Home' ? 0
                : event.key === 'End' ? items.length - 1
                : null;
            if (to !== null) {
                event.preventDefault();
                goTo(to);
            }
        });

        // Swipe on the picture: sideways steps through the gallery, down closes.
        var swipe = null;
        ui.frame.addEventListener('pointerdown', function (event) {
            swipe = event.pointerType === 'mouse' ? null : { x: event.clientX, y: event.clientY, id: event.pointerId };
        });
        ui.frame.addEventListener('pointerup', function (event) {
            if (!swipe || swipe.id !== event.pointerId) {
                return;
            }
            var dx = event.clientX - swipe.x;
            var dy = event.clientY - swipe.y;
            swipe = null;
            if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.3) {
                goTo(index + (dx < 0 ? 1 : -1));
            } else if (dy > 90 && dy > Math.abs(dx)) {
                close();
            }
        });
        ui.frame.addEventListener('pointercancel', function () {
            swipe = null;
        });
    }

    // ─── Page wiring ────────────────────────────────────────────────────────

    // Capture phase: runs before blazor.web.js's document-level link interception.
    document.addEventListener('click', function (event) {
        var link = event.target && event.target.closest
            ? event.target.closest('.screenshot-variant, .doc-image-link')
            : null;
        var item = !link ? null : isDocImage(link) ? link : link.closest('.screenshot-frame');
        if (!item) {
            return;
        }
        // A modifier or middle click keeps the browser's own behaviour (open the image).
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }
        event.preventDefault();
        open(item);
    }, true);

    // The viewer follows a theme change made while it is open (header theme picker).
    new MutationObserver(function () {
        if (dlg && dlg.open && !closing) {
            render();
        }
    }).observe(root, { attributes: true, attributeFilter: ['data-akml-theme'] });

    var resizeTimer = null;
    window.addEventListener('resize', function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            if (dlg && dlg.open && !closing) {
                layout();
            }
        }, 100);
    });

    // A page restored from the back/forward cache must not come back with a stuck viewer.
    window.addEventListener('pageshow', function (event) {
        if (event.persisted) {
            closeNow();
        }
    });

    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', closeNow);
    }
})();

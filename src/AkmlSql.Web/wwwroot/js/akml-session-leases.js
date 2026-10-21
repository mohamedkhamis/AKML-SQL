// Spec 043 (review of US1) — a window's lease on each engine session id it uses (Services/ISessionLeases.cs).
//
// Query tabs are stored per browser profile, so two windows of the app read the same engine session ids. An exclusive Web Lock
// per id, held for as long as the page lives, tells a live window apart from a page that is gone: the browser releases a page's
// locks when it unloads, so a reload takes its tabs' sessions back, and a second live window is refused and uses its own.
//
// A page that is not a secure context (http://<computer name>/) has no Web Locks. There the window asks the others over a
// BroadcastChannel and waits a moment: a live window that holds an id says so. A browser with neither cannot tell, and says so;
// the page then shares no stored id. A window that does not use a tab's stored session keeps the one it uses instead in
// sessionStorage, which lives as long as the browser tab, across its reloads.

const PREFIX = 'akml-engine-session-';
const CHANNEL = 'akml-engine-sessions';
const STORAGE_PREFIX = 'akml.window-session.';

/** How long a window waits for the others to say they hold an id it asks about. */
export const ANSWER_WAIT_MS = 150;

const held = new Map();        // Web Locks: session id -> function that releases its lock
const announced = new Set();   // BroadcastChannel: the ids this window holds
const asking = new Map();      // BroadcastChannel: id -> this window's question about it, still waiting for answers
let channel = null;
let batch = null;              // ids asked about in one turn of the event loop: one question, one wait

// Two windows asking about the same id at once (both restored at browser start) settle it the same way in both: the lower token
// wins. A window that holds an id but is busy for longer than the wait (a long render) is not heard; that case remains.
const windowToken = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

/** The Web Locks API, or null when the browser has none. */
function locks() {
    return (typeof navigator !== 'undefined' && navigator.locks && typeof navigator.locks.request === 'function') ? navigator.locks : null;
}

/** The channel the windows of the app ask each other on, or null when the browser has none. It answers questions about ids
 *  this window holds. */
function broadcast() {
    if (channel) return channel;
    if (typeof BroadcastChannel !== 'function') return null;
    try { channel = new BroadcastChannel(CHANNEL); } catch { return null; }
    channel.addEventListener('message', (e) => {
        const m = e.data;
        if (!m || m.type !== 'who-holds' || !Array.isArray(m.ids)) return;
        const mine = [];
        for (const id of m.ids) {
            if (announced.has(id)) { mine.push(id); continue; }
            const question = asking.get(id);
            if (!question) continue;
            if (windowToken < String(m.token)) mine.push(id);   // contested: this window wins it
            else question.lost.add(id);                           // contested: the other window wins it
        }
        if (mine.length) channel.postMessage({ type: 'held', ask: m.ask, ids: mine });
    });
    return channel;
}

/**
 * Claim the lease on `sessionId`: 'held' when this window now holds it (or already did), 'refused' when another live window
 * holds it, 'unavailable' when the browser cannot tell. `fresh`: an id this window has just made, which nobody else can hold.
 */
export function claimSession(sessionId, fresh) {
    if (!sessionId || held.has(sessionId) || announced.has(sessionId)) return Promise.resolve('held');
    const api = locks();
    if (api) return claimWithLock(api, sessionId, fresh);
    return claimByAsking(sessionId, fresh);
}

function claimWithLock(api, sessionId, fresh) {
    return new Promise(resolveClaim => {
        api.request(PREFIX + sessionId, { mode: 'exclusive', ifAvailable: true }, lock => {
            if (!lock) {
                resolveClaim('refused');
                return undefined;
            }
            resolveClaim('held');
            // Held until releaseSession, or until the page unloads.
            return new Promise(release => held.set(sessionId, release));
        }).catch(() => resolveClaim(claimByAsking(sessionId, fresh)));   // a refused request: ask the other windows instead
    });
}

function claimByAsking(sessionId, fresh) {
    const ch = broadcast();
    if (!ch) return Promise.resolve('unavailable');
    if (fresh) {
        announced.add(sessionId);
        return Promise.resolve('held');
    }
    return new Promise(resolve => {
        if (!batch) {
            const current = batch = { ids: new Set(), waiters: [] };
            setTimeout(() => ask(ch, current), 0);
        }
        batch.ids.add(sessionId);
        batch.waiters.push({ sessionId, resolve });
    });
}

function ask(ch, current) {
    if (batch === current) batch = null;
    const question = { id: `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`, lost: new Set() };
    const elsewhere = new Set();
    const onAnswer = (e) => {
        const m = e.data;
        if (m && m.type === 'held' && m.ask === question.id && Array.isArray(m.ids)) m.ids.forEach(id => elsewhere.add(id));
    };
    for (const id of current.ids) asking.set(id, question);
    ch.addEventListener('message', onAnswer);
    ch.postMessage({ type: 'who-holds', ask: question.id, token: windowToken, ids: [...current.ids] });
    setTimeout(() => {
        ch.removeEventListener('message', onAnswer);
        for (const id of current.ids) if (asking.get(id) === question) asking.delete(id);
        for (const w of current.waiters) {
            if (elsewhere.has(w.sessionId) || question.lost.has(w.sessionId)) {
                w.resolve('refused');
            } else {
                announced.add(w.sessionId);
                w.resolve('held');
            }
        }
    }, ANSWER_WAIT_MS);
}

/** Give up the lease on `sessionId` (its tab was closed). */
export function releaseSession(sessionId) {
    announced.delete(sessionId);
    const release = held.get(sessionId);
    if (!release) return;
    held.delete(sessionId);
    release();
}

/** For tests: the ids this window holds. */
export function heldSessions() {
    return [...held.keys(), ...announced];
}

// ── The sessions this window uses instead of a tab's stored one (sessionStorage: this browser tab, across reloads) ──────

function storage() {
    try { return typeof sessionStorage !== 'undefined' ? sessionStorage : null; } catch { return null; }
}

/** The engine session this window used for query tab `tabId` instead of the stored one, or null. */
export function windowSession(tabId) {
    const s = storage();
    if (!s || !tabId) return null;
    try { return s.getItem(STORAGE_PREFIX + tabId); } catch { return null; }
}

export function rememberWindowSession(tabId, sessionId) {
    const s = storage();
    if (!s || !tabId || !sessionId) return;
    try { s.setItem(STORAGE_PREFIX + tabId, sessionId); } catch { /* blocked or full: the next reload makes a new one */ }
}

export function forgetWindowSession(tabId) {
    const s = storage();
    if (!s || !tabId) return;
    try { s.removeItem(STORAGE_PREFIX + tabId); } catch { /* blocked: nothing to forget */ }
}

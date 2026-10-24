// Spec 021 (web edition) -- M2 foundation. Tiny IndexedDB shim consumed by
// AkmlSql.Web.Services.JsIndexedDbAdapter. Exposes a flat record API
// (get / set / del / list / clear) against a single database with one object-store
// per logical bucket. Records are simple strings (the C# side serialises JSON).
//
// Versioning: the database version bumps when StoreNames.cs adds a new bucket. Adding
// a new store does NOT delete existing data -- IndexedDB upgrade transactions create
// new object-stores in place.

const DB_NAME = 'AkmlSqlWeb';
// Phase 4 (web connection manager) added the 'savedSqlConnections' bucket: 2 -> 3.
// Spec 041 (R36) added the 'workspaceLayout' bucket: 3 -> 4.
// Spec 043 (R8) added the 'editorTabs' bucket (query tabs): 4 -> 5.
export const DB_VERSION = 5;

export const STORES = [
    'profiles',
    'analysisSettings',
    'editorSession',
    'diagnostics',
    'themePreference',
    'schemaEntries',
    'aiKeys',
    // Spec 021 M3 -- browser-side bridge state.
    'connections',
    'pairingTokens',
    // Spec 021 M3/M6 -- non-extractable wrap key handle.
    'keyMaterial',
    // Spec 021 M5 -- snippets persisted browser-side.
    'snippets',
    // Spec 028 M6 -- AI privacy/feature settings + persisted chat (DB_VERSION 1->2).
    'aiFeatureSettings',
    'chatHistory',
    // Phase 4 (web connection manager) -- saved SQL-Server connections (DB_VERSION 2->3).
    // Non-secret metadata only (name/server/database/auth/login) — never a password.
    'savedSqlConnections',
    // Spec 041 (R36) -- the editor workspace layout, one record per field (DB_VERSION 3->4).
    'workspaceLayout',
    // Spec 043 (R8) -- query tabs: one record per tab ('tab:{id}') and the tab set ('set') (DB_VERSION 4->5).
    // 'editorSession' above stays: its single-document record is moved into tab 1 once, on the first load.
    'editorTabs',
];

let _dbPromise = null;
// Set when another tab upgraded the database under this one (N26).
let _reopenWithoutVersion = false;

/** Open (once) the app's database. Exported for the node tests. */
export function openDb() {
    if (_dbPromise) return _dbPromise;
    _dbPromise = new Promise((resolve, reject) => {
        // After another tab upgraded the database, reopen at the version it now has: asking for this
        // tab's lower DB_VERSION would fail with a VersionError (spec 041 N26).
        const req = _reopenWithoutVersion ? indexedDB.open(DB_NAME) : indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = (event) => {
            const db = event.target.result;
            for (const name of STORES) {
                if (!db.objectStoreNames.contains(name)) {
                    db.createObjectStore(name);
                }
            }
        };
        req.onsuccess = () => {
            const db = req.result;
            // If a later version bump is requested while this connection is open in another tab, close
            // so that tab's upgrade isn't blocked (pairs with onblocked below), and forget this
            // connection: the next operation reopens instead of using a closed database.
            db.onversionchange = () => {
                db.close();
                _dbPromise = null;
                _reopenWithoutVersion = true;
            };
            resolve(db);
        };
        req.onerror = () => reject(req.error);
        // Spec 028 (M6): the v1->2 bump can be blocked by another tab still holding the old
        // connection. Without this the promise never settles and every IndexedDB-backed
        // operation hangs with no diagnostic. Surface it instead.
        req.onblocked = () => reject(new Error(
            'AkmlSqlWeb IndexedDB upgrade is blocked by another open AKML SQL tab. ' +
            'Close other tabs of this app and reload.'));
    });
    return _dbPromise;
}

function tx(storeName, mode) {
    return openDb().then((db) => {
        const transaction = db.transaction(storeName, mode);
        return transaction.objectStore(storeName);
    });
}

export async function get(storeName, key) {
    const store = await tx(storeName, 'readonly');
    return await new Promise((resolve, reject) => {
        const req = store.get(key);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error);
    });
}

export async function set(storeName, key, value) {
    const store = await tx(storeName, 'readwrite');
    return await new Promise((resolve, reject) => {
        const req = store.put(value, key);
        req.onsuccess = () => resolve();
        req.onerror = () => reject(req.error);
    });
}

/**
 * Spec 043 (review of US1): insert a record only when its key is free, in one IndexedDB request (`add`), so of two windows
 * writing the same key exactly one wins. True when written; false when the key exists (the transaction is kept, not aborted).
 */
export async function add(storeName, key, value) {
    const store = await tx(storeName, 'readwrite');
    return await new Promise((resolve, reject) => {
        const req = store.add(value, key);
        req.onsuccess = () => resolve(true);
        req.onerror = (e) => {
            if (req.error && req.error.name === 'ConstraintError') {
                e.preventDefault();   // a taken key is an answer, not a failure: keep the transaction
                resolve(false);
            } else {
                reject(req.error);
            }
        };
    });
}

export async function del(storeName, key) {
    const store = await tx(storeName, 'readwrite');
    return await new Promise((resolve, reject) => {
        const req = store.delete(key);
        req.onsuccess = () => resolve();
        req.onerror = () => reject(req.error);
    });
}

export async function list(storeName) {
    const store = await tx(storeName, 'readonly');
    return await new Promise((resolve, reject) => {
        const results = [];
        const cursorReq = store.openCursor();
        cursorReq.onsuccess = (event) => {
            const cursor = event.target.result;
            if (cursor) {
                results.push({ key: String(cursor.key), value: String(cursor.value) });
                cursor.continue();
            } else {
                resolve(results);
            }
        };
        cursorReq.onerror = () => reject(cursorReq.error);
    });
}

export async function clear(storeName) {
    const store = await tx(storeName, 'readwrite');
    return await new Promise((resolve, reject) => {
        const req = store.clear();
        req.onsuccess = () => resolve();
        req.onerror = () => reject(req.error);
    });
}

/**
 * Estimate quota usage. Used by the eviction logic (FR-027 / T110) and the diagnostics
 * page. Returns { usage, quota } in bytes, or nulls when the navigator does not support
 * storage estimation (Safari).
 */
export async function estimate() {
    if (!('storage' in navigator) || typeof navigator.storage.estimate !== 'function') {
        return { usage: null, quota: null };
    }
    const { usage, quota } = await navigator.storage.estimate();
    return { usage: usage ?? null, quota: quota ?? null };
}

/** Node tests only: forget the open connection and the reopen state. */
export function _resetForTests() {
    _dbPromise = null;
    _reopenWithoutVersion = false;
}

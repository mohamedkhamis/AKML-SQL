// Spec 021 (web edition) -- M2 foundation. Tiny IndexedDB shim consumed by
// AkmlSql.Web.Services.JsIndexedDbAdapter. Exposes a flat record API
// (get / set / del / list / clear) against a single database with one object-store
// per logical bucket. Records are simple strings (the C# side serialises JSON).
//
// Versioning: the database version bumps when StoreNames.cs adds a new bucket. Adding
// a new store does NOT delete existing data -- IndexedDB upgrade transactions create
// new object-stores in place.

const DB_NAME = 'AkmlSqlWeb';
const DB_VERSION = 1;

const STORES = [
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
];

let _dbPromise = null;

function openDb() {
    if (_dbPromise) return _dbPromise;
    _dbPromise = new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = (event) => {
            const db = event.target.result;
            for (const name of STORES) {
                if (!db.objectStoreNames.contains(name)) {
                    db.createObjectStore(name);
                }
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
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

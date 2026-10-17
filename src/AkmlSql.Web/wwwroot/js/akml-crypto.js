// Spec 021 (web edition) -- M3 task T069 + M6 T125. Web Crypto wrapper for bearer
// tokens (M3) and AI provider keys (M6). The wrapping key is a single non-extractable
// AES-GCM 256 CryptoKey per browser profile, persisted as a CryptoKey reference inside
// IndexedDB. Raw plaintext never leaves the call-site closures.
//
// Pattern mirrors specs/021-web-edition/contracts/ai-key-wrapping.md.

const DB_NAME = 'AkmlSqlWeb';
const STORE_KEY_MATERIAL = 'keyMaterial';
const PRIMARY_WRAP_KEY = 'primaryWrap';

// The keyMaterial store is created in akml-indexeddb.js's upgrade transaction (it is in its STORES array). This module
// opens its own short-lived connections: each is closed as soon as its transaction completes, and closes itself if another
// tab asks for a version change meanwhile — a connection held open here would block every later upgrade (review WEB-15).
function openDbDirect() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME);   // the version the shim created; never an upgrade from here
        req.onsuccess = () => {
            const db = req.result;
            db.onversionchange = () => db.close();
            resolve(db);
        };
        req.onerror = () => reject(req.error);
    });
}

/** Runs `body(store)` in one transaction on the keyMaterial store and resolves with its result once the transaction has
 * COMPLETED (so a write is durable before anyone is told), then closes the connection. */
async function withKeyStore(mode, body) {
    const db = await openDbDirect();
    try {
        if (!db.objectStoreNames.contains(STORE_KEY_MATERIAL)) {
            if (mode === 'readonly') return null;
            throw new Error('The browser database has no keyMaterial store yet; reload the page and try again.');
        }
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_KEY_MATERIAL, mode);
            let result = null;
            body(tx.objectStore(STORE_KEY_MATERIAL), value => { result = value; });
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('The key transaction was aborted.'));
        });
    } finally {
        db.close();
    }
}

function getStoredWrapKey() {
    return withKeyStore('readonly', (store, done) => {
        const req = store.get(PRIMARY_WRAP_KEY);
        req.onsuccess = () => done(req.result ?? null);
    });
}

/**
 * Stores `candidate` as the wrapping key unless one is already there, in ONE readwrite transaction — IndexedDB runs
 * readwrite transactions on a store one at a time, so of several tabs (or calls) racing to create the key exactly one
 * writes it and every other reads that one back. Resolves with the key that won. An established key is never replaced.
 */
function storeWrapKeyIfAbsent(candidate) {
    return withKeyStore('readwrite', (store, done) => {
        const req = store.get(PRIMARY_WRAP_KEY);
        req.onsuccess = () => {
            if (req.result) { done(req.result); return; }
            store.add(candidate, PRIMARY_WRAP_KEY).onsuccess = () => done(candidate);
        };
    });
}

/** Review WEB-03: Web Crypto exists only in a secure context (HTTPS, or the page's own computer). Without it no secret
 * can be stored, and nothing is ever stored in plain text instead. */
export function isSecureStorageAvailable() {
    return !!(globalThis.isSecureContext !== false && globalThis.crypto && globalThis.crypto.subtle);
}

export const INSECURE_CONTEXT_MESSAGE =
    'Secrets can only be stored when AKML SQL is opened over HTTPS (or on this computer as localhost). ' +
    'Open the HTTPS address of this site, then pair or save the key again.';

let _wrapKeyPromise = null;

/**
 * Load the per-profile wrapping key, or create it. extractable=false guarantees the key material is never observable
 * from JS. Concurrent first calls share one promise (review WEB-04: twelve concurrent wraps used to generate twelve keys
 * and store whichever finished last, so every other record became undecryptable); concurrent tabs settle on the one key
 * the store kept.
 */
function getOrCreateWrapKey() {
    if (!_wrapKeyPromise) {
        _wrapKeyPromise = loadOrCreateWrapKey().catch(err => { _wrapKeyPromise = null; throw err; });
    }
    return _wrapKeyPromise;
}

async function loadOrCreateWrapKey() {
    if (!isSecureStorageAvailable()) throw new Error(INSECURE_CONTEXT_MESSAGE);
    const existing = await getStoredWrapKey();
    if (existing) return existing;
    const candidate = await crypto.subtle.generateKey(
        { name: 'AES-GCM', length: 256 },
        /* extractable */ false,
        ['encrypt', 'decrypt'],
    );
    return await storeWrapKeyIfAbsent(candidate);
}

/** Test seam: forget the cached key, as a fresh page load would. */
export function _resetForTests() {
    _wrapKeyPromise = null;
}

/**
 * Wrap a UTF-8 plaintext with the per-profile wrap key + a per-record IV.
 * Returns an object { ciphertext, iv, aad } -- all Uint8Arrays exposed to .NET as
 * byte[].
 */
export async function wrap(plaintext, aadString) {
    const wrapKey = await getOrCreateWrapKey();
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const aad = new TextEncoder().encode(aadString);
    const pt = new TextEncoder().encode(plaintext);
    const ct = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv, additionalData: aad },
        wrapKey,
        pt,
    );
    return {
        ciphertext: new Uint8Array(ct),
        iv: iv,
        aad: aad,
    };
}

/**
 * Decrypt a previously-wrapped value. Throws DOMException(OperationError) if any of
 * { ciphertext, iv, aad } is tampered with -- AES-GCM's authentication tag fails.
 * Per contract: the plaintext returned MUST be used only within the calling closure
 * and dropped immediately after.
 */
export async function unwrap(ciphertext, iv, aad) {
    const wrapKey = await getOrCreateWrapKey();
    const pt = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: iv, additionalData: aad },
        wrapKey,
        ciphertext,
    );
    return new TextDecoder().decode(pt);
}

/**
 * Overwrite the ciphertext / iv with zeros. Best-effort -- once the bytes are in the
 * GC's hands the engine may have other copies. Pre-deletion zeroing tightens the
 * residual exposure window.
 */
export function zeroise(bytes) {
    if (bytes && bytes.fill) bytes.fill(0);
}

/**
 * Compute SHA-256 of a UTF-8 string and return its hex representation. Used by the
 * TLS-fingerprint check in ConnectionStore (T067).
 */
export async function sha256Hex(text) {
    const buf = new TextEncoder().encode(text);
    const digest = await crypto.subtle.digest('SHA-256', buf);
    return Array.from(new Uint8Array(digest))
        .map(b => b.toString(16).padStart(2, '0'))
        .join('');
}

// Spec 021 (web edition) -- M3 task T069 + M6 T125. Web Crypto wrapper for bearer
// tokens (M3) and AI provider keys (M6). The wrapping key is a single non-extractable
// AES-GCM 256 CryptoKey per browser profile, persisted as a CryptoKey reference inside
// IndexedDB. Raw plaintext never leaves the call-site closures.
//
// Pattern mirrors specs/021-web-edition/contracts/ai-key-wrapping.md.

const DB_NAME = 'AkmlSqlWeb';
const STORE_KEY_MATERIAL = 'keyMaterial';
const PRIMARY_WRAP_KEY = 'primaryWrap';

// We piggy-back on the akml-indexeddb.js shim's database connection -- the keyMaterial
// store is created in its upgrade transaction (we add it to the STORES array there).
function openDbDirect() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME);   // version 1 already created by akml-indexeddb.js
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

async function getStoredWrapKey() {
    const db = await openDbDirect();
    return await new Promise((resolve, reject) => {
        if (!db.objectStoreNames.contains(STORE_KEY_MATERIAL)) {
            resolve(null);
            return;
        }
        const tx = db.transaction(STORE_KEY_MATERIAL, 'readonly');
        const req = tx.objectStore(STORE_KEY_MATERIAL).get(PRIMARY_WRAP_KEY);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error);
    });
}

async function storeWrapKey(cryptoKey) {
    const db = await openDbDirect();
    return await new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_KEY_MATERIAL, 'readwrite');
        const req = tx.objectStore(STORE_KEY_MATERIAL).put(cryptoKey, PRIMARY_WRAP_KEY);
        req.onsuccess = () => resolve();
        req.onerror = () => reject(req.error);
    });
}

let _wrapKeyCache = null;

/**
 * Lazy-create or load the per-profile wrapping key. extractable=false guarantees the
 * key material is never observable from JS.
 */
async function getOrCreateWrapKey() {
    if (_wrapKeyCache) return _wrapKeyCache;
    const existing = await getStoredWrapKey();
    if (existing) {
        _wrapKeyCache = existing;
        return existing;
    }
    const generated = await crypto.subtle.generateKey(
        { name: 'AES-GCM', length: 256 },
        /* extractable */ false,
        ['encrypt', 'decrypt'],
    );
    await storeWrapKey(generated);
    _wrapKeyCache = generated;
    return generated;
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

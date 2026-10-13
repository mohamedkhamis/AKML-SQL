// Spec 021 (web edition) — file download helper.
// Replaces the inline JS.InvokeVoidAsync("eval", "(function(){...arguments[1]...})(...)")
// pattern previously used by Editor.SaveAsync and Diagnostics.ExportBundleAsync.
// `arguments` is undefined inside the strict-mode eval context Blazor uses, so the
// old approach threw a ReferenceError every time.

/**
 * Download a blob built from base64-encoded bytes.
 * @param {string} filename  The suggested filename.
 * @param {string} mimeType  e.g. 'application/sql' or 'application/zip'.
 * @param {string} base64    Base64-encoded payload.
 */
export function downloadBase64(filename, mimeType, base64) {
    const bin = atob(base64);
    const buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
    saveBlob(filename, new Blob([buf], { type: mimeType || 'application/octet-stream' }));
}

/**
 * Spec 041 (R12) — download text the page built (a result set as CSV, the settings export, a .sql
 * document). No Base64 round trip, so a 15 MB result does not become a 20 MB interop payload.
 * @param {string} filename  The suggested filename.
 * @param {string} mimeType  e.g. 'text/csv' or 'application/sql'.
 * @param {string} text      The content.
 * @param {boolean} withBom  Prefix a UTF-8 byte-order mark (Excel reads a CSV as UTF-8 only with one).
 */
export function downloadText(filename, mimeType, text, withBom) {
    const parts = withBom ? ['\uFEFF', text == null ? '' : String(text)] : [text == null ? '' : String(text)];
    saveBlob(filename, new Blob(parts, { type: mimeType || 'text/plain;charset=utf-8' }));
}

function saveBlob(filename, blob) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
}

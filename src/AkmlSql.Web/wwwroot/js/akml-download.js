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
    const blob = new Blob([buf], { type: mimeType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
}

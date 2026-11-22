// BrowserHttpHandler disposes HEAD responses by aborting their fetch controller, which
// Chromium can report as ERR_ABORTED even after the headers arrived. Keep this small
// probe native so success releases the timer without cancelling the completed fetch.
export async function detect(url) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 5000);
    try {
        const response = await fetch(url, {
            method: 'HEAD', cache: 'no-cache',
            headers: { 'Cache-Control': 'no-cache' }, signal: controller.signal
        });
        await response.arrayBuffer();
        return response.headers.get('X-AKML-Host')?.toLowerCase() === 'engine';
    } finally {
        clearTimeout(timer);
    }
}

// Spec 047 (WEB-001): a page IIS serves is static, so HEAD / cannot name its engine. Setup publishes the engine's bridge
// port next to the bundle ({app}\Web\akml-engine.json, written by web-engine-manifest.ps1). The parsed object, or null
// when the file is missing, unreadable or not a JSON object -- never an exception, so a missing file stays "no engine".
export async function readManifest(url) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 5000);
    try {
        const response = await fetch(url, { method: 'GET', cache: 'no-cache', signal: controller.signal });
        if (!response.ok) return null;
        const value = JSON.parse(await response.text());
        return value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null;
    } catch {
        return null;
    } finally {
        clearTimeout(timer);
    }
}

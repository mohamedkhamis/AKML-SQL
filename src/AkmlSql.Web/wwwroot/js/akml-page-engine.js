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

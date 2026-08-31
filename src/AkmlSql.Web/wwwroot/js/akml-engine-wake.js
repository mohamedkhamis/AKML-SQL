// Wake signals for the engine auto-connect loop.
//
// The loop recovers on its own via backoff, but backoff is a compromise: short intervals waste
// requests on a tab nobody is looking at, long ones leave a returning user staring at "Offline"
// for no reason. These two browser events resolve that tension — they fire exactly when the answer
// might have changed and someone is there to care.
//
//   visibilitychange -> the user switched back to the tab. If they went away to start the engine,
//                       this is the moment to try again.
//   online           -> the browser regained connectivity.
//
// Registered as a plain global (not an ES module) so MainLayout can call it without an import
// handshake on first render. Re-registering replaces the previous handler rather than stacking,
// because Blazor may render the layout more than once over a session.

(() => {
    let dotNetRef = null;
    let wired = false;
    let lastWake = 0;

    // A tab can fire visibilitychange several times in a second while a window is being dragged
    // between monitors or a user alt-tabs quickly. One wake per second is plenty and keeps a
    // stopped engine from being hammered.
    const MIN_INTERVAL_MS = 1000;

    function wake(reason) {
        if (!dotNetRef) return;
        const now = Date.now();
        if (now - lastWake < MIN_INTERVAL_MS) return;
        lastWake = now;
        // Fire and forget: a failed invoke must not break the page, and the backoff loop is still
        // running underneath as the safety net.
        dotNetRef.invokeMethodAsync('OnBrowserWake').catch(() => { });
    }

    window.akmlEngineWake = {
        register(ref) {
            dotNetRef = ref;
            if (wired) return;
            wired = true;

            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState === 'visible') wake('visible');
            });
            window.addEventListener('online', () => wake('online'));
            window.addEventListener('focus', () => wake('focus'));
        },
    };
})();

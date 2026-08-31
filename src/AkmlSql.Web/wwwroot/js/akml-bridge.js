// Spec 021 (web edition) -- M3 task T068. WebSocket shim consumed by
// AkmlSql.Web.Services.JsBridgeWebSocket. Exposes connect / send / receive / state
// against one binary-framed WebSocket. Each module instance owns at most one socket.

const _sockets = new Map();   // id -> { ws, queue, waiters }
let _nextId = 1;

function newId() {
    return String(_nextId++);
}

/**
 * Open a new WebSocket. Returns the socket id which the caller stores and uses for
 * subsequent send/receive/state/dispose calls.
 */
export function connect(url) {
    return new Promise((resolve, reject) => {
        const id = newId();
        const ws = new WebSocket(url);
        ws.binaryType = 'arraybuffer';

        const state = {
            ws,
            queue: [],          // queued inbound frames (Uint8Array)
            waiters: [],        // queued recv promises waiting on a frame
            closed: false,
        };
        _sockets.set(id, state);

        // The connect promise MUST settle exactly once, and it must settle on every path.
        //
        // This used to reject only from onerror, and only while readyState was still CONNECTING.
        // That condition is almost never true when it matters: for a refused connection the browser
        // has already advanced readyState to CLOSED by the time onerror fires, so the promise was
        // simply abandoned. The C# side then awaited it forever — the bridge sat on "Connecting…"
        // with no error, no state change and no retry, and the only way out was for the user to
        // reload or click Connect by hand. That is the whole of "it needs a manual connect every
        // time": open the page while the engine is not listening and the app never recovers.
        let settled = false;
        const settle = (fn, arg) => {
            if (settled) return;
            settled = true;
            fn(arg);
        };

        ws.onopen = () => settle(resolve, id);

        ws.onerror = () => {
            _sockets.delete(id);
            settle(reject, new Error(`WebSocket connect failed (${url}).`));
        };
        ws.onmessage = (event) => {
            const frame = new Uint8Array(event.data);
            if (state.waiters.length > 0) {
                const waiter = state.waiters.shift();
                waiter(frame);
            } else {
                state.queue.push(frame);
            }
        };
        ws.onclose = (e) => {
            // A close BEFORE open is a failed connect. Browsers do not guarantee onerror fires
            // first (or at all) for every failure mode, so this is the backstop that makes the
            // promise settle on every path rather than most of them.
            if (!settled) {
                _sockets.delete(id);
                settle(reject, new Error(
                    `WebSocket closed before opening (${url}, code ${e.code}${e.reason ? ': ' + e.reason : ''}).`));
                return;
            }

            state.closed = true;
            // Drain any pending waiters with null (signals "closed").
            while (state.waiters.length > 0) {
                state.waiters.shift()(null);
            }
        };
    });
}

export function send(id, frame) {
    const state = _sockets.get(id);
    if (!state) throw new Error(`Unknown WebSocket id '${id}'.`);
    if (state.ws.readyState !== WebSocket.OPEN) throw new Error('WebSocket is not open.');
    // Convert .NET byte[] (which arrives as Uint8Array on the JS side) to ArrayBuffer
    // before send so the engine sees a clean binary frame.
    const buffer = frame.buffer.slice(frame.byteOffset, frame.byteOffset + frame.byteLength);
    state.ws.send(buffer);
}

export function receive(id) {
    const state = _sockets.get(id);
    if (!state) return Promise.resolve(null);
    if (state.queue.length > 0) {
        return Promise.resolve(state.queue.shift());
    }
    if (state.closed) {
        return Promise.resolve(null);
    }
    return new Promise((resolve) => {
        state.waiters.push(resolve);
    });
}

export function getState(id) {
    const state = _sockets.get(id);
    if (!state) return 3;   // Closed
    return state.ws.readyState;
}

export function close(id) {
    const state = _sockets.get(id);
    if (!state) return;
    state.closed = true;
    try { state.ws.close(); } catch { /* swallow */ }
    while (state.waiters.length > 0) state.waiters.shift()(null);
    _sockets.delete(id);
}

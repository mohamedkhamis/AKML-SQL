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

        ws.onopen = () => resolve(id);
        ws.onerror = (e) => {
            // Reject the connect promise if the socket never opened. Otherwise the
            // receive-side will surface the error via state.closed.
            if (ws.readyState === WebSocket.CONNECTING) {
                _sockets.delete(id);
                reject(new Error('WebSocket connect failed.'));
            }
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
        ws.onclose = () => {
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

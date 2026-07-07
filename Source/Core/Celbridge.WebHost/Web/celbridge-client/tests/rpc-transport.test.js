import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { RpcTransport } from '../core/rpc-transport.js';

const WS_URL = 'ws://127.0.0.1/ws/host?token=abc';

// Minimal controllable WebSocket stand-in. Records every instance the transport creates so a test can
// drive open/close/message on each and assert reconnection behaviour.
class MockWebSocket {
    static CONNECTING = 0;
    static OPEN = 1;
    static CLOSING = 2;
    static CLOSED = 3;

    static instances = [];

    constructor(url) {
        this.url = url;
        this.readyState = MockWebSocket.CONNECTING;
        this.sent = [];
        this.listeners = {};
        MockWebSocket.instances.push(this);
    }

    addEventListener(type, handler) {
        (this.listeners[type] ??= []).push(handler);
    }

    send(message) {
        this.sent.push(message);
    }

    #emit(type, event) {
        for (const handler of this.listeners[type] ?? []) {
            handler(event);
        }
    }

    simulateOpen() {
        this.readyState = MockWebSocket.OPEN;
        this.#emit('open', {});
    }

    simulateMessage(data) {
        this.#emit('message', { data });
    }

    simulateClose() {
        this.readyState = MockWebSocket.CLOSED;
        this.#emit('close', {});
    }
}

// Captures window-level listeners the transport registers (online) so tests can fire them.
function stubWindowListeners() {
    const handlers = {};
    vi.stubGlobal('addEventListener', (type, handler) => {
        (handlers[type] ??= []).push(handler);
    });
    return {
        fire(type, event = {}) {
            for (const handler of handlers[type] ?? []) {
                handler(event);
            }
        }
    };
}

describe('RpcTransport WebSocket reconnection', () => {
    beforeEach(() => {
        MockWebSocket.instances = [];
        vi.stubGlobal('WebSocket', MockWebSocket);
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
    });

    const latestSocket = () => MockWebSocket.instances[MockWebSocket.instances.length - 1];

    it('opens a socket to the given URL and flushes buffered messages on open', () => {
        const transport = new RpcTransport({ wsUrl: WS_URL });
        expect(MockWebSocket.instances).toHaveLength(1);
        expect(latestSocket().url).toBe(WS_URL);

        // Sent before the socket opens: buffered, not delivered yet.
        transport.notify('test/method', { value: 1 });
        expect(latestSocket().sent).toHaveLength(0);

        latestSocket().simulateOpen();
        expect(latestSocket().sent).toHaveLength(1);
    });

    it('reconnects to the same URL after the socket drops', () => {
        new RpcTransport({ wsUrl: WS_URL });
        latestSocket().simulateOpen();
        expect(MockWebSocket.instances).toHaveLength(1);

        // Socket drops (e.g. the machine slept). Nothing reconnects until the backoff elapses.
        latestSocket().simulateClose();
        expect(MockWebSocket.instances).toHaveLength(1);

        vi.advanceTimersByTime(500);
        expect(MockWebSocket.instances).toHaveLength(2);
        expect(latestSocket().url).toBe(WS_URL);
    });

    it('buffers messages sent while disconnected and flushes them after reconnect', () => {
        const transport = new RpcTransport({ wsUrl: WS_URL });
        latestSocket().simulateOpen();
        latestSocket().simulateClose();

        // Host tries to send while the socket is down (e.g. an external-change reload notification).
        transport.notify('editor/externalChange', {});

        vi.advanceTimersByTime(500);
        const reconnected = latestSocket();
        expect(reconnected.sent).toHaveLength(0);

        reconnected.simulateOpen();
        expect(reconnected.sent).toHaveLength(1);
    });

    it('backs off exponentially across repeated failed reconnects', () => {
        new RpcTransport({ wsUrl: WS_URL });
        latestSocket().simulateClose();          // attempt 1 scheduled at 500ms

        vi.advanceTimersByTime(500);
        expect(MockWebSocket.instances).toHaveLength(2);
        latestSocket().simulateClose();          // attempt 2 scheduled at 1000ms

        vi.advanceTimersByTime(999);
        expect(MockWebSocket.instances).toHaveLength(2);
        vi.advanceTimersByTime(1);
        expect(MockWebSocket.instances).toHaveLength(3);
    });

    it('resets the backoff when a connection opens', () => {
        new RpcTransport({ wsUrl: WS_URL });
        latestSocket().simulateClose();          // attempt 1 at 500ms

        vi.advanceTimersByTime(500);
        latestSocket().simulateClose();          // attempt 2 at 1000ms (backoff grew)

        vi.advanceTimersByTime(1000);
        const reconnected = latestSocket();
        reconnected.simulateOpen();              // backoff resets
        reconnected.simulateClose();

        // Next reconnect should be back to the base 500ms, not the grown delay.
        vi.advanceTimersByTime(499);
        const countBefore = MockWebSocket.instances.length;
        vi.advanceTimersByTime(1);
        expect(MockWebSocket.instances.length).toBe(countBefore + 1);
    });

    it('reconnects immediately when the machine comes back online', () => {
        const window = stubWindowListeners();
        new RpcTransport({ wsUrl: WS_URL });
        latestSocket().simulateClose();
        expect(MockWebSocket.instances).toHaveLength(1);

        // 'online' fires before the backoff delay elapses: reconnect at once.
        window.fire('online');
        expect(MockWebSocket.instances).toHaveLength(2);
    });

    it('ignores a stale close from a socket that has already been replaced', () => {
        new RpcTransport({ wsUrl: WS_URL });
        const firstSocket = latestSocket();
        firstSocket.simulateClose();

        vi.advanceTimersByTime(500);
        const secondSocket = latestSocket();
        secondSocket.simulateOpen();
        expect(MockWebSocket.instances).toHaveLength(2);

        // A late close event from the replaced socket must not schedule another reconnect
        // on top of the live connection.
        firstSocket.simulateClose();
        vi.advanceTimersByTime(60000);
        expect(MockWebSocket.instances).toHaveLength(2);
    });
});

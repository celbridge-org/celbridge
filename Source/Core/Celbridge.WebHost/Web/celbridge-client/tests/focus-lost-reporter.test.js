import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import vm from 'node:vm';

const here = path.dirname(fileURLToPath(import.meta.url));
const reporterSource = readFileSync(
    path.join(here, '..', 'core', 'focus-lost-reporter.js'),
    'utf-8'
);

// Runs the reporter in a fresh vm context with a fake window. The window records the listeners the reporter adds,
// the native bridge records what is posted to it, and timers run only when the test runs them.
function runReporter({ bridge = 'chrome', isTop = true, hidden = false, hostTransport = false } = {}) {
    const listeners = {};
    const bridgeMessages = [];
    const transportMessages = [];
    const timers = [];
    const page = { hasFocus: false };

    const window = {
        location: { pathname: '/index.html' },
        addEventListener(type, listener) {
            (listeners[type] ??= []).push(listener);
        }
    };
    window.top = isTop ? window : {};

    const postMessage = (message) => bridgeMessages.push(JSON.parse(message));
    if (bridge === 'chrome') {
        window.chrome = { webview: { postMessage } };
    } else if (bridge === 'webkit') {
        window.webkit = { messageHandlers: { unoWebView: { postMessage } } };
    }

    const document = {
        hidden,
        hasFocus: () => page.hasFocus
    };

    const context = {
        window,
        document,
        JSON,
        setTimeout: (callback) => timers.push(callback)
    };
    if (hostTransport) {
        context.__hostSendMessage = (message) => transportMessages.push(JSON.parse(message));
    }

    vm.createContext(context);
    vm.runInContext(reporterSource, context);

    return {
        page,
        bridgeMessages,
        transportMessages,
        run: () => vm.runInContext(reporterSource, context),
        raise: (type) => (listeners[type] ?? []).forEach((listener) => listener()),
        listenerCount: (type) => (listeners[type] ?? []).length,
        runTimers: () => timers.splice(0).forEach((callback) => callback())
    };
}

function focusLostReports(messages) {
    return messages.filter((message) => message.method === 'input/focusLost');
}

function logEntries(messages) {
    return messages
        .filter((message) => message.method === 'host/log')
        .map((message) => message.params);
}

describe('focus-lost reporter', () => {
    it('reports a loss through chrome.webview once the page no longer holds the keyboard', () => {
        const reporter = runReporter({ bridge: 'chrome' });

        reporter.raise('blur');
        reporter.runTimers();

        expect(focusLostReports(reporter.bridgeMessages)).toEqual([{ jsonrpc: '2.0', method: 'input/focusLost' }]);
    });

    it('reports a loss through the WKWebView message handler where chrome.webview is absent', () => {
        const reporter = runReporter({ bridge: 'webkit' });

        reporter.raise('blur');
        reporter.runTimers();

        expect(focusLostReports(reporter.bridgeMessages)).toHaveLength(1);
    });

    it('decides only after the blur has settled', () => {
        const reporter = runReporter();

        reporter.raise('blur');

        expect(focusLostReports(reporter.bridgeMessages)).toHaveLength(0);
    });

    it('reports nothing when the keyboard moved into a frame of the same page', () => {
        const reporter = runReporter();
        reporter.page.hasFocus = true;

        reporter.raise('blur');
        reporter.runTimers();

        expect(focusLostReports(reporter.bridgeMessages)).toHaveLength(0);
        expect(logEntries(reporter.bridgeMessages).map((entry) => entry.message))
            .toContain('the page blurred but still holds the keyboard (/index.html)');
    });

    it('listens in the top document only', () => {
        const reporter = runReporter({ isTop: false });

        expect(reporter.listenerCount('blur')).toBe(0);
        expect(reporter.listenerCount('focus')).toBe(0);
    });

    it('installs once however many times it runs', () => {
        const reporter = runReporter();

        reporter.run();

        expect(reporter.listenerCount('blur')).toBe(1);
        expect(reporter.listenerCount('focus')).toBe(1);
    });

    it('warns that a visible page without a native bridge could not report its loss', () => {
        const reporter = runReporter({ bridge: 'none', hostTransport: true });

        reporter.raise('blur');
        reporter.runTimers();

        expect(logEntries(reporter.transportMessages)).toContainEqual({
            level: 'warn',
            message: 'focus loss not delivered, no native message bridge (surface visible) (/index.html)'
        });
    });

    it('logs a hidden page without a native bridge at debug level', () => {
        const reporter = runReporter({ bridge: 'none', hostTransport: true, hidden: true });

        reporter.raise('blur');
        reporter.runTimers();

        expect(logEntries(reporter.transportMessages)).toContainEqual({
            level: 'debug',
            message: 'focus loss not delivered, no native message bridge (surface hidden) (/index.html)'
        });
    });

    it('sends its diagnostics through the page transport when the page has one', () => {
        const reporter = runReporter({ hostTransport: true });

        reporter.raise('focus');

        expect(logEntries(reporter.bridgeMessages)).toHaveLength(0);
        expect(logEntries(reporter.transportMessages).map((entry) => entry.message)).toEqual([
            'native message bridge present (/index.html)',
            'the page took the keyboard (focus event 1) (/index.html)'
        ]);
    });

    it('reports the bridge state once and counts each focus', () => {
        const reporter = runReporter();

        reporter.raise('focus');
        reporter.raise('focus');

        expect(logEntries(reporter.bridgeMessages).map((entry) => entry.message)).toEqual([
            'native message bridge present (/index.html)',
            'the page took the keyboard (focus event 1) (/index.html)',
            'the page took the keyboard (focus event 2) (/index.html)'
        ]);
    });
});

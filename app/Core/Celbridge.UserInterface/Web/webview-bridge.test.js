import { describe, it, expect, vi, beforeEach } from 'vitest';
import { WebViewBridge } from './webview-bridge.js';

/**
 * Creates a mock message channel for testing.
 * @returns {{ bridge: WebViewBridge, sentMessages: string[], simulateResponse: Function, simulateNotification: Function }}
 */
function createTestBridge(options = {}) {
    const sentMessages = [];
    let messageHandler = null;

    const bridge = new WebViewBridge({
        postMessage: (msg) => sentMessages.push(msg),
        onMessage: (handler) => { messageHandler = handler; },
        timeout: options.timeout ?? 1000,
        ...options
    });

    const simulateResponse = (id, result) => {
        const response = JSON.stringify({
            jsonrpc: '2.0',
            result,
            id
        });
        messageHandler(response);
    };

    const simulateError = (id, code, message, data) => {
        const response = JSON.stringify({
            jsonrpc: '2.0',
            error: { code, message, data },
            id
        });
        messageHandler(response);
    };

    const simulateNotification = (method, params) => {
        const notification = JSON.stringify({
            jsonrpc: '2.0',
            method,
            params
        });
        messageHandler(notification);
    };

    return { bridge, sentMessages, simulateResponse, simulateError, simulateNotification };
}

describe('WebViewBridge', () => {
    describe('initialization', () => {
        it('should send initialize request with protocol version', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();

            expect(sentMessages).toHaveLength(1);
            const sent = JSON.parse(sentMessages[0]);
            expect(sent.jsonrpc).toBe('2.0');
            expect(sent.method).toBe('bridge/initialize');
            expect(sent.params.protocolVersion).toBe('1.0');
            expect(sent.id).toBe(1);

            simulateResponse(1, {
                content: '# Hello',
                metadata: { filePath: '/test.md', resourceKey: 'test', fileName: 'test.md' },
                localization: { key1: 'value1' },
                theme: { name: 'Dark', isDark: true }
            });

            const result = await initPromise;
            expect(result.content).toBe('# Hello');
            expect(result.metadata.filePath).toBe('/test.md');
        });

        it('should throw if initialized twice', async () => {
            const { bridge, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            await expect(bridge.initialize()).rejects.toThrow('Bridge already initialized');
        });
    });

    describe('request/response correlation', () => {
        it('should correlate responses by id', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            // Initialize first
            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            // Make two concurrent requests
            const promise1 = bridge.document.load();
            const promise2 = bridge.document.getMetadata();

            expect(sentMessages).toHaveLength(3);

            // Respond out of order
            simulateResponse(3, { filePath: '/test.md', resourceKey: 'test', fileName: 'test.md' });
            simulateResponse(2, { content: 'Content here' });

            const result1 = await promise1;
            const result2 = await promise2;

            expect(result1.content).toBe('Content here');
            expect(result2.filePath).toBe('/test.md');
        });

        it('should handle error responses', async () => {
            const { bridge, simulateResponse, simulateError } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = bridge.document.load();
            simulateError(2, -32603, 'File not found', { path: '/missing.md' });

            await expect(loadPromise).rejects.toMatchObject({
                message: 'File not found',
                code: -32603,
                data: { path: '/missing.md' }
            });
        });
    });

    describe('timeout handling', () => {
        it('should timeout requests that do not receive a response', async () => {
            const { bridge, simulateResponse } = createTestBridge({ timeout: 50 });

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = bridge.document.load();

            await expect(loadPromise).rejects.toMatchObject({
                message: expect.stringContaining('timeout')
            });
        });

        it('should not timeout if response arrives in time', async () => {
            const { bridge, simulateResponse } = createTestBridge({ timeout: 500 });

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = bridge.document.load();

            // Respond quickly
            simulateResponse(2, { content: 'Fast response' });

            const result = await loadPromise;
            expect(result.content).toBe('Fast response');
        });
    });

    describe('notifications', () => {
        it('should send document changed notification', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            bridge.document.notifyChanged();

            expect(sentMessages).toHaveLength(2);
            const notification = JSON.parse(sentMessages[1]);
            expect(notification.jsonrpc).toBe('2.0');
            expect(notification.method).toBe('document/changed');
            expect(notification.id).toBeUndefined(); // Notifications have no id
        });

        it('should receive and dispatch incoming notifications', async () => {
            const { bridge, simulateResponse, simulateNotification } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            bridge.document.onExternalChange(handler);

            simulateNotification('document/externalChange', {});

            expect(handler).toHaveBeenCalledOnce();
        });

        it('should handle theme change notifications', async () => {
            const { bridge, simulateResponse, simulateNotification } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            bridge.theme.onChanged(handler);

            simulateNotification('theme/changed', { name: 'Light', isDark: false });

            expect(handler).toHaveBeenCalledWith({ name: 'Light', isDark: false });
        });

        it('should handle localization update notifications', async () => {
            const { bridge, simulateResponse, simulateNotification } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            bridge.localization.onUpdated(handler);

            simulateNotification('localization/updated', { key1: 'value1' });

            expect(handler).toHaveBeenCalledWith({ key1: 'value1' });
        });
    });

    describe('document operations', () => {
        it('should send load request with options', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = bridge.document.load({ includeMetadata: true });
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('document/load');
            expect(sent.params.includeMetadata).toBe(true);

            simulateResponse(2, {
                content: '# Test',
                metadata: { filePath: '/test.md', resourceKey: 'test', fileName: 'test.md' }
            });

            const result = await loadPromise;
            expect(result.content).toBe('# Test');
            expect(result.metadata.filePath).toBe('/test.md');
        });

        it('should send save request with content', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const savePromise = bridge.document.save('# New content');
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('document/save');
            expect(sent.params.content).toBe('# New content');

            simulateResponse(2, { success: true });

            const result = await savePromise;
            expect(result.success).toBe(true);
        });
    });

    describe('dialog operations', () => {
        it('should send pickImage request and return path', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const pickPromise = bridge.dialog.pickImage(['.png', '.jpg']);
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('dialog/pickImage');
            expect(sent.params.extensions).toEqual(['.png', '.jpg']);

            simulateResponse(2, { path: '/images/photo.png' });

            const result = await pickPromise;
            expect(result).toBe('/images/photo.png');
        });

        it('should return null when dialog is cancelled', async () => {
            const { bridge, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const pickPromise = bridge.dialog.pickFile(['.txt']);
            simulateResponse(2, { path: null });

            const result = await pickPromise;
            expect(result).toBeNull();
        });

        it('should send alert request', async () => {
            const { bridge, sentMessages, simulateResponse } = createTestBridge();

            const initPromise = bridge.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const alertPromise = bridge.dialog.alert('Title', 'Message');
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('dialog/alert');
            expect(sent.params.title).toBe('Title');
            expect(sent.params.message).toBe('Message');

            simulateResponse(2, {});

            await alertPromise; // Should resolve without error
        });
    });

    describe('logging', () => {
        it('should not throw when setting log level', () => {
            const { bridge } = createTestBridge();
            expect(() => bridge.setLogLevel('debug')).not.toThrow();
            expect(() => bridge.setLogLevel('warn')).not.toThrow();
            expect(() => bridge.setLogLevel('error')).not.toThrow();
            expect(() => bridge.setLogLevel('none')).not.toThrow();
        });
    });

    describe('edge cases', () => {
        it('should handle response with no matching request', async () => {
            const { bridge, simulateResponse } = createTestBridge();

            // This should not throw, just log a warning
            expect(() => simulateResponse(999, { content: 'orphan' })).not.toThrow();
        });

        it('should handle malformed JSON gracefully', async () => {
            const sentMessages = [];
            let messageHandler = null;

            const bridge = new WebViewBridge({
                postMessage: (msg) => sentMessages.push(msg),
                onMessage: (handler) => { messageHandler = handler; }
            });

            // Should not throw
            expect(() => messageHandler('{ invalid json')).not.toThrow();
        });
    });
});

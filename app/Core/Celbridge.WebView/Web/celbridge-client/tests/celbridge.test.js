import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Celbridge } from './celbridge.js';

/**
 * Creates a mock message channel for testing.
 * @returns {{ client: Celbridge, sentMessages: string[], simulateResponse: Function, simulateNotification: Function }}
 */
function createTestClient(options = {}) {
    const sentMessages = [];
    let messageHandler = null;

    const client = new Celbridge({
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

    return { client, sentMessages, simulateResponse, simulateError, simulateNotification };
}

describe('Celbridge', () => {
    describe('initialization', () => {
        it('should send initialize request with protocol version', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();

            expect(sentMessages).toHaveLength(1);
            const sent = JSON.parse(sentMessages[0]);
            expect(sent.jsonrpc).toBe('2.0');
            expect(sent.method).toBe('document/initialize');
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
            const { client, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            await expect(client.initialize()).rejects.toThrow('Client already initialized');
        });
    });

    describe('request/response correlation', () => {
        it('should correlate responses by id', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            // Initialize first
            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            // Make two concurrent requests
            const promise1 = client.document.load();
            const promise2 = client.document.save('test content');

            expect(sentMessages).toHaveLength(3);

            // Respond out of order
            simulateResponse(3, { success: true });
            simulateResponse(2, { content: 'Content here', metadata: {} });

            const result1 = await promise1;
            const result2 = await promise2;

            expect(result1.content).toBe('Content here');
            expect(result2.success).toBe(true);
        });

        it('should handle error responses', async () => {
            const { client, simulateResponse, simulateError } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = client.document.load();
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
            const { client, simulateResponse } = createTestClient({ timeout: 50 });

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = client.document.load();

            await expect(loadPromise).rejects.toMatchObject({
                message: expect.stringContaining('timeout')
            });
        });

        it('should not timeout if response arrives in time', async () => {
            const { client, simulateResponse } = createTestClient({ timeout: 500 });

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const loadPromise = client.document.load();

            // Respond quickly
            simulateResponse(2, { content: 'Fast response' });

            const result = await loadPromise;
            expect(result.content).toBe('Fast response');
        });
    });

    describe('notifications', () => {
        it('should send document changed notification', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.document.notifyChanged();

            expect(sentMessages).toHaveLength(2);
            const notification = JSON.parse(sentMessages[1]);
            expect(notification.jsonrpc).toBe('2.0');
            expect(notification.method).toBe('document/changed');
            expect(notification.id).toBeUndefined(); // Notifications have no id
        });

        it('should receive and dispatch incoming notifications', async () => {
            const { client, simulateResponse, simulateNotification } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            client.document.onExternalChange(handler);

            simulateNotification('document/externalChange', {});

            expect(handler).toHaveBeenCalledOnce();
        });

        it('should handle language change notifications', async () => {
            const { client, simulateResponse, simulateNotification } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: { locale: 'en' } });
            await initPromise;

                        const handler = vi.fn();
                        client.localization.onLanguageChanged(handler);

                        simulateNotification('localization/languageChanged', 'fr');

                        expect(handler).toHaveBeenCalledWith('fr');
                    });
                });

                describe('document operations', () => {
                    it('should send load request', async () => {
                        const { client, sentMessages, simulateResponse } = createTestClient();

                        const initPromise = client.initialize();
                        simulateResponse(1, { content: '', metadata: { locale: 'en' } });
                        await initPromise;

                        const loadPromise = client.document.load();
                        const sent = JSON.parse(sentMessages[1]);
                        expect(sent.method).toBe('document/load');

            simulateResponse(2, {
                content: '# Test',
                metadata: { filePath: '/test.md', resourceKey: 'test', fileName: 'test.md' }
            });

            const result = await loadPromise;
            expect(result.content).toBe('# Test');
            expect(result.metadata.filePath).toBe('/test.md');
        });

        it('should send save request with content', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const savePromise = client.document.save('# New content');
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
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const pickPromise = client.dialog.pickImage(['.png', '.jpg']);
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('dialog/pickImage');
            expect(sent.params.extensions).toEqual(['.png', '.jpg']);

            simulateResponse(2, { path: '/images/photo.png' });

            const result = await pickPromise;
            expect(result).toBe('/images/photo.png');
        });

        it('should return path when file is selected', async () => {
            const { client, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const pickPromise = client.dialog.pickFile(['.txt']);
            simulateResponse(2, { path: '/documents/notes.txt' });

            const result = await pickPromise;
            expect(result).toBe('/documents/notes.txt');
        });

        it('should return null when dialog is cancelled', async () => {
            const { client, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const pickPromise = client.dialog.pickFile(['.txt']);
            simulateResponse(2, { path: null });

            const result = await pickPromise;
            expect(result).toBeNull();
        });

        it('should send alert request', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const alertPromise = client.dialog.alert('Title', 'Message');
            const sent = JSON.parse(sentMessages[1]);
            expect(sent.method).toBe('dialog/alert');
            expect(sent.params.title).toBe('Title');
            expect(sent.params.message).toBe('Message');

            simulateResponse(2, {});

            await alertPromise; // Should resolve without error
        });
    });

    describe('input operations', () => {
        it('should send link clicked notification with href', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.input.notifyLinkClicked('/docs/intro.md');

            expect(sentMessages).toHaveLength(2);
            const notification = JSON.parse(sentMessages[1]);
            expect(notification.jsonrpc).toBe('2.0');
            expect(notification.method).toBe('input/linkClicked');
            expect(notification.params.href).toBe('/docs/intro.md');
            expect(notification.id).toBeUndefined();
        });

        it('should send scroll changed notification with percentage', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.input.notifyScrollChanged(0.75);

            expect(sentMessages).toHaveLength(2);
            const notification = JSON.parse(sentMessages[1]);
            expect(notification.method).toBe('input/scrollChanged');
            expect(notification.params.scrollPercentage).toBe(0.75);
            expect(notification.id).toBeUndefined();
        });
    });

    describe('code preview operations', () => {
        it('should start with empty basePath', () => {
            const { client } = createTestClient();
            expect(client.codePreview.basePath).toBe('');
        });

        it('should update basePath and call handler on setContext notification', async () => {
            const { client, simulateResponse, simulateNotification } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            client.codePreview.onSetContext(handler);

            simulateNotification('codePreview/setContext', { basePath: '/projects/myproject' });

            expect(client.codePreview.basePath).toBe('/projects/myproject');
            expect(handler).toHaveBeenCalledWith('/projects/myproject');
        });

        it('should call handler on update notification with content', async () => {
            const { client, simulateResponse, simulateNotification } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            client.codePreview.onUpdate(handler);

            simulateNotification('codePreview/update', { content: '<h1>Hello</h1>' });

            expect(handler).toHaveBeenCalledWith('<h1>Hello</h1>');
        });

        it('should call handler on scroll notification with percentage', async () => {
            const { client, simulateResponse, simulateNotification } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            const handler = vi.fn();
            client.codePreview.onScroll(handler);

            simulateNotification('codePreview/scroll', { scrollPercentage: 0.5 });

            expect(handler).toHaveBeenCalledWith(0.5);
        });

        it('should send openResource notification with href', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.codePreview.openResource('docs/intro.md');

            expect(sentMessages).toHaveLength(2);
            const notification = JSON.parse(sentMessages[1]);
            expect(notification.method).toBe('codePreview/openResource');
            expect(notification.params.href).toBe('docs/intro.md');
            expect(notification.id).toBeUndefined();
        });

        it('should send openExternal notification with href', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.codePreview.openExternal('https://example.com');

            const notification = JSON.parse(sentMessages[1]);
            expect(notification.method).toBe('codePreview/openExternal');
            expect(notification.params.href).toBe('https://example.com');
            expect(notification.id).toBeUndefined();
        });

        it('should send syncToEditor notification with scroll percentage', async () => {
            const { client, sentMessages, simulateResponse } = createTestClient();

            const initPromise = client.initialize();
            simulateResponse(1, { content: '', metadata: {}, localization: {}, theme: {} });
            await initPromise;

            client.codePreview.syncToEditor(0.25);

            const notification = JSON.parse(sentMessages[1]);
            expect(notification.method).toBe('codePreview/syncToEditor');
            expect(notification.params.scrollPercentage).toBe(0.25);
            expect(notification.id).toBeUndefined();
        });
    });

    describe('logging', () => {
        it('should not throw when setting log level', () => {
            const { client } = createTestClient();
            expect(() => client.setLogLevel('debug')).not.toThrow();
            expect(() => client.setLogLevel('warn')).not.toThrow();
            expect(() => client.setLogLevel('error')).not.toThrow();
            expect(() => client.setLogLevel('none')).not.toThrow();
        });
    });

    describe('edge cases', () => {
        it('should handle response with no matching request', async () => {
            const { client, simulateResponse } = createTestClient();

            // This should not throw, just log a warning
            expect(() => simulateResponse(999, { content: 'orphan' })).not.toThrow();
        });

        it('should handle malformed JSON gracefully', async () => {
            const sentMessages = [];
            let messageHandler = null;

            const client = new Celbridge({
                postMessage: (msg) => sentMessages.push(msg),
                onMessage: (handler) => { messageHandler = handler; }
            });

            // Should not throw
            expect(() => messageHandler('{ invalid json')).not.toThrow();
        });
    });
});

import { describe, it, expect } from 'vitest';
import { Celbridge } from './celbridge.js';
import {
    CelToolError,
    CelToolErrorCode,
    ToolsAPI,
    buildCelProxy,
    isToolAllowed,
    matchesToolPattern,
    jsonRpcCodeForCelCode
} from './api/tools-api.js';

/**
 * @param {Object} [options]
 * @returns {{ client: Celbridge, sentMessages: string[], simulateResponse: Function, simulateError: Function }}
 */
function createTestClient(options = {}) {
    const sentMessages = [];
    let messageHandler = null;

    const client = new Celbridge({
        postMessage: (msg) => sentMessages.push(msg),
        onMessage: (handler) => { messageHandler = handler; },
        timeout: options.timeout ?? 1000,
        context: options.context
    });

    const simulateResponse = (id, result) => {
        messageHandler(JSON.stringify({ jsonrpc: '2.0', id, result }));
    };

    const simulateError = (id, code, message, data) => {
        messageHandler(JSON.stringify({
            jsonrpc: '2.0',
            id,
            error: { code, message, data }
        }));
    };

    return { client, sentMessages, simulateResponse, simulateError };
}

describe('matchesToolPattern', () => {
    it('matches literal alias exactly', () => {
        expect(matchesToolPattern('app.get_version', 'app.get_version')).toBe(true);
        expect(matchesToolPattern('app.get_version', 'app.version')).toBe(false);
    });

    it('matches namespace wildcards', () => {
        expect(matchesToolPattern('app.get_version', 'app.*')).toBe(true);
        expect(matchesToolPattern('document.open', 'app.*')).toBe(false);
        expect(matchesToolPattern('app.get_version', 'document.*')).toBe(false);
    });

    it('star matches all tools', () => {
        expect(matchesToolPattern('anything.goes', '*')).toBe(true);
    });

    it('does not treat prefix as wildcard without .*', () => {
        // "app" is not a valid pattern for "app.get_version"; must be "app.*".
        expect(matchesToolPattern('app.get_version', 'app')).toBe(false);
    });
});

describe('isToolAllowed', () => {
    it('returns false for empty allowlist', () => {
        expect(isToolAllowed('app.get_version', [])).toBe(false);
        expect(isToolAllowed('app.get_version', null)).toBe(false);
    });

    it('matches any pattern in the list', () => {
        const allowed = ['document.*', 'app.get_version'];
        expect(isToolAllowed('document.open', allowed)).toBe(true);
        expect(isToolAllowed('app.get_version', allowed)).toBe(true);
        expect(isToolAllowed('file.read', allowed)).toBe(false);
    });
});

describe('buildCelProxy', () => {
    it('builds nested namespaces from dotted aliases', () => {
        const calls = [];
        const invoke = (alias, args) => {
            calls.push({ alias, args });
            return Promise.resolve('ok');
        };
        const proxy = buildCelProxy(['app.get_version', 'document.open'], invoke);

        expect(typeof proxy.app.get_version).toBe('function');
        expect(typeof proxy.document.open).toBe('function');

        return proxy.app.get_version({ foo: 1 }).then(() => {
            expect(calls[0]).toEqual({ alias: 'app.get_version', args: { foo: 1 } });
        });
    });

    it('ignores empty or non-string aliases', () => {
        const proxy = buildCelProxy(['', null, undefined, 'valid.tool'], () => Promise.resolve());
        expect(typeof proxy.valid.tool).toBe('function');
    });
});

describe('ToolsAPI', () => {
    it('exposes allowedPatterns as a readonly copy', () => {
        const patterns = ['app.*', 'document.open'];
        const api = new ToolsAPI(/* transport */ { request: () => Promise.resolve([]) }, patterns);
        expect(api.allowedPatterns).toEqual(patterns);
    });

    it('call() rejects tools not in the allowlist with CEL_TOOL_DENIED', async () => {
        const api = new ToolsAPI({ request: () => Promise.resolve({}) }, ['app.*']);
        await expect(api.call('file.read', {})).rejects.toMatchObject({
            code: CelToolErrorCode.Denied,
            tool: 'file.read'
        });
    });

    it('call() dispatches allowed tools via tools/call and returns value', async () => {
        const calls = [];
        const transport = {
            request: (method, params) => {
                calls.push({ method, params });
                return Promise.resolve({ isSuccess: true, value: '0.2.5' });
            }
        };
        const api = new ToolsAPI(transport, ['app.*']);

        const value = await api.call('app.get_version', { foo: 1 });

        expect(value).toBe('0.2.5');
        expect(calls[0].method).toBe('tools/call');
        expect(calls[0].params).toEqual({ name: 'app.get_version', arguments: { foo: 1 } });
    });

    it('call() surfaces tool failures as CEL_TOOL_FAILED', async () => {
        const transport = {
            request: () => Promise.resolve({
                isSuccess: false,
                errorMessage: 'missing arg'
            })
        };
        const api = new ToolsAPI(transport, ['*']);

        await expect(api.call('document.open', {})).rejects.toMatchObject({
            code: CelToolErrorCode.Failed,
            message: 'missing arg'
        });
    });

    it('call() maps JSON-RPC error codes to cel error codes', async () => {
        const transport = {
            request: () => {
                const err = new Error('not found');
                err.code = jsonRpcCodeForCelCode(CelToolErrorCode.NotFound);
                return Promise.reject(err);
            }
        };
        const api = new ToolsAPI(transport, ['*']);

        await expect(api.call('missing.tool', {})).rejects.toMatchObject({
            code: CelToolErrorCode.NotFound
        });
    });

    it('cel proxy lazily exposes only literal aliases', () => {
        const api = new ToolsAPI({ request: () => Promise.resolve([]) }, ['app.get_version', 'document.*']);
        const cel = api.cel;
        expect(typeof cel.app.get_version).toBe('function');
        // Wildcard entries don't populate the proxy until refreshProxy() runs.
        expect(cel.document).toBeUndefined();
    });

    it('refreshProxy() expands wildcards from tools/list response', async () => {
        const transport = {
            request: () => Promise.resolve([
                { name: 'doc_open', alias: 'document.open' },
                { name: 'doc_save', alias: 'document.save' },
                { name: 'file_read', alias: 'file.read' }
            ])
        };
        const api = new ToolsAPI(transport, ['document.*']);

        await api.refreshProxy();

        expect(typeof api.cel.document.open).toBe('function');
        expect(typeof api.cel.document.save).toBe('function');
        expect(api.cel.file).toBeUndefined(); // filtered out by allowlist
    });
});

describe('Celbridge.tools integration', () => {
    it('defaults to an empty allowlist when no context is provided', () => {
        const { client } = createTestClient();
        expect(client.tools.allowedPatterns).toEqual([]);
        expect(client.secrets).toEqual({});
    });

    it('reads allowedTools from constructor context', () => {
        const { client } = createTestClient({
            context: { allowedTools: ['app.get_version'], secrets: {} }
        });
        expect(client.tools.allowedPatterns).toEqual(['app.get_version']);
    });

    it('reads and exposes secrets', () => {
        const secrets = { spreadjs_license: 'abc123' };
        const { client } = createTestClient({
            context: { allowedTools: [], secrets }
        });
        expect(client.secrets.spreadjs_license).toBe('abc123');
    });

    it('cel proxy dispatches via tools/call', async () => {
        const { client, sentMessages, simulateResponse } = createTestClient({
            context: { allowedTools: ['app.get_version'], secrets: {} }
        });

        const promise = client.cel.app.get_version({});

        const sent = JSON.parse(sentMessages[0]);
        expect(sent.method).toBe('tools/call');
        expect(sent.params.name).toBe('app.get_version');

        simulateResponse(sent.id, { isSuccess: true, value: '0.2.5' });

        await expect(promise).resolves.toBe('0.2.5');
    });

    it('denied tool calls reject without hitting the transport', async () => {
        const { client, sentMessages } = createTestClient({
            context: { allowedTools: ['app.*'], secrets: {} }
        });

        await expect(client.tools.call('file.read', {})).rejects.toMatchObject({
            name: 'CelToolError',
            code: CelToolErrorCode.Denied,
            tool: 'file.read'
        });
        expect(sentMessages).toHaveLength(0);
    });
});

describe('Celbridge global context scrubbing', () => {
    it('reads globalThis.__celbridgeContext and deletes it', () => {
        globalThis.__celbridgeContext = {
            allowedTools: ['app.*'],
            secrets: { key: 'value' }
        };

        const client = new Celbridge({
            postMessage: () => {},
            onMessage: () => {}
        });

        expect(client.tools.allowedPatterns).toEqual(['app.*']);
        expect(client.secrets.key).toBe('value');
        expect(globalThis.__celbridgeContext).toBeUndefined();
    });
});

describe('InputAPI.notifyShortcut', () => {
    it('sends input/keyboardShortcut notification with modifier flags', () => {
        const { client, sentMessages } = createTestClient();

        client.input.notifyShortcut('S', { ctrl: true, shift: false, alt: false });

        const sent = JSON.parse(sentMessages[0]);
        expect(sent.jsonrpc).toBe('2.0');
        expect(sent.method).toBe('input/keyboardShortcut');
        expect(sent.params).toEqual({
            key: 'S',
            ctrlKey: true,
            shiftKey: false,
            altKey: false
        });
        expect(sent.id).toBeUndefined();
    });

    it('defaults modifier flags to false when omitted', () => {
        const { client, sentMessages } = createTestClient();

        client.input.notifyShortcut('Escape');

        const sent = JSON.parse(sentMessages[0]);
        expect(sent.params).toEqual({
            key: 'Escape',
            ctrlKey: false,
            shiftKey: false,
            altKey: false
        });
    });
});

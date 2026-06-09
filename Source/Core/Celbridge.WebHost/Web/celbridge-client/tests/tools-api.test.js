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

/**
 * Convenience: build a descriptor with the given alias and parameter list.
 * @param {string} alias
 * @param {Array<{name: string, type?: string}>} [parameters]
 * @returns {import('./api/tools-api.js').ToolDescriptor}
 */
function descriptor(alias, parameters = []) {
    return { name: alias.replace(/\./g, '_'), alias, description: '', parameters };
}

describe('matchesToolPattern', () => {
    it('matches literal alias exactly', () => {
        expect(matchesToolPattern('app.get_state', 'app.get_state')).toBe(true);
        expect(matchesToolPattern('app.get_state', 'app.version')).toBe(false);
    });

    it('matches namespace wildcards', () => {
        expect(matchesToolPattern('app.get_state', 'app.*')).toBe(true);
        expect(matchesToolPattern('document.open', 'app.*')).toBe(false);
        expect(matchesToolPattern('app.get_state', 'document.*')).toBe(false);
    });

    it('star matches all tools', () => {
        expect(matchesToolPattern('anything.goes', '*')).toBe(true);
    });

    it('does not treat prefix as wildcard without .*', () => {
        // "app" is not a valid pattern for "app.get_state"; must be "app.*".
        expect(matchesToolPattern('app.get_state', 'app')).toBe(false);
    });
});

describe('isToolAllowed', () => {
    it('returns false for empty allowlist', () => {
        expect(isToolAllowed('app.get_state', [])).toBe(false);
        expect(isToolAllowed('app.get_state', null)).toBe(false);
    });

    it('matches any pattern in the list', () => {
        const allowed = ['document.*', 'app.get_state'];
        expect(isToolAllowed('document.open', allowed)).toBe(true);
        expect(isToolAllowed('app.get_state', allowed)).toBe(true);
        expect(isToolAllowed('file.read', allowed)).toBe(false);
    });
});

describe('buildCelProxy', () => {
    it('builds nested namespaces from descriptor aliases with camelCase leaf names', () => {
        const calls = [];
        const invoke = (alias, args) => {
            calls.push({ alias, args });
            return Promise.resolve('ok');
        };
        const proxy = buildCelProxy(
            [
                descriptor('app.get_state'),
                descriptor('document.open', [{ name: 'fileResource', type: 'string' }])
            ],
            invoke
        );

        expect(typeof proxy.app.getState).toBe('function');
        expect(typeof proxy.document.open).toBe('function');

        return proxy.document.open('readme.md').then(() => {
            expect(calls[0]).toEqual({ alias: 'document.open', args: { fileResource: 'readme.md' } });
        });
    });

    it('maps positional arguments to parameter names in declaration order', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('file.replace', [
                { name: 'fileResource', type: 'string' },
                { name: 'searchText', type: 'string' },
                { name: 'matchCase', type: 'boolean' }
            ])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        return proxy.file.replace('a.md', 'foo', false).then(() => {
            expect(calls[0].args).toEqual({
                fileResource: 'a.md',
                searchText: 'foo',
                matchCase: false
            });
        });
    });

    it('throws CelToolError(InvalidArgs) on arity overflow', () => {
        const proxy = buildCelProxy(
            [descriptor('app.get_state', [])],
            () => Promise.resolve('ok')
        );

        expect(() => proxy.app.getState('extra')).toThrow(CelToolError);
        try {
            proxy.app.getState('extra');
        } catch (error) {
            expect(error.code).toBe(CelToolErrorCode.InvalidArgs);
            expect(error.message).toContain('cel.app.get_state');
            expect(error.message).toContain('1 positional arguments');
        }
    });

    it('JSON-stringifies arrays passed to string-typed parameters', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('file.multi_edit', [
                { name: 'fileResource', type: 'string' },
                { name: 'editsJson', type: 'string' }
            ])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        const edits = [{ oldString: 'a', newString: 'A' }];
        return proxy.file.multiEdit('a.md', edits).then(() => {
            expect(calls[0].args.editsJson).toBe(JSON.stringify(edits));
        });
    });

    it('JSON-stringifies plain objects passed to string-typed parameters', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('app.custom', [{ name: 'payload', type: 'string' }])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        return proxy.app.custom({ a: 1 }).then(() => {
            expect(calls[0].args).toEqual({ payload: '{"a":1}' });
        });
    });

    it('does not stringify arguments for non-string parameters', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('document.open', [
                { name: 'fileResource', type: 'string' },
                { name: 'sectionIndex', type: 'integer' }
            ])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        return proxy.document.open('a.md', 2).then(() => {
            expect(calls[0].args).toEqual({ fileResource: 'a.md', sectionIndex: 2 });
        });
    });

    it('ignores non-descriptor entries', () => {
        const proxy = buildCelProxy(
            [null, undefined, {}, { alias: '' }, descriptor('valid.tool')],
            () => Promise.resolve()
        );
        expect(typeof proxy.valid.tool).toBe('function');
    });

    it('rejects a boolean-typed parameter receiving a string (positional-arg swap)', () => {
        const proxy = buildCelProxy(
            [descriptor('explorer.delete', [
                { name: 'resource', type: 'string' },
                { name: 'showDialog', type: 'boolean' },
                { name: 'referencePolicy', type: 'string' }
            ])],
            () => Promise.resolve('ok')
        );

        expect(() => proxy.explorer.delete('foo.xlsx', 'break_references', false)).toThrow(CelToolError);
        try {
            proxy.explorer.delete('foo.xlsx', 'break_references', false);
        } catch (error) {
            expect(error.code).toBe(CelToolErrorCode.InvalidArgs);
            expect(error.message).toContain("'showDialog'");
            expect(error.message).toContain('expects boolean');
            expect(error.message).toContain('got string');
        }
    });

    it('rejects a string-typed parameter receiving a number', () => {
        const proxy = buildCelProxy(
            [descriptor('document.open', [{ name: 'fileResource', type: 'string' }])],
            () => Promise.resolve('ok')
        );

        expect(() => proxy.document.open(42)).toThrow(CelToolError);
    });

    it('rejects an integer-typed parameter receiving a non-integer number', () => {
        const proxy = buildCelProxy(
            [descriptor('document.open', [
                { name: 'fileResource', type: 'string' },
                { name: 'sectionIndex', type: 'integer' }
            ])],
            () => Promise.resolve('ok')
        );

        expect(() => proxy.document.open('a.md', 1.5)).toThrow(CelToolError);
    });

    it('accepts undefined and null in defaulted positional slots', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('explorer.delete', [
                { name: 'resource', type: 'string' },
                { name: 'showDialog', type: 'boolean' },
                { name: 'referencePolicy', type: 'string' }
            ])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        return Promise.all([
            proxy.explorer.delete('foo.xlsx', undefined, 'break_references'),
            proxy.explorer.delete('foo.xlsx', null, 'break_references')
        ]).then(() => {
            expect(calls[0].args).toEqual({ resource: 'foo.xlsx', showDialog: undefined, referencePolicy: 'break_references' });
            expect(calls[1].args).toEqual({ resource: 'foo.xlsx', showDialog: null, referencePolicy: 'break_references' });
        });
    });

    it('skips validation when the descriptor reports no parameter type', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('legacy.tool', [{ name: 'payload' }])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        return proxy.legacy.tool(42).then(() => {
            expect(calls[0].args).toEqual({ payload: 42 });
        });
    });

    it('runs auto-stringify before type validation (arrays for string params still accepted)', () => {
        const calls = [];
        const proxy = buildCelProxy(
            [descriptor('file.multi_edit', [
                { name: 'fileResource', type: 'string' },
                { name: 'editsJson', type: 'string' }
            ])],
            (alias, args) => { calls.push({ alias, args }); return Promise.resolve('ok'); }
        );

        const edits = [{ oldString: 'a', newString: 'A' }];
        return proxy.file.multiEdit('a.md', edits).then(() => {
            expect(calls[0].args.editsJson).toBe(JSON.stringify(edits));
        });
    });
});

describe('ToolsAPI', () => {
    it('exposes allowedPatterns as a readonly copy', () => {
        const patterns = ['app.*', 'document.open'];
        const api = new ToolsAPI({ request: () => Promise.resolve([]) }, patterns);
        expect(api.allowedPatterns).toEqual(patterns);
    });

    it('cel proxy throws synchronously before descriptors are loaded', () => {
        const api = new ToolsAPI({ request: () => Promise.resolve([]) }, ['*']);
        expect(api.isReady).toBe(false);
        expect(() => api.cel).toThrow(CelToolError);
        try {
            void api.cel;
        } catch (error) {
            expect(error.code).toBe(CelToolErrorCode.Failed);
            expect(error.message).toContain('not initialized');
        }
    });

    it('accepts initialDescriptors via constructor to skip fetch', () => {
        const api = new ToolsAPI(
            { request: () => Promise.reject(new Error('should not fetch')) },
            ['*'],
            [descriptor('app.get_state')]
        );
        expect(api.isReady).toBe(true);
        expect(typeof api.cel.app.getState).toBe('function');
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

        const value = await api.call('app.get_state', { foo: 1 });

        expect(value).toBe('0.2.5');
        expect(calls[0].method).toBe('tools/call');
        expect(calls[0].params).toEqual({ name: 'app.get_state', arguments: { foo: 1 } });
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

    it('loadDescriptors() fetches tools/list and filters by the allowlist', async () => {
        const transport = {
            request: (method) => {
                expect(method).toBe('tools/list');
                return Promise.resolve([
                    descriptor('document.open', [{ name: 'fileResource', type: 'string' }]),
                    descriptor('document.save'),
                    descriptor('file.read', [{ name: 'fileResource', type: 'string' }])
                ]);
            }
        };
        const api = new ToolsAPI(transport, ['document.*']);

        await api.loadDescriptors();

        expect(api.isReady).toBe(true);
        expect(typeof api.cel.document.open).toBe('function');
        expect(typeof api.cel.document.save).toBe('function');
        expect(api.cel.file).toBeUndefined(); // filtered out by allowlist
    });

    it('loadDescriptors() tolerates transport failures with an empty descriptor list', async () => {
        const transport = { request: () => Promise.reject(new Error('no tools/list')) };
        const api = new ToolsAPI(transport, ['*']);

        await api.loadDescriptors();

        expect(api.isReady).toBe(true);
        expect(api.list()).toEqual([]);
    });

    it('list() throws before descriptors are loaded', () => {
        const api = new ToolsAPI({ request: () => Promise.resolve([]) }, ['*']);
        expect(() => api.list()).toThrow(CelToolError);
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
            context: { allowedTools: ['app.get_state'], secrets: {} }
        });
        expect(client.tools.allowedPatterns).toEqual(['app.get_state']);
    });

    it('reads and exposes secrets', () => {
        const secrets = { spreadjs_license: 'abc123' };
        const { client } = createTestClient({
            context: { allowedTools: [], secrets }
        });
        expect(client.secrets.spreadjs_license).toBe('abc123');
    });

    it('cel accessor throws before initialize() completes', () => {
        const { client } = createTestClient({
            context: { allowedTools: ['*'], secrets: {} }
        });
        expect(() => client.cel).toThrow(/not initialized/);
    });

    it('cel proxy dispatches via tools/call with positional arguments after initialize()', async () => {
        const { client, sentMessages, simulateResponse } = createTestClient({
            context: { allowedTools: ['app.get_state'], secrets: {} }
        });

        // Kick off initialize() — it sends document/initialize, then tools/list,
        // then (optionally) localization if metadata.locale is present.
        const initPromise = client.initialize();

        const docInitMessage = JSON.parse(sentMessages[0]);
        expect(docInitMessage.method).toBe('document/initialize');
        simulateResponse(docInitMessage.id, {
            content: '',
            metadata: { filePath: '', resourceKey: '', fileName: '', locale: '' }
        });

        // Allow the microtask queue to advance so tools/list is posted.
        await Promise.resolve();
        await Promise.resolve();

        const listMessage = JSON.parse(sentMessages[1]);
        expect(listMessage.method).toBe('tools/list');
        simulateResponse(listMessage.id, [
            descriptor('app.get_state', [])
        ]);

        await initPromise;

        // Now the proxy is built. Call via positional shape.
        const callPromise = client.cel.app.getState();

        const callMessage = JSON.parse(sentMessages[2]);
        expect(callMessage.method).toBe('tools/call');
        expect(callMessage.params.name).toBe('app.get_state');
        expect(callMessage.params.arguments).toEqual({});

        simulateResponse(callMessage.id, { isSuccess: true, value: '0.2.5' });

        await expect(callPromise).resolves.toBe('0.2.5');
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

describe('cel globalThis exposure', () => {
    it('assigns globalThis.cel after initialize() resolves', async () => {
        delete globalThis.cel;

        const { client, sentMessages, simulateResponse } = createTestClient({
            context: { allowedTools: ['app.get_state'], secrets: {} }
        });

        const initPromise = client.initialize();

        const docInit = JSON.parse(sentMessages[0]);
        simulateResponse(docInit.id, {
            content: '',
            metadata: { filePath: '', resourceKey: '', fileName: '', locale: '' }
        });

        await Promise.resolve();
        await Promise.resolve();

        const toolsList = JSON.parse(sentMessages[1]);
        simulateResponse(toolsList.id, [
            { name: 'app_get_state', alias: 'app.get_state', description: '', parameters: [] }
        ]);

        await initPromise;

        expect(globalThis.cel).toBeDefined();
        expect(typeof globalThis.cel.app.getState).toBe('function');

        delete globalThis.cel;
    });

    it('accessing cel global before initialize() throws via client.cel', () => {
        delete globalThis.cel;
        const { client } = createTestClient({
            context: { allowedTools: ['*'], secrets: {} }
        });

        expect(() => client.cel).toThrow(/not initialized/);
        expect(globalThis.cel).toBeUndefined();
    });

    it('exposeCelGlobal: false keeps globalThis.cel untouched', async () => {
        delete globalThis.cel;

        const sentMessages = [];
        let messageHandler = null;
        const client = new Celbridge({
            postMessage: (msg) => sentMessages.push(msg),
            onMessage: (handler) => { messageHandler = handler; },
            timeout: 1000,
            exposeCelGlobal: false,
            context: { allowedTools: [], secrets: {} }
        });

        const initPromise = client.initialize();
        const docInit = JSON.parse(sentMessages[0]);
        messageHandler(JSON.stringify({
            jsonrpc: '2.0',
            id: docInit.id,
            result: { content: '', metadata: { filePath: '', resourceKey: '', fileName: '' } }
        }));

        await initPromise;

        // With allowedTools=[], no tools/list is sent, so init completes with only the one message.
        expect(globalThis.cel).toBeUndefined();
        expect(client.tools.isReady).toBe(true);
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

        client.input.notifyShortcut('W', { ctrl: true, shift: false, alt: false });

        const sent = JSON.parse(sentMessages[0]);
        expect(sent.jsonrpc).toBe('2.0');
        expect(sent.method).toBe('input/keyboardShortcut');
        expect(sent.params).toEqual({
            key: 'W',
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

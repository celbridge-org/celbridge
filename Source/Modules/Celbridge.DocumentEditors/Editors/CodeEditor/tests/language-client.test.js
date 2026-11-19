import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { createModelStub, FakeChannel } from './fixtures/monaco-language-stub.js';
import { LanguageChannel, LanguageClient } from '../js/language-client.js';

const incrementalCapabilities = { textDocumentSync: { openClose: true, change: 2 }, hoverProvider: true };

let channel;
let connections;
let model;
let onReady;
let onDiagnostics;
let onUnavailable;
let onDisconnected;

function createClient() {
    return new LanguageClient({
        model,
        channel,
        onReady,
        onDiagnostics,
        onUnavailable,
        onDisconnected,
        reconnectDelayMs: 1000
    });
}

// Connects a client, and delivers the host's first message on the connection.
function connectReadyClient(capabilities = incrementalCapabilities) {
    const client = createClient();
    client.connect();
    connections[0].receive({ method: 'celbridge/serverReady', params: { serverId: 'typescript', capabilities } });

    return client;
}

beforeEach(() => {
    vi.useFakeTimers();
    channel = new FakeChannel();
    connections = channel.connections;
    model = createModelStub({ filePath: 'D:\\Projects\\Acme\\src\\main.ts', text: 'let a = 1;' });
    onReady = vi.fn();
    onDiagnostics = vi.fn();
    onUnavailable = vi.fn();
    onDisconnected = vi.fn();
});

afterEach(() => {
    vi.useRealTimers();
});

describe('LanguageClient connecting', () => {
    it('sends the whole text once the host reports the server ready, and passes on the capabilities', () => {
        connectReadyClient();

        expect(connections[0].sent).toEqual([{
            jsonrpc: '2.0',
            method: 'textDocument/didOpen',
            params: { text: 'let a = 1;', version: 1 }
        }]);
        expect(onReady).toHaveBeenCalledWith(incrementalCapabilities);
    });

    it('sends nothing before the host reports the server ready', async () => {
        const client = createClient();
        client.connect();

        model.change('let a = 2;', []);
        const result = await client.sendRequest('textDocument/hover', {});

        expect(connections[0].sent).toEqual([]);
        expect(result).toBeNull();
    });
});

describe('LanguageClient changes', () => {
    it('sends each change as a range and text to a server that takes changes', () => {
        connectReadyClient();

        model.change('let ab = 1;', [{
            range: { startLineNumber: 1, startColumn: 6, endLineNumber: 1, endColumn: 6 },
            rangeLength: 0,
            text: 'b'
        }]);

        expect(connections[0].sentWith('textDocument/didChange')).toEqual([{
            jsonrpc: '2.0',
            method: 'textDocument/didChange',
            params: {
                version: 2,
                contentChanges: [{
                    range: { start: { line: 0, character: 5 }, end: { line: 0, character: 5 } },
                    rangeLength: 0,
                    text: 'b'
                }]
            }
        }]);
    });

    it('sends the whole text to a server that takes whole documents', () => {
        connectReadyClient({ textDocumentSync: 1 });

        model.change('let ab = 1;', [{
            range: { startLineNumber: 1, startColumn: 6, endLineNumber: 1, endColumn: 6 },
            rangeLength: 0,
            text: 'b'
        }]);

        expect(connections[0].sentWith('textDocument/didChange')[0].params.contentChanges).toEqual([{ text: 'let ab = 1;' }]);
    });

    it('sends the whole text when the buffer is replaced', () => {
        connectReadyClient();

        model.change('reloaded', [], true);

        expect(connections[0].sentWith('textDocument/didChange')[0].params.contentChanges).toEqual([{ text: 'reloaded' }]);
    });

    it('sends no changes to a server that syncs no documents', () => {
        connectReadyClient({ textDocumentSync: 0 });

        model.change('let ab = 1;', []);

        expect(connections[0].sentWith('textDocument/didChange')).toEqual([]);
    });
});

describe('LanguageClient requests', () => {
    it('returns the server\'s result for a request', async () => {
        const client = connectReadyClient();

        const pending = client.sendRequest('textDocument/hover', { position: { line: 0, character: 4 } });
        const request = connections[0].sentWith('textDocument/hover')[0];
        connections[0].receive({ id: request.id, result: { contents: 'a number' } });

        expect(request.params).toEqual({ position: { line: 0, character: 4 } });
        expect(await pending).toEqual({ contents: 'a number' });
    });

    it('returns null when the server fails the request', async () => {
        const client = connectReadyClient();

        const pending = client.sendRequest('textDocument/hover', {});
        const request = connections[0].sentWith('textDocument/hover')[0];
        connections[0].receive({ id: request.id, error: { code: -32801, message: 'Content modified' } });

        expect(await pending).toBeNull();
    });

    it('gives each request its own id, and matches each answer to its request', async () => {
        const client = connectReadyClient();

        const first = client.sendRequest('textDocument/hover', {});
        const second = client.sendRequest('textDocument/definition', {});
        const [hoverRequest] = connections[0].sentWith('textDocument/hover');
        const [definitionRequest] = connections[0].sentWith('textDocument/definition');
        connections[0].receive({ id: definitionRequest.id, result: 'definition' });
        connections[0].receive({ id: hoverRequest.id, result: 'hover' });

        expect(hoverRequest.id).not.toBe(definitionRequest.id);
        expect(await first).toBe('hover');
        expect(await second).toBe('definition');
    });

    it('cancels a request on the host when Monaco cancels it, and returns null', async () => {
        const client = connectReadyClient();
        let cancel;
        const token = {
            onCancellationRequested: (listener) => {
                cancel = listener;
                return { dispose: () => {} };
            }
        };

        const pending = client.sendRequest('textDocument/references', {}, token);
        const request = connections[0].sentWith('textDocument/references')[0];
        cancel();

        expect(await pending).toBeNull();
        expect(connections[0].sentWith('$/cancelRequest')).toEqual([{
            jsonrpc: '2.0',
            method: '$/cancelRequest',
            params: { id: request.id }
        }]);
    });

    it('returns null for a request that is waiting when the host closes the connection', async () => {
        const client = connectReadyClient();

        const pending = client.sendRequest('textDocument/hover', {});
        connections[0].close();

        expect(await pending).toBeNull();
        expect(onDisconnected).toHaveBeenCalledTimes(1);
    });
});

describe('LanguageClient diagnostics', () => {
    it('passes on the document\'s diagnostics', () => {
        connectReadyClient();
        const diagnostics = [{ message: 'A type error' }];

        connections[0].receive({ method: 'textDocument/publishDiagnostics', params: { diagnostics } });

        expect(onDiagnostics).toHaveBeenCalledWith(diagnostics);
    });
});

describe('LanguageClient reconnecting', () => {
    it('opens a new connection after the host closes one, and sends the current text on it', () => {
        connectReadyClient();
        model.change('let a = 2;', [{
            range: { startLineNumber: 1, startColumn: 9, endLineNumber: 1, endColumn: 10 },
            rangeLength: 1,
            text: '2'
        }]);

        connections[0].close();
        expect(connections).toHaveLength(1);
        vi.advanceTimersByTime(1000);
        connections[1].receive({ method: 'celbridge/serverReady', params: { capabilities: incrementalCapabilities } });

        expect(connections).toHaveLength(2);
        expect(connections[1].sentWith('textDocument/didOpen')[0].params).toEqual({ text: 'let a = 2;', version: 2 });
        expect(onReady).toHaveBeenCalledTimes(2);
        expect(model.listenerCount()).toBe(1);
    });

    it('stops reconnecting once the host reports that no server will run', () => {
        const client = createClient();
        client.connect();

        connections[0].receive({ method: 'celbridge/serverUnavailable' });
        connections[0].close();
        vi.advanceTimersByTime(5000);

        expect(onUnavailable).toHaveBeenCalledTimes(1);
        expect(connections).toHaveLength(1);
    });

    it('closes the connection and stops for good when disposed', async () => {
        const client = connectReadyClient();
        const pending = client.sendRequest('textDocument/hover', {});

        client.dispose();
        vi.advanceTimersByTime(5000);
        model.change('later', []);

        expect(await pending).toBeNull();
        expect(connections).toHaveLength(1);
        expect(connections[0].readyState).toBe(3);
        expect(model.listenerCount()).toBe(0);
        expect(onDisconnected).not.toHaveBeenCalled();
    });
});

describe('LanguageChannel', () => {
    let handlers;
    let sent;
    let bridge;
    let receiver;

    beforeEach(() => {
        handlers = {};
        sent = [];
        bridge = {
            onNotification: (method, handler) => {
                handlers[method] = handler;
            },
            sendNotification: (method, params) => {
                sent.push({ method, params });
            }
        };
        receiver = { handleMessage: vi.fn(), handleClosed: vi.fn() };
    });

    it('opens, sends and closes through the bridge, with each message as JSON text', () => {
        const languageChannel = new LanguageChannel(bridge);

        languageChannel.open(receiver);
        languageChannel.send({ id: 1, method: 'textDocument/hover' });
        languageChannel.close(receiver);

        expect(sent).toEqual([
            { method: 'language/open', params: {} },
            { method: 'language/send', params: { message: '{"id":1,"method":"textDocument/hover"}' } },
            { method: 'language/close', params: {} }
        ]);
    });

    it('passes the host\'s messages and the host\'s close to the receiver that opened the connection', () => {
        const languageChannel = new LanguageChannel(bridge);
        languageChannel.open(receiver);

        handlers['language/message']({ message: '{"id":1,"result":"a number"}' });
        handlers['language/closed']();

        expect(receiver.handleMessage).toHaveBeenCalledWith({ id: 1, result: 'a number' });
        expect(receiver.handleClosed).toHaveBeenCalledTimes(1);
    });

    it('passes nothing on before a connection is open, and after the connection is closed', () => {
        const languageChannel = new LanguageChannel(bridge);

        handlers['language/message']({ message: '{}' });
        languageChannel.open(receiver);
        languageChannel.close(receiver);
        handlers['language/message']({ message: '{}' });
        handlers['language/closed']();

        expect(receiver.handleMessage).not.toHaveBeenCalled();
        expect(receiver.handleClosed).not.toHaveBeenCalled();
    });

    it('keeps the connection of a newer receiver when an older receiver closes', () => {
        const languageChannel = new LanguageChannel(bridge);
        const newerReceiver = { handleMessage: vi.fn(), handleClosed: vi.fn() };
        languageChannel.open(receiver);
        languageChannel.open(newerReceiver);

        languageChannel.close(receiver);
        handlers['language/message']({ message: '{"id":2}' });

        expect(sent.map((entry) => entry.method)).toEqual(['language/open', 'language/open']);
        expect(newerReceiver.handleMessage).toHaveBeenCalledWith({ id: 2 });
    });
});

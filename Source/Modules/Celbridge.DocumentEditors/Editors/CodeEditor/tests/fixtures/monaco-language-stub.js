// A stand-in for the parts of the global `monaco` namespace that the language server modules use. Each enum
// member's value is its own name, so a test reads which member a converter chose. Each register function records
// its provider. The editor functions record the editor opener, the commands and the models that the page creates.

import { vi } from 'vitest';

// Writes a file URI the way Monaco does: a lowercase drive letter, and the drive's colon percent-encoded.
function createUri(path) {
    let normalizedPath = path.replace(/\\/g, '/');
    if (/^[a-zA-Z]:/.test(normalizedPath)) {
        normalizedPath = '/' + normalizedPath;
    }
    normalizedPath = normalizedPath.replace(/^\/([a-zA-Z]):/, (match, drive) => `/${drive.toLowerCase()}:`);

    const text = 'file://' + normalizedPath.split('/').map(encodeURIComponent).join('/');

    return {
        scheme: 'file',
        path: normalizedPath,
        toString: () => text
    };
}

function parseUri(text) {
    if (!text.startsWith('file://')) {
        return { path: text, toString: () => text };
    }

    return createUri(decodeURIComponent(text.substring('file://'.length)));
}

const namedEnum = () => new Proxy({}, { get: (target, name) => name });

const registerFunctionNames = [
    'registerCompletionItemProvider',
    'registerHoverProvider',
    'registerSignatureHelpProvider',
    'registerDefinitionProvider',
    'registerDeclarationProvider',
    'registerTypeDefinitionProvider',
    'registerImplementationProvider',
    'registerReferenceProvider',
    'registerDocumentHighlightProvider',
    'registerDocumentSymbolProvider',
    'registerFoldingRangeProvider',
    'registerSelectionRangeProvider',
    'registerLinkProvider',
    'registerRenameProvider',
    'registerCodeActionProvider',
    'registerDocumentFormattingEditProvider',
    'registerDocumentRangeFormattingEditProvider'
];

/**
 * Installs the stand-in as the global `monaco`, and returns what the code under test did with it.
 * `providers` maps each register function's name to the provider it was given, with the provider's metadata.
 * `registrations` lists what each register function returned. `commands` maps each command's id to its handler.
 * `models` maps each created model's URI to the model. `opener` is the registered editor opener.
 */
export function installMonacoLanguageStub() {
    const providers = {};
    const registrations = [];
    const commands = {};
    const models = new Map();
    const state = { opener: null };

    const languages = {
        CompletionItemKind: namedEnum(),
        CompletionItemInsertTextRule: namedEnum(),
        CompletionItemTag: namedEnum(),
        DocumentHighlightKind: namedEnum(),
        SymbolKind: namedEnum(),
        SymbolTag: namedEnum(),
        FoldingRangeKind: namedEnum()
    };

    for (const name of registerFunctionNames) {
        languages[name] = vi.fn((language, provider, metadata) => {
            providers[name] = { language, provider, metadata };
            const registration = { name, dispose: vi.fn() };
            registrations.push(registration);
            return registration;
        });
    }

    const monaco = {
        Uri: { file: createUri, parse: parseUri },
        MarkerSeverity: namedEnum(),
        MarkerTag: namedEnum(),
        languages,
        editor: {
            setModelMarkers: vi.fn(),
            registerEditorOpener: vi.fn((opener) => {
                state.opener = opener;
                const registration = { name: 'registerEditorOpener', dispose: vi.fn() };
                registrations.push(registration);
                return registration;
            }),
            registerCommand: vi.fn((id, handler) => {
                commands[id] = handler;
                const registration = { name: 'registerCommand', dispose: vi.fn() };
                registrations.push(registration);
                return registration;
            }),
            getModel: vi.fn((uri) => models.get(uri.toString()) ?? null),
            createModel: vi.fn((text, language, uri) => {
                const model = {
                    uri,
                    text,
                    language,
                    setValue: vi.fn((value) => {
                        model.text = value;
                    }),
                    dispose: vi.fn(() => models.delete(uri.toString()))
                };
                models.set(uri.toString(), model);
                return model;
            })
        },
        typescript: {
            typescriptDefaults: { setModeConfiguration: vi.fn() },
            javascriptDefaults: { setModeConfiguration: vi.fn() }
        }
    };

    globalThis.monaco = monaco;

    return {
        monaco,
        providers,
        registrations,
        commands,
        models,
        get opener() {
            return state.opener;
        }
    };
}

/**
 * A Monaco model for the language server modules: a URI, a language, text and a version, with a way to raise a
 * content change.
 */
export function createModelStub({ filePath, language = 'typescript', text = '' }) {
    const listeners = [];
    const model = {
        uri: createUri(filePath),
        text,
        versionId: 1,
        getLanguageId: () => language,
        getValue: () => model.text,
        getVersionId: () => model.versionId,
        getWordUntilPosition: (position) => ({ word: '', startColumn: position.column, endColumn: position.column }),
        // Returns the text of a range on one line.
        getValueInRange: (range) => {
            const line = model.text.split('\n')[range.startLineNumber - 1] ?? '';
            return line.substring(range.startColumn - 1, range.endColumn - 1);
        },
        onDidChangeContent: (listener) => {
            listeners.push(listener);
            return {
                dispose: () => listeners.splice(listeners.indexOf(listener), 1)
            };
        },
        // Changes the text, and raises the change the way Monaco does.
        change: (newText, changes, isFlush = false) => {
            model.text = newText;
            model.versionId++;
            for (const listener of [...listeners]) {
                listener({ changes, versionId: model.versionId, isFlush });
            }
        },
        listenerCount: () => listeners.length
    };

    return model;
}

/**
 * One connection that the language client opened on a fake channel. Records what the client sends, and lets a
 * test deliver the host's messages and close the connection from the host's side.
 */
export class FakeConnection {
    readyState = 1;
    sent = [];

    constructor(receiver) {
        this.receiver = receiver;
    }

    // Closes the connection from the host's side.
    close() {
        if (this.readyState === 3) {
            return;
        }

        this.readyState = 3;
        this.receiver.handleClosed();
    }

    // Delivers one message from the host.
    receive(message) {
        if (this.readyState === 1) {
            this.receiver.handleMessage(JSON.parse(JSON.stringify(message)));
        }
    }

    // The messages the client sent with the given method.
    sentWith(method) {
        return this.sent.filter((message) => message.method === method);
    }
}

/**
 * A language channel for the language client. Each `open` adds a connection to `connections`.
 */
export class FakeChannel {
    connections = [];
    #current = null;

    open(receiver) {
        if (this.#current) {
            this.#current.readyState = 3;
        }

        this.#current = new FakeConnection(receiver);
        this.connections.push(this.#current);
    }

    send(message) {
        this.#current.sent.push(JSON.parse(JSON.stringify(message)));
    }

    close(receiver) {
        if (this.#current?.receiver !== receiver) {
            return;
        }

        this.#current.readyState = 3;
        this.#current = null;
    }
}

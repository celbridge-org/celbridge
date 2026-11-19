// Gives the editor a language server's features for the page's document. Each feature has one Monaco provider.
// A provider turns Monaco's arguments into a Language Server Protocol request, and sends the request through the
// language client. The provider then turns the answer into Monaco's types. The server's diagnostics become the
// model's markers.
//
// The module expects the global `monaco` AMD namespace to be loaded before any method is called.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { isMacOS, isWindows } from '/assets/celbridge-client/platform.js';
import { LanguageChannel, LanguageClient } from './language-client.js';
import * as convert from './lsp-convert.js';

const markerOwner = 'celbridge-language-server';

// Monaco's mode configuration for its built-in TypeScript and JavaScript worker, with every option off.
const builtInFeaturesOff = {
    completionItems: false,
    hovers: false,
    documentSymbols: false,
    definitions: false,
    references: false,
    documentHighlights: false,
    rename: false,
    diagnostics: false,
    documentRangeFormattingEdits: false,
    signatureHelp: false,
    onTypeFormattingEdits: false,
    codeActions: false,
    inlayHints: false
};

export class LanguageFeatures {
    #channel;
    #serverId = '';
    #model = null;
    #documentUri = '';
    #client = null;
    #registrations = [];

    /**
     * @param {Object} [options]
     * @param {Object} [options.channel] - The page's language channel. The default uses the page's bridge.
     */
    constructor({ channel } = {}) {
        this.#channel = channel ?? new LanguageChannel(celbridge);
    }

    /**
     * Sets the document's language, and connects the document to the language server named in the host's
     * metadata.
     * The document leaves the previous server first. `setLanguage` sets the model's language.
     * @param {Object} model - The Monaco model of the document.
     * @param {string} language - The Monaco language of the document.
     * @param {Object} metadata - The document's metadata from the host.
     * @param {Function} setLanguage - Called with the language, once Monaco is ready for the language.
     */
    open(model, language, metadata, setLanguage) {
        this.stop();
        this.prepare(language, metadata);
        setLanguage(language);
        this.start(model, metadata);
    }

    /**
     * Readies Monaco for a document with a language server, by turning off Monaco's own features for the
     * document's language. Monaco applies the change only before the language activates, so call this before the
     * model gets the language. Only a document with a server changes Monaco.
     * @param {string} language - The Monaco language the model is about to get.
     * @param {Object} metadata - The document's metadata from the host.
     */
    prepare(language, metadata) {
        this.#serverId = metadata?.languageServer ?? '';
        if (!this.#serverId) {
            return;
        }

        const typescript = monaco.typescript;
        if (language === 'typescript') {
            typescript.typescriptDefaults.setModeConfiguration(builtInFeaturesOff);
        } else if (language === 'javascript') {
            typescript.javascriptDefaults.setModeConfiguration(builtInFeaturesOff);
        }
    }

    /**
     * Connects the document to the document's language server. The providers register once the server reports
     * the capabilities. Only a document with a server connects.
     * @param {Object} model - The Monaco model of the document.
     * @param {Object} metadata - The document's metadata from the host.
     */
    start(model, metadata) {
        if (!this.#serverId || !metadata?.filePath) {
            return;
        }

        this.#model = model;

        // The model keeps the URI it was created with, so the document's current URI is recorded here.
        this.#documentUri = normalizeUri(monaco.Uri.file(metadata.filePath));

        this.#client = new LanguageClient({
            model,
            channel: this.#channel,
            onReady: (capabilities) => this.#registerProviders(capabilities),
            onDiagnostics: (diagnostics) => {
                monaco.editor.setModelMarkers(model, markerOwner, convert.toMarkers(diagnostics));
            },
            onUnavailable: () => this.#removeProviders(),
            // Clear the markers while the server is down. Syntax coloring stays.
            onDisconnected: () => monaco.editor.setModelMarkers(model, markerOwner, [])
        });
        this.#client.connect();
    }

    /**
     * Disconnects the document from its language server, and removes the providers and the markers.
     */
    stop() {
        this.#client?.dispose();
        this.#client = null;
        this.#removeProviders();
        this.#model = null;
        this.#serverId = '';
    }

    #removeProviders() {
        for (const registration of this.#registrations) {
            registration.dispose();
        }
        this.#registrations = [];

        if (this.#model) {
            monaco.editor.setModelMarkers(this.#model, markerOwner, []);
        }
    }

    // Registers a provider for each feature in the server's capabilities. A connection that opens again
    // reports the capabilities again, and the providers already registered stay.
    #registerProviders(capabilities) {
        if (this.#registrations.length > 0) {
            return;
        }

        const languages = monaco.languages;
        const language = this.#model.getLanguageId();
        const model = this.#model;
        const client = this.#client;

        const at = (position) => ({ position: convert.toLspPosition(position) });
        const ownModel = (candidate) => candidate === model;
        const resolveUri = (uri) => this.#resolveOwnUri(uri);

        const locationProvider = (method) => async (candidate, position, token) => {
            if (!ownModel(candidate)) {
                return null;
            }

            const result = await client.sendRequest(method, at(position), token);

            return convert.toLocations(result, resolveUri);
        };

        if (capabilities.completionProvider) {
            const provider = {
                triggerCharacters: capabilities.completionProvider.triggerCharacters ?? [],
                provideCompletionItems: async (candidate, position, context, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = { ...at(position), context: convert.toCompletionContext(context) };
                    const result = await client.sendRequest('textDocument/completion', params, token);

                    const word = candidate.getWordUntilPosition(position);
                    const wordRange = {
                        startLineNumber: position.lineNumber,
                        startColumn: word.startColumn,
                        endLineNumber: position.lineNumber,
                        endColumn: word.endColumn
                    };

                    return convert.toCompletionList(result, wordRange);
                }
            };

            if (capabilities.completionProvider.resolveProvider) {
                provider.resolveCompletionItem = async (suggestion, token) => {
                    const resolvedItem = await client.sendRequest('completionItem/resolve', suggestion.lspItem, token);

                    return convert.applyResolvedCompletionItem(suggestion, resolvedItem);
                };
            }

            this.#registrations.push(languages.registerCompletionItemProvider(language, provider));
        }

        if (capabilities.hoverProvider) {
            this.#registrations.push(languages.registerHoverProvider(language, {
                provideHover: async (candidate, position, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/hover', at(position), token);

                    return convert.toHover(result);
                }
            }));
        }

        if (capabilities.signatureHelpProvider) {
            this.#registrations.push(languages.registerSignatureHelpProvider(language, {
                signatureHelpTriggerCharacters: capabilities.signatureHelpProvider.triggerCharacters ?? [],
                signatureHelpRetriggerCharacters: capabilities.signatureHelpProvider.retriggerCharacters ?? [],
                provideSignatureHelp: async (candidate, position, token, context) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    // Monaco and the protocol number the trigger kinds alike.
                    const params = {
                        ...at(position),
                        context: {
                            triggerKind: context?.triggerKind ?? 1,
                            triggerCharacter: context?.triggerCharacter,
                            isRetrigger: context?.isRetrigger === true
                        }
                    };
                    const result = await client.sendRequest('textDocument/signatureHelp', params, token);

                    return convert.toSignatureHelp(result);
                }
            }));
        }

        if (capabilities.definitionProvider) {
            this.#registrations.push(languages.registerDefinitionProvider(language, {
                provideDefinition: locationProvider('textDocument/definition')
            }));
        }

        if (capabilities.declarationProvider) {
            this.#registrations.push(languages.registerDeclarationProvider(language, {
                provideDeclaration: locationProvider('textDocument/declaration')
            }));
        }

        if (capabilities.typeDefinitionProvider) {
            this.#registrations.push(languages.registerTypeDefinitionProvider(language, {
                provideTypeDefinition: locationProvider('textDocument/typeDefinition')
            }));
        }

        if (capabilities.implementationProvider) {
            this.#registrations.push(languages.registerImplementationProvider(language, {
                provideImplementation: locationProvider('textDocument/implementation')
            }));
        }

        if (capabilities.referencesProvider) {
            this.#registrations.push(languages.registerReferenceProvider(language, {
                provideReferences: async (candidate, position, context, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = {
                        ...at(position),
                        context: { includeDeclaration: context?.includeDeclaration === true }
                    };
                    const result = await client.sendRequest('textDocument/references', params, token);

                    return convert.toLocations(result, resolveUri);
                }
            }));
        }

        if (capabilities.documentHighlightProvider) {
            this.#registrations.push(languages.registerDocumentHighlightProvider(language, {
                provideDocumentHighlights: async (candidate, position, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/documentHighlight', at(position), token);
                    const highlights = convert.toDocumentHighlights(result);

                    // When the server's answer is empty, Monaco highlights the other uses of the word.
                    return highlights.length > 0 ? highlights : null;
                }
            }));
        }

        if (capabilities.documentSymbolProvider) {
            this.#registrations.push(languages.registerDocumentSymbolProvider(language, {
                provideDocumentSymbols: async (candidate, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/documentSymbol', {}, token);

                    return convert.toDocumentSymbols(result);
                }
            }));
        }

        if (capabilities.foldingRangeProvider) {
            this.#registrations.push(languages.registerFoldingRangeProvider(language, {
                provideFoldingRanges: async (candidate, context, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/foldingRange', {}, token);

                    return convert.toFoldingRanges(result);
                }
            }));
        }

        if (capabilities.selectionRangeProvider) {
            this.#registrations.push(languages.registerSelectionRangeProvider(language, {
                provideSelectionRanges: async (candidate, positions, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = { positions: positions.map(convert.toLspPosition) };
                    const result = await client.sendRequest('textDocument/selectionRange', params, token);

                    return convert.toSelectionRanges(result);
                }
            }));
        }

        if (capabilities.documentLinkProvider) {
            const provider = {
                provideLinks: async (candidate, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/documentLink', {}, token);

                    return convert.toLinks(result);
                }
            };

            if (capabilities.documentLinkProvider.resolveProvider) {
                provider.resolveLink = async (link, token) => {
                    const resolvedLink = await client.sendRequest('documentLink/resolve', link.lspLink, token);

                    return resolvedLink ? convert.toLink(resolvedLink) : link;
                };
            }

            this.#registrations.push(languages.registerLinkProvider(language, provider));
        }
    }

    // Returns the model's URI for a server's URI that names the page's own document, and null for another file.
    // A location in another file has no model in this page.
    #resolveOwnUri(uri) {
        if (normalizeUri(monaco.Uri.parse(uri)) !== this.#documentUri) {
            return null;
        }

        return this.#model.uri;
    }
}

// Returns a URI as text that compares equal for two spellings of one file's path. Windows and macOS ignore letter
// case in file names.
function normalizeUri(uri) {
    const text = uri.toString();

    return isWindows() || isMacOS() ? text.toLowerCase() : text;
}

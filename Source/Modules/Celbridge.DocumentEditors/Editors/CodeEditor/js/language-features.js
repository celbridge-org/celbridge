// Gives the editor a language server's features for the page's document. Each feature has one Monaco provider.
// A provider turns Monaco's arguments into a Language Server Protocol request, and sends the request through the
// language client. The provider then turns the answer into Monaco's types. The server's diagnostics become the
// model's markers. A location in another file gets a read-only copy of the file's text for a peek to show. The
// host applies a rename's or a code action's edit, so an edit to another file reaches that file. Each semantic
// token gets one of the token types in the editor's themes. The page leaves out the server's inlay hints.
//
// The module expects the global `monaco` AMD namespace to be loaded before any method is called.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { t } from '/assets/celbridge-client/localization.js';
import { isMacOS, isWindows } from '/assets/celbridge-client/platform.js';
import { semanticTokenLegend } from './editor-themes.js';
import { LanguageChannel, LanguageClient } from './language-client.js';
import { getLanguageForFile } from './language-mapper.js';
import * as convert from './lsp-convert.js';

const markerOwner = 'celbridge-language-server';

// The host's own request for the text of another file. Mirrored by PageMessages in the host's language servers
// module.
const fileTextMethod = 'celbridge/fileText';

// The page's command that applies a code action. Monaco runs the command with the server's action as the argument.
const applyCodeActionCommand = 'celbridge.applyCodeAction';

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
    #diagnostics = [];
    // Each other file's copy for a peek, keyed by the file's URI, with the host's stamp for the copy.
    #peekModels = new Map();

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
     * Readies Monaco for a document with a language server, by turning off Monaco's own TypeScript and JavaScript
     * features. A peek can show a file in either language, so both languages lose the features. Monaco applies the
     * change only before a language activates, so call this before the model gets the language. Only a TypeScript
     * or JavaScript document with a server changes Monaco.
     * @param {string} language - The Monaco language the model is about to get.
     * @param {Object} metadata - The document's metadata from the host.
     */
    prepare(language, metadata) {
        this.#serverId = metadata?.languageServer ?? '';
        if (!this.#serverId) {
            return;
        }

        if (language !== 'typescript' && language !== 'javascript') {
            return;
        }

        const typescript = monaco.typescript;
        typescript.typescriptDefaults.setModeConfiguration(builtInFeaturesOff);
        typescript.javascriptDefaults.setModeConfiguration(builtInFeaturesOff);
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
                this.#diagnostics = diagnostics;
                monaco.editor.setModelMarkers(model, markerOwner, convert.toMarkers(diagnostics));
            },
            onUnavailable: () => this.#removeProviders(),
            // Clear the markers while the server is down. Syntax coloring stays.
            onDisconnected: () => {
                this.#diagnostics = [];
                monaco.editor.setModelMarkers(model, markerOwner, []);
            }
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

        for (const { model } of this.#peekModels.values()) {
            model.dispose();
        }
        this.#peekModels.clear();

        this.#diagnostics = [];
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

        const locationProvider = (method) => async (candidate, position, token) => {
            if (!ownModel(candidate)) {
                return null;
            }

            const result = await client.sendRequest(method, at(position), token);

            return this.#toLocations(result);
        };

        // Go to definition into another file asks the host to open the file at the location.
        this.#registrations.push(monaco.editor.registerEditorOpener({
            openCodeEditor: (source, resource, selectionOrPosition) =>
                this.#openOtherFile(resource, selectionOrPosition)
        }));

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

                    return this.#toLocations(result);
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

        if (capabilities.renameProvider) {
            const provider = {
                provideRenameEdits: async (candidate, position, newName, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = { ...at(position), newName };
                    const result = await client.sendRequest('textDocument/rename', params, token);
                    if (!result) {
                        return { edits: [], rejectReason: t('CodeEditor_Rename_Refused') };
                    }

                    // The host applies the edit, so the edit reaches files that have no model in this page. Monaco
                    // gets an empty edit, and applies nothing itself.
                    const applied = await this.#applyEdit(result);
                    if (!applied) {
                        return { edits: [], rejectReason: t('CodeEditor_Rename_Failed') };
                    }

                    return { edits: [] };
                }
            };

            if (capabilities.renameProvider.prepareProvider) {
                provider.resolveRenameLocation = async (candidate, position, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/prepareRename', at(position), token);

                    return convert.toRenameLocation(result, candidate, position, t('CodeEditor_Rename_Refused'));
                };
            }

            this.#registrations.push(languages.registerRenameProvider(language, provider));
        }

        if (capabilities.codeActionProvider) {
            const canResolve = capabilities.codeActionProvider.resolveProvider === true;

            this.#registrations.push(monaco.editor.registerCommand(applyCodeActionCommand, (accessor, action) =>
                this.#applyCodeAction(action, canResolve)));

            const metadata = {};
            if (Array.isArray(capabilities.codeActionProvider.codeActionKinds)) {
                metadata.providedCodeActionKinds = capabilities.codeActionProvider.codeActionKinds;
            }

            this.#registrations.push(languages.registerCodeActionProvider(language, {
                provideCodeActions: async (candidate, range, context, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    // Monaco and the protocol number the trigger kinds alike.
                    const params = {
                        range: convert.toLspRange(range),
                        context: {
                            diagnostics: convert.findDiagnostics(context.markers, this.#diagnostics),
                            triggerKind: context.trigger
                        }
                    };
                    if (context.only) {
                        params.context.only = [context.only];
                    }

                    const result = await client.sendRequest('textDocument/codeAction', params, token);

                    return convert.toCodeActionList(result, applyCodeActionCommand, canResolve);
                }
            }, metadata));
        }

        if (capabilities.documentFormattingProvider) {
            this.#registrations.push(languages.registerDocumentFormattingEditProvider(language, {
                provideDocumentFormattingEdits: async (candidate, options, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = { options: convert.toFormattingOptions(options) };
                    const result = await client.sendRequest('textDocument/formatting', params, token);

                    return convert.toTextEdits(result);
                }
            }));
        }

        if (capabilities.documentRangeFormattingProvider) {
            this.#registrations.push(languages.registerDocumentRangeFormattingEditProvider(language, {
                provideDocumentRangeFormattingEdits: async (candidate, range, options, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const params = {
                        range: convert.toLspRange(range),
                        options: convert.toFormattingOptions(options)
                    };
                    const result = await client.sendRequest('textDocument/rangeFormatting', params, token);

                    return convert.toTextEdits(result);
                }
            }));
        }

        const semanticTokensProvider = capabilities.semanticTokensProvider;
        if (semanticTokensProvider?.full
            && semanticTokensProvider.legend) {
            this.#registrations.push(languages.registerDocumentSemanticTokensProvider(language, {
                getLegend: () => semanticTokenLegend,
                provideDocumentSemanticTokens: async (candidate, lastResultId, token) => {
                    if (!ownModel(candidate)) {
                        return null;
                    }

                    const result = await client.sendRequest('textDocument/semanticTokens/full', {}, token);
                    if (!result) {
                        return null;
                    }

                    return convert.toSemanticTokens(result, semanticTokensProvider.legend);
                },
                releaseDocumentSemanticTokens: () => {}
            }));
        }
    }

    // Returns Monaco's locations for a location answer. A location in another file gets a read-only model with
    // the file's text first, since a peek needs a model to show the location.
    async #toLocations(result) {
        if (!this.#model) {
            return [];
        }

        const locations = convert.toLocations(result, (uri) => this.#resolveUri(uri));

        const otherUris = new Map();
        for (const location of locations) {
            if (location.uri !== this.#model.uri) {
                otherUris.set(location.uri.toString(), location.uri);
            }
        }

        await Promise.all([...otherUris.values()].map((uri) => this.#preparePeekModel(uri)));

        return locations;
    }

    // Returns the model's URI for a server's URI that names the page's own document, and Monaco's URI for another
    // file.
    #resolveUri(uri) {
        const monacoUri = monaco.Uri.parse(uri);
        if (normalizeUri(monacoUri) === this.#documentUri) {
            return this.#model.uri;
        }

        return monacoUri;
    }

    // Creates or refreshes the read-only copy of another file's text. The host sends the text again only when the
    // file has changed since the copy's stamp. When the host fails to read a file, a peek of
    // the file stays empty.
    async #preparePeekModel(uri) {
        const key = uri.toString();

        // A file renamed onto the model's original path would share the page's own model.
        if (key === this.#model.uri.toString()) {
            return;
        }

        const peekModel = this.#peekModels.get(key);
        const params = { uri: key };
        if (peekModel) {
            params.stamp = peekModel.stamp;
        }

        const result = await this.#client?.sendRequest(fileTextMethod, params);

        // The page may have left the server while the host read the file. An answer without text means that the
        // copy is current.
        if (!this.#model
            || !result
            || typeof result.text !== 'string') {
            return;
        }

        const existingModel = monaco.editor.getModel(uri);
        if (existingModel) {
            existingModel.setValue(result.text);
            this.#peekModels.set(key, { model: existingModel, stamp: result.stamp });
            return;
        }

        const fileName = uri.path.substring(uri.path.lastIndexOf('/') + 1);
        const model = monaco.editor.createModel(result.text, getLanguageForFile(fileName), uri);
        this.#peekModels.set(key, { model, stamp: result.stamp });
    }

    // Asks the host to open another file at a location. Monaco moves within the page's own document itself.
    async #openOtherFile(resource, selectionOrPosition) {
        if (!this.#client
            || resource.toString() === this.#model?.uri.toString()) {
            return false;
        }

        const params = { uri: resource.toString(), takeFocus: true };
        if (selectionOrPosition?.startLineNumber !== undefined) {
            params.selection = convert.toLspRange(selectionOrPosition);
        } else if (selectionOrPosition?.lineNumber !== undefined) {
            const position = convert.toLspPosition(selectionOrPosition);
            params.selection = { start: position, end: position };
        }

        const result = await this.#client.sendRequest('window/showDocument', params);

        return result?.success === true;
    }

    // Hands a workspace edit to the host, and returns true once the host has applied every change.
    async #applyEdit(edit) {
        const result = await this.#client?.sendRequest('workspace/applyEdit', { edit });

        return result?.applied === true;
    }

    // Applies the server's code action. An action without an edit asks the server to fill the edit in first.
    async #applyCodeAction(action, canResolve) {
        let resolvedAction = action;
        if (!resolvedAction?.edit && canResolve) {
            resolvedAction = await this.#client?.sendRequest('codeAction/resolve', action);
        }

        if (!resolvedAction?.edit) {
            return;
        }

        await this.#applyEdit(resolvedAction.edit);
    }
}

// Returns a URI as text that compares equal for two spellings of one file's path. Windows and macOS ignore letter
// case in file names.
function normalizeUri(uri) {
    const text = uri.toString();

    return isWindows() || isMacOS() ? text.toLowerCase() : text;
}

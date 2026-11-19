import { describe, it, expect, beforeEach } from 'vitest';

import { installMonacoLanguageStub, createModelStub, FakeChannel } from './fixtures/monaco-language-stub.js';
import { LanguageFeatures } from '../js/language-features.js';

// The TypeScript language server's capabilities and answers for main.ts of a two-file project.
import recorded from './fixtures/tsc-answers.json';

const answers = recorded.answers;

const mainPath = 'D:\\Projects\\Acme\\src\\main.ts';
const serverMetadata = { filePath: mainPath, fileName: 'main.ts', languageServer: 'typescript' };

let stub;
let connections;
let model;
let features;

// Answers the next request with the given method, the way the host passes on the server's answer.
function answerRequest(method, result) {
    const connection = connections[connections.length - 1];
    const request = connection.sentWith(method).at(-1);
    connection.receive({ id: request.id, result });

    return request;
}

// Starts the features for main.ts, and delivers the server's capabilities.
function startWithServer(capabilities = answers.capabilities, metadata = serverMetadata) {
    features.prepare('typescript', metadata);
    features.start(model, metadata);
    connections[0].receive({ method: 'celbridge/serverReady', params: { serverId: 'typescript', capabilities } });
}

beforeEach(() => {
    stub = installMonacoLanguageStub();
    const channel = new FakeChannel();
    connections = channel.connections;
    model = createModelStub({ filePath: mainPath, text: recorded.mainText });
    features = new LanguageFeatures({ channel });
});

describe('LanguageFeatures.prepare', () => {
    it('turns off Monaco\'s own features for the language of a document with a server', () => {
        features.prepare('typescript', serverMetadata);

        const { typescriptDefaults, javascriptDefaults } = stub.monaco.typescript;
        expect(typescriptDefaults.setModeConfiguration).toHaveBeenCalledTimes(1);
        const configuration = typescriptDefaults.setModeConfiguration.mock.calls[0][0];
        expect(Object.values(configuration).every((value) => value === false)).toBe(true);
        expect(configuration.diagnostics).toBe(false);
        expect(javascriptDefaults.setModeConfiguration).not.toHaveBeenCalled();
    });

    it('turns off the JavaScript features alone for a JavaScript document', () => {
        features.prepare('javascript', serverMetadata);

        const { typescriptDefaults, javascriptDefaults } = stub.monaco.typescript;
        expect(javascriptDefaults.setModeConfiguration).toHaveBeenCalledTimes(1);
        expect(typescriptDefaults.setModeConfiguration).not.toHaveBeenCalled();
    });

    it('leaves Monaco\'s own features on, and opens no connection, when the host reports no server', () => {
        const metadata = { ...serverMetadata, languageServer: '' };

        features.prepare('typescript', metadata);
        features.start(model, metadata);

        expect(stub.monaco.typescript.typescriptDefaults.setModeConfiguration).not.toHaveBeenCalled();
        expect(connections).toHaveLength(0);
    });
});

describe('LanguageFeatures.open', () => {
    it('turns off Monaco\'s own features before the model gets its language, and connects after', () => {
        const steps = [];
        stub.monaco.typescript.typescriptDefaults.setModeConfiguration.mockImplementation(() => steps.push('features off'));

        features.open(model, 'typescript', serverMetadata, (language) => {
            steps.push(`language ${language}`);
            expect(connections).toHaveLength(0);
        });

        expect(steps).toEqual(['features off', 'language typescript']);
        expect(connections).toHaveLength(1);
    });

    it('leaves the previous server before joining the next', () => {
        startWithServer();
        const registrations = [...stub.registrations];

        features.open(model, 'plaintext', { ...serverMetadata, languageServer: '' }, () => {});

        expect(registrations.every((registration) => registration.dispose.mock.calls.length === 1)).toBe(true);
        expect(connections[0].readyState).toBe(3);
        expect(connections).toHaveLength(1);
    });
});

describe('LanguageFeatures providers', () => {
    it('registers a provider for each feature the server offers, for the model\'s language', () => {
        startWithServer();

        // The TypeScript server offers neither declarations nor document links.
        expect(Object.keys(stub.providers).sort()).toEqual([
            'registerCompletionItemProvider',
            'registerDefinitionProvider',
            'registerDocumentHighlightProvider',
            'registerDocumentSymbolProvider',
            'registerFoldingRangeProvider',
            'registerHoverProvider',
            'registerImplementationProvider',
            'registerReferenceProvider',
            'registerSelectionRangeProvider',
            'registerSignatureHelpProvider',
            'registerTypeDefinitionProvider'
        ]);
        expect(stub.providers.registerHoverProvider.language).toBe('typescript');
    });

    it('registers every provider for a server that offers every feature', () => {
        startWithServer({
            ...answers.capabilities,
            declarationProvider: true,
            documentLinkProvider: { resolveProvider: true }
        });

        expect(Object.keys(stub.providers)).toHaveLength(13);
        expect(typeof stub.providers.registerLinkProvider.provider.resolveLink).toBe('function');
    });

    it('registers no provider for a feature missing from the capabilities', () => {
        startWithServer({ hoverProvider: true });

        expect(Object.keys(stub.providers)).toEqual(['registerHoverProvider']);
    });

    it('gives the completion provider the server\'s trigger characters and a resolve function', () => {
        startWithServer();

        const provider = stub.providers.registerCompletionItemProvider.provider;
        expect(provider.triggerCharacters).toEqual(answers.capabilities.completionProvider.triggerCharacters);
        expect(typeof provider.resolveCompletionItem).toBe('function');

        const signatureProvider = stub.providers.registerSignatureHelpProvider.provider;
        expect(signatureProvider.signatureHelpTriggerCharacters).toEqual(['(', ',', '<']);
        expect(signatureProvider.signatureHelpRetriggerCharacters).toEqual([')']);
    });

    it('asks for completions at the cursor, and resolves a suggestion with the server\'s own item', async () => {
        startWithServer();
        const provider = stub.providers.registerCompletionItemProvider.provider;

        const pending = provider.provideCompletionItems(
            model, { lineNumber: 9, column: 35 }, { triggerKind: 1, triggerCharacter: '.' });
        const request = answerRequest('textDocument/completion', answers.completion);
        const list = await pending;

        expect(request.params).toEqual({
            position: { line: 8, character: 34 },
            context: { triggerKind: 2, triggerCharacter: '.' }
        });
        expect(list.suggestions.map((suggestion) => suggestion.label)).toEqual(['radius']);

        const resolving = provider.resolveCompletionItem(list.suggestions[0]);
        const resolveRequest = answerRequest('completionItem/resolve', answers.completionResolve);
        const resolved = await resolving;

        expect(resolveRequest.params.label).toBe('radius');
        expect(resolveRequest.params.data.name).toBe('radius');
        expect(resolved.detail).toBe('(property) Wheel.radius: number');
    });

    it('asks for the hover at the cursor', async () => {
        startWithServer();

        const pending = stub.providers.registerHoverProvider.provider.provideHover(model, { lineNumber: 8, column: 23 });
        const request = answerRequest('textDocument/hover', answers.hover);
        const hover = await pending;

        expect(request.params).toEqual({ position: { line: 7, character: 22 } });
        expect(hover.contents[0].value).toContain('function area');
    });

    it('asks for signature help with Monaco\'s trigger', async () => {
        startWithServer();
        const provider = stub.providers.registerSignatureHelpProvider.provider;

        const pending = provider.provideSignatureHelp(
            model, { lineNumber: 8, column: 27 }, undefined, { triggerKind: 2, triggerCharacter: '(', isRetrigger: false });
        const request = answerRequest('textDocument/signatureHelp', answers.signatureHelp);
        const help = await pending;

        expect(request.params.context).toEqual({ triggerKind: 2, triggerCharacter: '(', isRetrigger: false });
        expect(help.value.signatures[0].label).toBe('area(circle: Circle): number');
    });

    it('returns a definition in the document under the model\'s URI, and leaves out one in another file', async () => {
        startWithServer();
        const provider = stub.providers.registerDefinitionProvider.provider;

        const own = provider.provideDefinition(model, { lineNumber: 7, column: 21 });
        answerRequest('textDocument/definition', answers.definitionOwn);
        const other = provider.provideDefinition(model, { lineNumber: 8, column: 23 });
        answerRequest('textDocument/definition', answers.definitionOther);

        expect((await own)[0].uri).toBe(model.uri);
        expect(await other).toEqual([]);
    });

    it('sends each location request under its own method', async () => {
        startWithServer({ ...answers.capabilities, declarationProvider: true });
        const position = { lineNumber: 9, column: 30 };

        const declaration = stub.providers.registerDeclarationProvider.provider.provideDeclaration(model, position);
        answerRequest('textDocument/declaration', answers.definitionOwn);
        const typeDefinition = stub.providers.registerTypeDefinitionProvider.provider.provideTypeDefinition(model, position);
        answerRequest('textDocument/typeDefinition', answers.typeDefinition);
        const implementation = stub.providers.registerImplementationProvider.provider.provideImplementation(model, position);
        answerRequest('textDocument/implementation', answers.implementation);

        expect(await declaration).toHaveLength(1);
        expect(await typeDefinition).toHaveLength(1);
        expect(await implementation).toHaveLength(1);
    });

    it('asks for references with Monaco\'s choice about the declaration', async () => {
        startWithServer();
        const provider = stub.providers.registerReferenceProvider.provider;

        const pending = provider.provideReferences(model, { lineNumber: 7, column: 8 }, { includeDeclaration: true });
        const request = answerRequest('textDocument/references', answers.references);
        const references = await pending;

        expect(request.params.context).toEqual({ includeDeclaration: true });
        expect(references).toHaveLength(answers.references.length);
    });

    it('returns null for no highlights, so Monaco highlights the word itself', async () => {
        startWithServer();
        const provider = stub.providers.registerDocumentHighlightProvider.provider;

        const empty = provider.provideDocumentHighlights(model, { lineNumber: 7, column: 8 });
        answerRequest('textDocument/documentHighlight', answers.documentHighlight);
        const found = provider.provideDocumentHighlights(model, { lineNumber: 7, column: 8 });
        answerRequest('textDocument/documentHighlight', [{
            range: { start: { line: 6, character: 6 }, end: { line: 6, character: 11 } },
            kind: 3
        }]);

        expect(await empty).toBeNull();
        expect((await found)[0].kind).toBe('Write');
    });

    it('asks for the document\'s symbols, folds, selection ranges and links', async () => {
        startWithServer({ ...answers.capabilities, documentLinkProvider: true });

        const symbols = stub.providers.registerDocumentSymbolProvider.provider.provideDocumentSymbols(model);
        answerRequest('textDocument/documentSymbol', answers.documentSymbol);
        const folds = stub.providers.registerFoldingRangeProvider.provider.provideFoldingRanges(model, {});
        answerRequest('textDocument/foldingRange', answers.foldingRange);
        const selections = stub.providers.registerSelectionRangeProvider.provider.provideSelectionRanges(
            model, [{ lineNumber: 9, column: 37 }]);
        const selectionRequest = answerRequest('textDocument/selectionRange', answers.selectionRange);
        const links = stub.providers.registerLinkProvider.provider.provideLinks(model);
        answerRequest('textDocument/documentLink', [{
            range: { start: { line: 0, character: 30 }, end: { line: 0, character: 38 } },
            target: 'file:///d%3A/Projects/Acme/src/shapes.ts'
        }]);

        expect((await symbols).some((symbol) => symbol.name === 'Wheel')).toBe(true);
        expect(await folds).toEqual([{ start: 3, end: 4 }]);
        expect(selectionRequest.params).toEqual({ positions: [{ line: 8, character: 36 }] });
        expect((await selections)[0].length).toBeGreaterThan(3);
        expect((await links).links[0].url).toBe('file:///d%3A/Projects/Acme/src/shapes.ts');
    });

    it('answers nothing for a model that is not the page\'s document', async () => {
        startWithServer();
        const otherModel = createModelStub({ filePath: 'D:\\Projects\\Acme\\src\\shapes.ts' });

        const hover = await stub.providers.registerHoverProvider.provider.provideHover(otherModel, { lineNumber: 1, column: 1 });

        expect(hover).toBeNull();
        expect(connections[0].sentWith('textDocument/hover')).toEqual([]);
    });

    it('returns an empty answer when the host closes the connection during a request', async () => {
        startWithServer();

        const pending = stub.providers.registerHoverProvider.provider.provideHover(model, { lineNumber: 8, column: 23 });
        connections[0].close();

        expect(await pending).toBeNull();
    });

    it('keeps the providers it has when a connection opens again', () => {
        startWithServer();
        const registrationCount = stub.registrations.length;

        connections[0].receive({ method: 'celbridge/serverReady', params: { capabilities: answers.capabilities } });

        expect(stub.registrations).toHaveLength(registrationCount);
    });
});

describe('LanguageFeatures diagnostics', () => {
    it('shows the document\'s diagnostics as the model\'s markers', () => {
        startWithServer();

        connections[0].receive({
            method: 'textDocument/publishDiagnostics',
            params: { diagnostics: answers.diagnostic.items }
        });

        const [markedModel, owner, markers] = stub.monaco.editor.setModelMarkers.mock.calls.at(-1);
        expect(markedModel).toBe(model);
        expect(owner).toBe('celbridge-language-server');
        expect(markers[0].message).toBe("Type 'number' is not assignable to type 'string'.");
    });

    it('removes the markers while the server is down', () => {
        startWithServer();

        connections[0].close();

        expect(stub.monaco.editor.setModelMarkers).toHaveBeenLastCalledWith(model, 'celbridge-language-server', []);
    });
});

describe('LanguageFeatures after a rename', () => {
    it('removes the providers and the markers when the document leaves its server', () => {
        startWithServer();
        const registrations = [...stub.registrations];

        features.stop();

        expect(registrations.every((registration) => registration.dispose.mock.calls.length === 1)).toBe(true);
        expect(stub.monaco.editor.setModelMarkers).toHaveBeenLastCalledWith(model, 'celbridge-language-server', []);
        expect(connections[0].readyState).toBe(3);
    });

    it('removes the providers when the host reports that no server will run', () => {
        startWithServer();
        const registrations = [...stub.registrations];

        connections[0].receive({ method: 'celbridge/serverUnavailable' });

        expect(registrations.every((registration) => registration.dispose.mock.calls.length === 1)).toBe(true);
    });

    it('treats a result that names the document\'s new URI as the page\'s own model', async () => {
        const renamedMetadata = {
            filePath: 'D:\\Projects\\Acme\\src\\renamed.ts',
            fileName: 'renamed.ts',
            languageServer: 'typescript'
        };
        startWithServer();

        // The model keeps the URI of main.ts, and the document is now renamed.ts.
        features.stop();
        features.prepare('typescript', renamedMetadata);
        features.start(model, renamedMetadata);
        connections[1].receive({ method: 'celbridge/serverReady', params: { capabilities: answers.capabilities } });

        const provider = stub.providers.registerDefinitionProvider.provider;
        const renamedAnswer = JSON.parse(JSON.stringify(answers.definitionOwn).replaceAll('main.ts', 'renamed.ts'));
        const underNewName = provider.provideDefinition(model, { lineNumber: 7, column: 21 });
        answerRequest('textDocument/definition', renamedAnswer);
        const underOldName = provider.provideDefinition(model, { lineNumber: 7, column: 21 });
        answerRequest('textDocument/definition', answers.definitionOwn);

        expect(connections).toHaveLength(2);
        expect(connections[1].sentWith('textDocument/didOpen')[0].params.text).toBe(recorded.mainText);
        expect((await underNewName)[0].uri).toBe(model.uri);
        expect(await underOldName).toEqual([]);
    });
});

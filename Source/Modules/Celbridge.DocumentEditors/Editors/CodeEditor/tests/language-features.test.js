import { describe, it, expect, beforeEach, vi } from 'vitest';

import { installMonacoLanguageStub, createModelStub, FakeChannel } from './fixtures/monaco-language-stub.js';
import { semanticTokenLegend } from '../js/editor-themes.js';
import { LanguageFeatures } from '../js/language-features.js';

// The TypeScript language server's capabilities and answers for main.ts of a two-file project.
import recorded from './fixtures/tsc-answers.json';

const answers = recorded.answers;

const mainPath = 'D:\\Projects\\Acme\\src\\main.ts';
const serverMetadata = { filePath: mainPath, fileName: 'main.ts', languageServer: 'typescript' };
const shapesUri = 'file:///d%3A/Projects/Acme/src/shapes.ts';
const shapesText = 'export function area(circle: Circle): number {\n    return 0;\n}\n';

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

// Answers a request with the given method once the page has sent it. A provider sends some requests only after the
// server's first answer arrives.
async function answerWhenSent(method, result) {
    const connection = connections[connections.length - 1];
    await vi.waitFor(() => {
        expect(connection.sentWith(method).length).toBeGreaterThan(0);
    });

    return answerRequest(method, result);
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
    it('turns off Monaco\'s own TypeScript and JavaScript features for a document with a server', () => {
        features.prepare('typescript', serverMetadata);

        const { typescriptDefaults, javascriptDefaults } = stub.monaco.typescript;
        expect(typescriptDefaults.setModeConfiguration).toHaveBeenCalledTimes(1);
        const configuration = typescriptDefaults.setModeConfiguration.mock.calls[0][0];
        expect(Object.values(configuration).every((value) => value === false)).toBe(true);
        expect(configuration.diagnostics).toBe(false);
        expect(javascriptDefaults.setModeConfiguration).toHaveBeenCalledWith(configuration);
    });

    it('turns off both languages\' features for a JavaScript document, since a peek can show either', () => {
        features.prepare('javascript', serverMetadata);

        const { typescriptDefaults, javascriptDefaults } = stub.monaco.typescript;
        expect(javascriptDefaults.setModeConfiguration).toHaveBeenCalledTimes(1);
        expect(typescriptDefaults.setModeConfiguration).toHaveBeenCalledTimes(1);
    });

    it('leaves Monaco\'s TypeScript features on for a document in another language with a server', () => {
        features.prepare('python', { ...serverMetadata, languageServer: 'python' });

        expect(stub.monaco.typescript.typescriptDefaults.setModeConfiguration).not.toHaveBeenCalled();
        expect(stub.monaco.typescript.javascriptDefaults.setModeConfiguration).not.toHaveBeenCalled();
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

        // The TypeScript server offers neither declarations nor document links. The page provides no code lenses.
        expect(Object.keys(stub.providers).sort()).toEqual([
            'registerCodeActionProvider',
            'registerCompletionItemProvider',
            'registerDefinitionProvider',
            'registerDocumentFormattingEditProvider',
            'registerDocumentHighlightProvider',
            'registerDocumentRangeFormattingEditProvider',
            'registerDocumentSemanticTokensProvider',
            'registerDocumentSymbolProvider',
            'registerFoldingRangeProvider',
            'registerHoverProvider',
            'registerImplementationProvider',
            'registerInlayHintsProvider',
            'registerReferenceProvider',
            'registerRenameProvider',
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

        expect(Object.keys(stub.providers)).toHaveLength(19);
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

    it('returns a definition in the document under the model\'s URI', async () => {
        startWithServer();
        const provider = stub.providers.registerDefinitionProvider.provider;

        const own = provider.provideDefinition(model, { lineNumber: 7, column: 21 });
        answerRequest('textDocument/definition', answers.definitionOwn);

        expect((await own)[0].uri).toBe(model.uri);
        expect(connections[0].sentWith('celbridge/fileText')).toEqual([]);
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

describe('LanguageFeatures inlay hints and semantic tokens', () => {
    it('asks for the inlay hints of Monaco\'s range', async () => {
        startWithServer();
        const provider = stub.providers.registerInlayHintsProvider.provider;
        const range = { startLineNumber: 1, startColumn: 1, endLineNumber: 11, endColumn: 1 };

        const pending = provider.provideInlayHints(model, range);
        const request = answerRequest('textDocument/inlayHint', answers.inlayHint);
        const { hints } = await pending;

        expect(request.params).toEqual({ range: { start: { line: 0, character: 0 }, end: { line: 10, character: 0 } } });
        expect(hints.map((hint) => hint.label)).toEqual([': number', 'circle:', ': number', 'circle:', '...data:']);
    });

    it('gives Monaco the page\'s legend, and the document\'s tokens in that legend', async () => {
        startWithServer();
        const provider = stub.providers.registerDocumentSemanticTokensProvider.provider;

        const pending = provider.provideDocumentSemanticTokens(model, null);
        answerRequest('textDocument/semanticTokens/full', answers.semanticTokens);
        const tokens = await pending;

        expect(provider.getLegend()).toBe(semanticTokenLegend);
        expect(tokens.data).toHaveLength(17 * 5);
        expect(typeof provider.releaseDocumentSemanticTokens).toBe('function');
    });

    it('registers no semantic tokens provider for a server without whole-document tokens', () => {
        startWithServer({
            ...answers.capabilities,
            semanticTokensProvider: { ...answers.capabilities.semanticTokensProvider, full: false }
        });

        expect(stub.providers.registerDocumentSemanticTokensProvider).toBeUndefined();
    });

    it('answers nothing for a model that is not the page\'s document', async () => {
        startWithServer();
        const otherModel = createModelStub({ filePath: 'D:\\Projects\\Acme\\src\\shapes.ts' });
        const range = { startLineNumber: 1, startColumn: 1, endLineNumber: 2, endColumn: 1 };

        expect(await stub.providers.registerInlayHintsProvider.provider.provideInlayHints(otherModel, range)).toBeNull();
        expect(await stub.providers.registerDocumentSemanticTokensProvider.provider
            .provideDocumentSemanticTokens(otherModel, null)).toBeNull();
        expect(connections[0].sent).toHaveLength(1);
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

        expect(connections).toHaveLength(2);
        expect(connections[1].sentWith('textDocument/didOpen')[0].params.text).toBe(recorded.mainText);
        expect((await underNewName)[0].uri).toBe(model.uri);
    });
});

describe('LanguageFeatures locations in other files', () => {
    it('gives a location in another file a copy of the file\'s text, for a peek', async () => {
        startWithServer();
        const provider = stub.providers.registerDefinitionProvider.provider;

        const pending = provider.provideDefinition(model, { lineNumber: 8, column: 23 });
        answerRequest('textDocument/definition', answers.definitionOther);
        const fileTextRequest = await answerWhenSent('celbridge/fileText', { stamp: '1-60', text: shapesText });
        const [location] = await pending;

        expect(fileTextRequest.params).toEqual({ uri: shapesUri });
        expect(location.uri.toString()).toBe(shapesUri);
        expect(location.range).toEqual({ startLineNumber: 10, startColumn: 1, endLineNumber: 12, endColumn: 2 });
        expect(stub.models.get(shapesUri).text).toBe(shapesText);
    });

    it('sends the copy\'s stamp, and keeps the copy when the host reports that the file is unchanged', async () => {
        startWithServer();
        const provider = stub.providers.registerDefinitionProvider.provider;

        const first = provider.provideDefinition(model, { lineNumber: 8, column: 23 });
        answerRequest('textDocument/definition', answers.definitionOther);
        await answerWhenSent('celbridge/fileText', { stamp: '1-60', text: shapesText });
        await first;

        const second = provider.provideDefinition(model, { lineNumber: 8, column: 23 });
        answerRequest('textDocument/definition', answers.definitionOther);
        await vi.waitFor(() => {
            expect(connections[0].sentWith('celbridge/fileText')).toHaveLength(2);
        });
        const secondRequest = answerRequest('celbridge/fileText', { stamp: '1-60' });
        await second;

        expect(secondRequest.params).toEqual({ uri: shapesUri, stamp: '1-60' });
        expect(stub.monaco.editor.createModel).toHaveBeenCalledTimes(1);
        expect(stub.models.get(shapesUri).text).toBe(shapesText);
    });

    it('gives each other file in a references answer a copy of its text', async () => {
        startWithServer();
        const provider = stub.providers.registerReferenceProvider.provider;

        const pending = provider.provideReferences(model, { lineNumber: 8, column: 23 }, { includeDeclaration: true });
        answerRequest('textDocument/references', answers.referencesOther);
        await answerWhenSent('celbridge/fileText', { stamp: '1-60', text: shapesText });
        const references = await pending;

        expect(references).toHaveLength(answers.referencesOther.length);
        expect(references.filter((reference) => reference.uri === model.uri)).toHaveLength(3);
        expect(connections[0].sentWith('celbridge/fileText')).toHaveLength(1);
    });

    it('removes the copies when the document leaves its server', async () => {
        startWithServer();
        const pending = stub.providers.registerDefinitionProvider.provider.provideDefinition(
            model, { lineNumber: 8, column: 23 });
        answerRequest('textDocument/definition', answers.definitionOther);
        await answerWhenSent('celbridge/fileText', { stamp: '1-60', text: shapesText });
        await pending;
        const peekModel = stub.models.get(shapesUri);

        features.stop();

        expect(peekModel.dispose).toHaveBeenCalledTimes(1);
    });

    it('asks the host to open another file at the location', async () => {
        startWithServer();

        const opening = stub.opener.openCodeEditor(
            {},
            stub.monaco.Uri.parse(shapesUri),
            { startLineNumber: 10, startColumn: 17, endLineNumber: 10, endColumn: 21 });
        const request = answerRequest('window/showDocument', { success: true });

        expect(request.params).toEqual({
            uri: shapesUri,
            takeFocus: true,
            selection: { start: { line: 9, character: 16 }, end: { line: 9, character: 20 } }
        });
        expect(await opening).toBe(true);
    });

    it('opens nothing when the host refuses the file, and leaves the page\'s own document to Monaco', async () => {
        startWithServer();

        const refused = stub.opener.openCodeEditor({}, stub.monaco.Uri.parse(shapesUri), { lineNumber: 1, column: 1 });
        const request = answerRequest('window/showDocument', { success: false });
        const own = await stub.opener.openCodeEditor({}, model.uri, { lineNumber: 1, column: 1 });

        expect(request.params.selection).toEqual({ start: { line: 0, character: 0 }, end: { line: 0, character: 0 } });
        expect(await refused).toBe(false);
        expect(own).toBe(false);
        expect(connections[0].sentWith('window/showDocument')).toHaveLength(1);
    });
});

describe('LanguageFeatures edits', () => {
    it('hands a rename\'s edit to the host, and gives Monaco an empty edit', async () => {
        startWithServer();
        const provider = stub.providers.registerRenameProvider.provider;

        const pending = provider.provideRenameEdits(model, { lineNumber: 9, column: 35 }, 'size');
        const renameRequest = answerRequest('textDocument/rename', answers.rename);
        const applyRequest = await answerWhenSent('workspace/applyEdit', { applied: true });
        const result = await pending;

        expect(renameRequest.params).toEqual({ position: { line: 8, character: 34 }, newName: 'size' });
        expect(applyRequest.params).toEqual({ edit: answers.rename });
        expect(result).toEqual({ edits: [] });
    });

    it('rejects the rename when the server finds nothing to rename or the host cannot apply the edit', async () => {
        startWithServer();
        const provider = stub.providers.registerRenameProvider.provider;

        const nothing = provider.provideRenameEdits(model, { lineNumber: 1, column: 1 }, 'size');
        answerRequest('textDocument/rename', null);
        const failed = provider.provideRenameEdits(model, { lineNumber: 9, column: 35 }, 'size');
        answerRequest('textDocument/rename', answers.rename);
        await answerWhenSent('workspace/applyEdit', { applied: false, failureReason: 'The document changed' });

        expect((await nothing).rejectReason).toBe('CodeEditor_Rename_Refused');
        expect((await failed).rejectReason).toBe('CodeEditor_Rename_Failed');
    });

    it('asks the server where a rename starts', async () => {
        startWithServer();
        const provider = stub.providers.registerRenameProvider.provider;

        const pending = provider.resolveRenameLocation(model, { lineNumber: 9, column: 35 });
        const request = answerRequest('textDocument/prepareRename', answers.prepareRename);
        const location = await pending;

        expect(request.params).toEqual({ position: { line: 8, character: 34 } });
        expect(location).toEqual({
            range: { startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 41 },
            text: 'radius'
        });
    });

    it('sends a code action request with the server\'s own diagnostics, and offers the server\'s actions', async () => {
        startWithServer();
        connections[0].receive({
            method: 'textDocument/publishDiagnostics',
            params: { diagnostics: [answers.codeActionDiagnostic] }
        });
        const registration = stub.providers.registerCodeActionProvider;
        const marker = { startLineNumber: 8, startColumn: 7, endLineNumber: 8, endColumn: 11, message: answers.codeActionDiagnostic.message };

        const pending = registration.provider.provideCodeActions(
            model,
            { startLineNumber: 8, startColumn: 7, endLineNumber: 8, endColumn: 11 },
            { markers: [marker], trigger: 1, only: 'source.organizeImports' });
        const request = answerRequest('textDocument/codeAction', answers.organizeImports);
        const list = await pending;

        expect(registration.metadata.providedCodeActionKinds).toEqual(answers.capabilities.codeActionProvider.codeActionKinds);
        expect(request.params.context).toEqual({
            diagnostics: [answers.codeActionDiagnostic],
            triggerKind: 1,
            only: ['source.organizeImports']
        });
        expect(list.actions.map((action) => action.title)).toEqual(['Organize Imports']);
        expect(list.actions[0].command.id).toBe('celbridge.applyCodeAction');
        expect(list.actions[0].edit).toBeUndefined();
    });

    it('hands the chosen code action\'s edit to the host', async () => {
        startWithServer();
        const [action] = answers.organizeImports;

        const applying = stub.commands['celbridge.applyCodeAction']({}, action);
        const request = answerRequest('workspace/applyEdit', { applied: true });
        await applying;

        expect(request.params).toEqual({ edit: action.edit });
    });

    it('asks the server to fill in a code action\'s edit when the server resolves actions', async () => {
        startWithServer({
            ...answers.capabilities,
            codeActionProvider: { ...answers.capabilities.codeActionProvider, resolveProvider: true }
        });
        const [organizeImports] = answers.organizeImports;
        const unresolved = { title: organizeImports.title, kind: organizeImports.kind, data: { id: 1 } };

        const applying = stub.commands['celbridge.applyCodeAction']({}, unresolved);
        const resolveRequest = answerRequest('codeAction/resolve', organizeImports);
        const applyRequest = await answerWhenSent('workspace/applyEdit', { applied: true });
        await applying;

        expect(resolveRequest.params).toEqual(unresolved);
        expect(applyRequest.params).toEqual({ edit: organizeImports.edit });
    });

    it('returns the server\'s formatting edits for the document and for a range', async () => {
        model = createModelStub({ filePath: mainPath, text: recorded.formatText });
        startWithServer();
        const options = { tabSize: 4, insertSpaces: true };

        const documentEdits = stub.providers.registerDocumentFormattingEditProvider.provider
            .provideDocumentFormattingEdits(model, options);
        const documentRequest = answerRequest('textDocument/formatting', answers.formatting);
        const rangeEdits = stub.providers.registerDocumentRangeFormattingEditProvider.provider
            .provideDocumentRangeFormattingEdits(
                model, { startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 25 }, options);
        const rangeRequest = answerRequest('textDocument/rangeFormatting', answers.rangeFormatting);

        expect(documentRequest.params).toEqual({ options });
        expect(rangeRequest.params.range).toEqual({ start: { line: 0, character: 0 }, end: { line: 0, character: 24 } });
        expect((await documentEdits)).toHaveLength(answers.formatting.length);
        expect((await rangeEdits)[0]).toEqual({
            range: { startLineNumber: 1, startColumn: 16, endLineNumber: 1, endColumn: 19 },
            text: ' '
        });
    });
});

import { describe, it, expect, beforeEach } from 'vitest';

import { installMonacoLanguageStub } from './fixtures/monaco-language-stub.js';
import { semanticTokenLegend } from '../js/editor-themes.js';
import * as convert from '../js/lsp-convert.js';

// The TypeScript language server's answers for main.ts of a two-file project.
import recorded from './fixtures/tsc-answers.json';

const answers = recorded.answers;

const mainUri = 'file:///d%3A/Projects/Acme/src/main.ts';

// Keeps the locations in main.ts under the model's name, and leaves the other files out.
const resolveMainUri = (uri) => (uri === mainUri ? 'the model' : null);

beforeEach(() => {
    installMonacoLanguageStub();
});

describe('positions and ranges', () => {
    it('counts lines and characters from zero for the server, and from one for Monaco', () => {
        expect(convert.toLspPosition({ lineNumber: 8, column: 22 })).toEqual({ line: 7, character: 21 });

        const range = convert.toMonacoRange({ start: { line: 7, character: 6 }, end: { line: 7, character: 10 } });
        expect(range).toEqual({ startLineNumber: 8, startColumn: 7, endLineNumber: 8, endColumn: 11 });
        expect(convert.toLspRange(range)).toEqual({ start: { line: 7, character: 6 }, end: { line: 7, character: 10 } });
        expect(convert.toMonacoPosition({ line: 7, character: 21 })).toEqual({ lineNumber: 8, column: 22 });
    });
});

describe('toMarkers', () => {
    it('turns the recorded type error into an error marker on the name', () => {
        const markers = convert.toMarkers(answers.diagnostic.items);

        expect(markers).toEqual([{
            startLineNumber: 8,
            startColumn: 7,
            endLineNumber: 8,
            endColumn: 11,
            severity: 'Error',
            message: "Type 'number' is not assignable to type 'string'.",
            source: 'ts',
            code: '2322'
        }]);
    });

    it('carries the severity, tags, code link and related information of a diagnostic', () => {
        const markers = convert.toMarkers([{
            range: { start: { line: 0, character: 0 }, end: { line: 0, character: 3 } },
            severity: 4,
            message: 'Unused',
            code: 'unused',
            codeDescription: { href: 'https://example.com/rules/unused' },
            tags: [1, 2],
            relatedInformation: [{
                location: { uri: mainUri, range: { start: { line: 2, character: 0 }, end: { line: 2, character: 4 } } },
                message: 'Declared here'
            }]
        }]);

        expect(markers[0].severity).toBe('Hint');
        expect(markers[0].tags).toEqual(['Unnecessary', 'Deprecated']);
        expect(markers[0].code.value).toBe('unused');
        expect(markers[0].code.target.toString()).toBe('https://example.com/rules/unused');
        expect(markers[0].relatedInformation[0].message).toBe('Declared here');
        expect(markers[0].relatedInformation[0].startLineNumber).toBe(3);
    });

    it('returns no markers for no diagnostics', () => {
        expect(convert.toMarkers(undefined)).toEqual([]);
    });
});

describe('toCompletionList', () => {
    const wordRange = { startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 35 };

    it('turns the recorded member completion into a suggestion with the list\'s default ranges', () => {
        const list = convert.toCompletionList(answers.completion, wordRange);

        expect(list.incomplete).toBe(false);
        expect(list.suggestions).toHaveLength(1);

        const suggestion = list.suggestions[0];
        expect(suggestion.label).toBe('radius');
        expect(suggestion.kind).toBe('Field');
        expect(suggestion.insertText).toBe('radius');
        expect(suggestion.sortText).toBe('11');
        expect(suggestion.commitCharacters).toEqual(['.', ',', ';']);
        expect(suggestion.range).toEqual({
            insert: { startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 35 },
            replace: { startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 41 }
        });
        expect(suggestion.lspItem.data.name).toBe('radius');
    });

    it('uses the word at the cursor for an item with no range', () => {
        const list = convert.toCompletionList([{ label: 'alpha' }], wordRange);

        expect(list.suggestions[0].range).toBe(wordRange);
        expect(list.suggestions[0].kind).toBe('Text');
        expect(list.incomplete).toBe(false);
    });

    it('takes an item\'s own edit, snippet format, deprecation and extra edits', () => {
        const list = convert.toCompletionList({
            isIncomplete: true,
            items: [{
                label: 'log',
                labelDetails: { detail: '(message)', description: 'console' },
                kind: 2,
                insertTextFormat: 2,
                tags: [1],
                documentation: { kind: 'markdown', value: '**Logs** a message.' },
                textEdit: {
                    range: { start: { line: 1, character: 2 }, end: { line: 1, character: 4 } },
                    newText: 'log($1)'
                },
                additionalTextEdits: [{
                    range: { start: { line: 0, character: 0 }, end: { line: 0, character: 0 } },
                    newText: "import { log } from './log';\n"
                }]
            }]
        }, wordRange);

        const suggestion = list.suggestions[0];
        expect(list.incomplete).toBe(true);
        expect(suggestion.label).toEqual({ label: 'log', detail: '(message)', description: 'console' });
        expect(suggestion.kind).toBe('Method');
        expect(suggestion.insertText).toBe('log($1)');
        expect(suggestion.insertTextRules).toBe('InsertAsSnippet');
        expect(suggestion.tags).toEqual(['Deprecated']);
        expect(suggestion.documentation).toEqual({ value: '**Logs** a message.' });
        expect(suggestion.range).toEqual({ startLineNumber: 2, startColumn: 3, endLineNumber: 2, endColumn: 5 });
        expect(suggestion.additionalTextEdits).toEqual([{
            range: { startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 1 },
            text: "import { log } from './log';\n"
        }]);
    });

    it('returns no suggestions for no answer', () => {
        expect(convert.toCompletionList(null, wordRange)).toEqual({ suggestions: [] });
    });

    it('numbers the trigger kinds from one for the server', () => {
        expect(convert.toCompletionContext({ triggerKind: 1, triggerCharacter: '.' }))
            .toEqual({ triggerKind: 2, triggerCharacter: '.' });
        expect(convert.toCompletionContext({ triggerKind: 0 })).toEqual({ triggerKind: 1 });
    });
});

describe('applyResolvedCompletionItem', () => {
    it('adds the recorded detail of the resolved item to the suggestion', () => {
        const suggestion = convert.toCompletionList(answers.completion, null).suggestions[0];

        convert.applyResolvedCompletionItem(suggestion, answers.completionResolve);

        expect(suggestion.detail).toBe('(property) Wheel.radius: number');
    });

    it('leaves the suggestion as it is when the request fails', () => {
        const suggestion = { label: 'radius', detail: 'before' };

        expect(convert.applyResolvedCompletionItem(suggestion, null)).toBe(suggestion);
        expect(suggestion.detail).toBe('before');
    });
});

describe('toHover', () => {
    it('turns the recorded hover into markdown over the name', () => {
        const hover = convert.toHover(answers.hover);

        expect(hover.contents).toHaveLength(1);
        expect(hover.contents[0].value).toContain('function area(circle: Circle): number');
        expect(hover.contents[0].value).toContain('Returns the area of a circle.');
        expect(hover.range).toEqual({ startLineNumber: 8, startColumn: 22, endLineNumber: 8, endColumn: 26 });
    });

    it('writes a marked string with a language as a block of code', () => {
        const hover = convert.toHover({ contents: [{ language: 'python', value: 'x: int' }, 'A number.'] });

        expect(hover.contents).toEqual([{ value: '```python\nx: int\n```' }, { value: 'A number.' }]);
    });

    it('returns null for no answer and for empty contents', () => {
        expect(convert.toHover(null)).toBeNull();
        expect(convert.toHover({ contents: { kind: 'markdown', value: '' } })).toBeNull();
    });
});

describe('toSignatureHelp', () => {
    it('turns the recorded signature into Monaco\'s signature with its parameter', () => {
        const help = convert.toSignatureHelp(answers.signatureHelp);

        expect(help.value.activeSignature).toBe(0);
        expect(help.value.activeParameter).toBe(0);
        expect(help.value.signatures).toEqual([{
            label: 'area(circle: Circle): number',
            documentation: { value: 'Returns the area of a circle.' },
            parameters: [{ label: 'circle: Circle', documentation: { value: 'The circle to measure.' } }],
            activeParameter: 0
        }]);
        expect(typeof help.dispose).toBe('function');
    });

    it('returns null for no signatures', () => {
        expect(convert.toSignatureHelp({ signatures: [] })).toBeNull();
        expect(convert.toSignatureHelp(null)).toBeNull();
    });
});

describe('toLocations', () => {
    it('turns the recorded definition in the document into a link under the model\'s name', () => {
        const locations = convert.toLocations(answers.definitionOwn, resolveMainUri);

        expect(locations).toEqual([{
            uri: 'the model',
            originSelectionRange: { startLineNumber: 7, startColumn: 19, endLineNumber: 7, endColumn: 24 },
            range: { startLineNumber: 3, startColumn: 1, endLineNumber: 5, endColumn: 2 },
            targetSelectionRange: { startLineNumber: 3, startColumn: 7, endLineNumber: 3, endColumn: 12 }
        }]);
    });

    it('leaves out the recorded definition in another file', () => {
        expect(convert.toLocations(answers.definitionOther, resolveMainUri)).toEqual([]);
    });

    it('turns the recorded type definition and implementation into links', () => {
        expect(convert.toLocations(answers.typeDefinition, resolveMainUri)[0].targetSelectionRange.startLineNumber).toBe(3);
        expect(convert.toLocations(answers.implementation, resolveMainUri)[0].range.startLineNumber).toBe(3);
    });

    it('keeps the recorded references in the document, and leaves out the one in another file', () => {
        const locations = convert.toLocations(answers.referencesOther, resolveMainUri);

        expect(answers.referencesOther).toHaveLength(4);
        expect(locations.map((location) => location.range.startLineNumber)).toEqual([1, 8, 9]);
        expect(locations.every((location) => location.uri === 'the model')).toBe(true);
    });

    it('takes one location that is not in a list, as a declaration answer may be', () => {
        const locations = convert.toLocations(
            { uri: mainUri, range: { start: { line: 2, character: 6 }, end: { line: 2, character: 11 } } },
            resolveMainUri);

        expect(locations).toEqual([{
            uri: 'the model',
            range: { startLineNumber: 3, startColumn: 7, endLineNumber: 3, endColumn: 12 }
        }]);
    });

    it('returns no locations for no answer', () => {
        expect(convert.toLocations(null, resolveMainUri)).toEqual([]);
    });
});

describe('toDocumentHighlights', () => {
    it('gives each highlight its kind', () => {
        const range = { start: { line: 6, character: 6 }, end: { line: 6, character: 11 } };
        const highlights = convert.toDocumentHighlights([{ range, kind: 3 }, { range, kind: 2 }, { range }]);

        expect(highlights.map((highlight) => highlight.kind)).toEqual(['Write', 'Read', 'Text']);
        expect(highlights[0].range.startLineNumber).toBe(7);
    });

    it('returns no highlights for the recorded empty answer', () => {
        expect(convert.toDocumentHighlights(answers.documentHighlight)).toEqual([]);
    });
});

describe('toDocumentSymbols', () => {
    it('turns the recorded symbols into a tree, with the class\'s field under the class', () => {
        const symbols = convert.toDocumentSymbols(answers.documentSymbol);

        const wheelClass = symbols.find((symbol) => symbol.name === 'Wheel');
        expect(wheelClass.kind).toBe('Class');
        expect(wheelClass.range).toEqual({ startLineNumber: 3, startColumn: 1, endLineNumber: 5, endColumn: 2 });
        expect(wheelClass.selectionRange).toEqual({ startLineNumber: 3, startColumn: 7, endLineNumber: 3, endColumn: 12 });
        expect(wheelClass.children.map((child) => child.name)).toEqual(['radius']);
        expect(wheelClass.children[0].kind).toBe('Property');
        expect(symbols.find((symbol) => symbol.name === 'total').kind).toBe('Variable');
    });

    it('takes a flat list of symbol information', () => {
        const symbols = convert.toDocumentSymbols([{
            name: 'area',
            kind: 12,
            containerName: 'shapes',
            deprecated: true,
            location: { uri: mainUri, range: { start: { line: 9, character: 0 }, end: { line: 11, character: 1 } } }
        }]);

        expect(symbols[0].kind).toBe('Function');
        expect(symbols[0].detail).toBe('shapes');
        expect(symbols[0].tags).toEqual(['Deprecated']);
        expect(symbols[0].selectionRange).toEqual(symbols[0].range);
    });
});

describe('toFoldingRanges', () => {
    it('turns the recorded fold of the class body into lines counted from one', () => {
        expect(convert.toFoldingRanges(answers.foldingRange)).toEqual([{ start: 3, end: 4 }]);
    });

    it('gives a fold its kind', () => {
        const ranges = convert.toFoldingRanges([{ startLine: 0, endLine: 2, kind: 'imports' }]);

        expect(ranges).toEqual([{ start: 1, end: 3, kind: 'Imports' }]);
    });
});

describe('toSelectionRanges', () => {
    it('lists the recorded ranges for the position from the innermost outwards', () => {
        const ranges = convert.toSelectionRanges(answers.selectionRange);

        expect(ranges).toHaveLength(1);
        expect(ranges[0][0].range).toEqual({ startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 41 });
        expect(ranges[0][1].range).toEqual({ startLineNumber: 9, startColumn: 29, endLineNumber: 9, endColumn: 41 });
        expect(ranges[0].length).toBeGreaterThan(3);
    });
});

describe('toLinks', () => {
    it('turns each document link into Monaco\'s link, and keeps the server\'s link for the resolve request', () => {
        const documentLink = {
            range: { start: { line: 0, character: 30 }, end: { line: 0, character: 38 } },
            target: 'file:///d%3A/Projects/Acme/src/shapes.ts',
            tooltip: 'Open shapes.ts'
        };

        const links = convert.toLinks([documentLink]);

        expect(links.links).toEqual([{
            range: { startLineNumber: 1, startColumn: 31, endLineNumber: 1, endColumn: 39 },
            url: 'file:///d%3A/Projects/Acme/src/shapes.ts',
            tooltip: 'Open shapes.ts',
            lspLink: documentLink
        }]);
    });
});

describe('toRenameLocation', () => {
    const position = { lineNumber: 9, column: 35 };
    const model = { getValueInRange: () => 'radius' };

    it('reads a range with a placeholder, and a bare range from the model\'s text', () => {
        const withPlaceholder = convert.toRenameLocation(answers.prepareRename, model, position, 'refused');
        const bareRange = convert.toRenameLocation(answers.prepareRename.range, model, position, 'refused');

        expect(withPlaceholder.text).toBe('radius');
        expect(bareRange).toEqual({
            range: { startLineNumber: 9, startColumn: 35, endLineNumber: 9, endColumn: 41 },
            text: 'radius'
        });
    });

    it('rejects the rename when the server refuses, and leaves the default behavior to Monaco', () => {
        const refused = convert.toRenameLocation(null, model, position, 'refused');

        expect(refused.rejectReason).toBe('refused');
        expect(refused.range.startLineNumber).toBe(9);
        expect(convert.toRenameLocation({ defaultBehavior: true }, model, position, 'refused')).toBeNull();
    });
});

describe('findDiagnostics', () => {
    it('returns the server\'s diagnostic for each marker that matches one by range and message', () => {
        const diagnostic = answers.codeActionDiagnostic;
        const matching = { startLineNumber: 8, startColumn: 7, endLineNumber: 8, endColumn: 11, message: diagnostic.message };
        const otherMessage = { ...matching, message: 'Another message.' };

        expect(convert.findDiagnostics([matching, otherMessage], [diagnostic])).toEqual([diagnostic]);
        expect(convert.findDiagnostics(undefined, [diagnostic])).toEqual([]);
    });
});

describe('toCodeActionList', () => {
    const edit = answers.organizeImports[0].edit;

    it('runs the page\'s command with the server\'s action, and gives Monaco no edit', () => {
        const disabled = { title: 'Extract', kind: 'refactor.extract', edit, disabled: { reason: 'Select an expression' } };

        const list = convert.toCodeActionList([answers.organizeImports[0], disabled], 'apply', false);

        expect(list.actions).toHaveLength(2);
        expect(list.actions[0]).toEqual({
            title: 'Organize Imports',
            kind: 'source.organizeImports',
            isPreferred: false,
            command: { id: 'apply', title: 'Organize Imports', arguments: [answers.organizeImports[0]] }
        });
        expect(list.actions[1].disabled).toBe('Select an expression');
    });

    it('leaves out commands, and an action without an edit unless the server resolves actions', () => {
        const bareCommand = { title: 'Run', command: 'acme.run' };
        const actionWithCommand = { title: 'Fix and run', edit, command: { title: 'Run', command: 'acme.run' } };
        const resolvable = { title: 'Fix later', kind: 'quickfix', data: { id: 1 } };

        const items = [bareCommand, actionWithCommand, resolvable];

        expect(convert.toCodeActionList(items, 'apply', false).actions).toEqual([]);
        expect(convert.toCodeActionList(items, 'apply', true).actions.map((action) => action.title)).toEqual(['Fix later']);
    });
});

describe('toInlayHints', () => {
    // Recorded with every kind of hint on, since main.ts passes no literal argument.
    it('turns a label made of parts into plain text, with the hint\'s kind and padding', () => {
        const { hints } = convert.toInlayHints(answers.inlayHint);

        expect(hints).toHaveLength(5);
        expect(hints[0]).toEqual({
            label: ': number',
            position: { lineNumber: 4, column: 11 },
            paddingLeft: true,
            paddingRight: false,
            kind: 'Type'
        });
        expect(hints[1]).toEqual({
            label: 'circle:',
            position: { lineNumber: 8, column: 27 },
            paddingLeft: false,
            paddingRight: true,
            kind: 'Parameter'
        });
    });

    it('keeps a label written as a string, a tooltip and the hint\'s edits', () => {
        const hint = {
            position: { line: 0, character: 5 },
            label: '= 0',
            tooltip: { kind: 'markdown', value: 'The value of **Red**' },
            textEdits: [{ range: { start: { line: 0, character: 5 }, end: { line: 0, character: 5 } }, newText: ' = 0' }]
        };

        const [inlayHint] = convert.toInlayHints([hint]).hints;

        expect(inlayHint.label).toBe('= 0');
        expect(inlayHint.kind).toBeUndefined();
        expect(inlayHint.tooltip).toEqual({ value: 'The value of **Red**' });
        expect(inlayHint.textEdits).toEqual([{
            range: { startLineNumber: 1, startColumn: 6, endLineNumber: 1, endColumn: 6 },
            text: ' = 0'
        }]);
    });

    it('returns no hints for an empty answer', () => {
        expect(convert.toInlayHints(null).hints).toEqual([]);
    });
});

describe('toSemanticTokens', () => {
    const legend = answers.capabilities.semanticTokensProvider.legend;

    // Returns each token of Monaco's data at its line and character, with the name of the page's token type.
    function decode(data) {
        const tokens = [];
        let line = 0;
        let character = 0;
        for (let index = 0; index < data.length; index += 5) {
            line += data[index];
            character = data[index] === 0 ? character + data[index + 1] : data[index + 1];
            tokens.push([line, character, data[index + 2], semanticTokenLegend.tokenTypes[data[index + 3]], data[index + 4]]);
        }

        return tokens;
    }

    it('gives each recorded token the page\'s type for its color, at the same place', () => {
        const { data } = convert.toSemanticTokens(answers.semanticTokens, legend);

        expect(data).toBeInstanceOf(Uint32Array);
        const tokens = decode(data);
        expect(tokens).toHaveLength(17);
        expect(tokens.slice(0, 4)).toEqual([
            [2, 6, 5, 'semantic-type', 0],
            [2, 23, 6, 'semantic-type', 0],
            [3, 4, 6, 'semantic-variable', 0],
            [6, 6, 5, 'semantic-constant', 0]
        ]);
        expect(tokens[6]).toEqual([7, 21, 4, 'semantic-function', 0]);
        expect(tokens[13]).toEqual([9, 0, 7, 'semantic-variable', 0]);
        expect(tokens[14]).toEqual([9, 8, 3, 'semantic-function', 0]);
    });

    it('leaves out a token of a type without a color, and keeps the next token in place', () => {
        const keyword = legend.tokenTypes.indexOf('keyword');
        const variable = legend.tokenTypes.indexOf('variable');
        const enumMember = legend.tokenTypes.indexOf('enumMember');
        const data = [
            0, 0, 6, keyword, 0,
            0, 7, 5, variable, 0,
            1, 2, 3, keyword, 0,
            0, 4, 3, enumMember, 0
        ];

        const tokens = decode(convert.toSemanticTokens({ data }, legend).data);

        expect(tokens).toEqual([
            [0, 7, 5, 'semantic-variable', 0],
            [1, 6, 3, 'semantic-constant', 0]
        ]);
    });

    it('returns no tokens for an empty answer', () => {
        expect(convert.toSemanticTokens({ data: [] }, legend).data).toHaveLength(0);
    });
});

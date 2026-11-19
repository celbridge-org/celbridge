// Converts between the Language Server Protocol's types and Monaco's types. A language server counts lines and
// characters from zero, and Monaco counts line numbers and columns from one.
//
// The module expects the global `monaco` AMD namespace to be loaded before any function is called.

// The protocol numbers these kinds from one, in this order.
const completionItemKindNames = [
    'Text', 'Method', 'Function', 'Constructor', 'Field', 'Variable', 'Class', 'Interface', 'Module', 'Property',
    'Unit', 'Value', 'Enum', 'Keyword', 'Snippet', 'Color', 'File', 'Reference', 'Folder', 'EnumMember', 'Constant',
    'Struct', 'Event', 'Operator', 'TypeParameter'
];

const symbolKindNames = [
    'File', 'Module', 'Namespace', 'Package', 'Class', 'Method', 'Property', 'Field', 'Constructor', 'Enum',
    'Interface', 'Function', 'Variable', 'Constant', 'String', 'Number', 'Boolean', 'Array', 'Object', 'Key', 'Null',
    'EnumMember', 'Struct', 'Event', 'Operator', 'TypeParameter'
];

const markerSeverityNames = ['Error', 'Warning', 'Info', 'Hint'];
const highlightKindNames = ['Text', 'Read', 'Write'];

const foldingKindNames = {
    comment: 'Comment',
    imports: 'Imports',
    region: 'Region'
};

const snippetTextFormat = 2;
const deprecatedTag = 1;

export function toLspPosition(position) {
    return { line: position.lineNumber - 1, character: position.column - 1 };
}

export function toMonacoRange(range) {
    return {
        startLineNumber: range.start.line + 1,
        startColumn: range.start.character + 1,
        endLineNumber: range.end.line + 1,
        endColumn: range.end.character + 1
    };
}

export function toLspRange(range) {
    return {
        start: { line: range.startLineNumber - 1, character: range.startColumn - 1 },
        end: { line: range.endLineNumber - 1, character: range.endColumn - 1 }
    };
}

// Returns Monaco's markdown for documentation written as a string, as marked strings or as markup content.
export function toMarkdown(content) {
    if (content === null || content === undefined) {
        return undefined;
    }

    if (typeof content === 'string') {
        return { value: content };
    }

    // A marked string with a language is a block of code.
    if (typeof content.language === 'string') {
        return { value: '```' + content.language + '\n' + content.value + '\n```' };
    }

    if (content.kind === 'plaintext') {
        return { value: escapeMarkdown(content.value ?? '') };
    }

    return { value: content.value ?? '' };
}

function escapeMarkdown(text) {
    return text.replace(/[\\`*_{}[\]()#+\-.!|<>]/g, '\\$&');
}

// Returns Monaco's form of an item's documentation: a plain string stays a string, and markup becomes markdown.
function toDocumentation(documentation) {
    if (documentation === null || documentation === undefined) {
        return undefined;
    }

    if (typeof documentation === 'string') {
        return documentation;
    }

    if (documentation.kind === 'plaintext') {
        return documentation.value ?? '';
    }

    return { value: documentation.value ?? '' };
}

export function toMarkers(diagnostics) {
    return (diagnostics ?? []).map((diagnostic) => {
        const severityName = markerSeverityNames[(diagnostic.severity ?? 1) - 1] ?? 'Error';

        const marker = {
            ...toMonacoRange(diagnostic.range),
            severity: monaco.MarkerSeverity[severityName],
            message: diagnostic.message ?? '',
            source: diagnostic.source
        };

        if (diagnostic.code !== undefined && diagnostic.code !== null) {
            const code = String(diagnostic.code);
            const href = diagnostic.codeDescription?.href;
            marker.code = href ? { value: code, target: monaco.Uri.parse(href) } : code;
        }

        const tags = [];
        for (const tag of diagnostic.tags ?? []) {
            if (tag === 1) {
                tags.push(monaco.MarkerTag.Unnecessary);
            } else if (tag === 2) {
                tags.push(monaco.MarkerTag.Deprecated);
            }
        }
        if (tags.length > 0) {
            marker.tags = tags;
        }

        if (diagnostic.relatedInformation?.length > 0) {
            marker.relatedInformation = diagnostic.relatedInformation.map((information) => ({
                ...toMonacoRange(information.location.range),
                resource: monaco.Uri.parse(information.location.uri),
                message: information.message
            }));
        }

        return marker;
    });
}

// Returns the parameters of a completion request for Monaco's trigger. Monaco numbers the trigger kinds from
// zero, and the protocol numbers the same kinds from one.
export function toCompletionContext(context) {
    const completionContext = { triggerKind: (context?.triggerKind ?? 0) + 1 };
    if (context?.triggerCharacter) {
        completionContext.triggerCharacter = context.triggerCharacter;
    }

    return completionContext;
}

// Returns Monaco's suggestions for a completion answer, which is a list of items or a completion list.
// `defaultRange` is the word at the cursor. An item that names no range replaces that word.
export function toCompletionList(result, defaultRange) {
    if (!result) {
        return { suggestions: [] };
    }

    const items = Array.isArray(result) ? result : (result.items ?? []);
    const defaults = Array.isArray(result) ? {} : (result.itemDefaults ?? {});

    return {
        suggestions: items.map((item) => toCompletionItem(item, defaults, defaultRange)),
        incomplete: Array.isArray(result) ? false : result.isIncomplete === true
    };
}

function toCompletionItem(item, defaults, defaultRange) {
    const kindName = completionItemKindNames[(item.kind ?? 1) - 1] ?? 'Text';

    let label = item.label;
    if (item.labelDetails) {
        label = {
            label: item.label,
            detail: item.labelDetails.detail,
            description: item.labelDetails.description
        };
    }

    let insertText = item.insertText ?? item.label;
    let range = defaultRange;

    if (item.textEdit) {
        insertText = item.textEdit.newText;
        range = toEditRange(item.textEdit);
    } else if (defaults.editRange) {
        insertText = item.textEditText ?? insertText;
        range = toEditRange(defaults.editRange);
    }

    const suggestion = {
        label,
        kind: monaco.languages.CompletionItemKind[kindName],
        detail: item.detail,
        documentation: toDocumentation(item.documentation),
        sortText: item.sortText,
        filterText: item.filterText,
        preselect: item.preselect,
        insertText,
        range,
        commitCharacters: item.commitCharacters ?? defaults.commitCharacters,
        // The server's item, with the list's default data filled in, for the resolve request.
        lspItem: defaults.data !== undefined && item.data === undefined ? { ...item, data: defaults.data } : item
    };

    const textFormat = item.insertTextFormat ?? defaults.insertTextFormat;
    if (textFormat === snippetTextFormat) {
        suggestion.insertTextRules = monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet;
    }

    if (item.deprecated === true || item.tags?.includes(deprecatedTag)) {
        suggestion.tags = [monaco.languages.CompletionItemTag.Deprecated];
    }

    if (item.additionalTextEdits?.length > 0) {
        suggestion.additionalTextEdits = item.additionalTextEdits.map(toTextEdit);
    }

    return suggestion;
}

// An edit names one range, or a range to insert at and a range to replace.
function toEditRange(edit) {
    if (edit.insert && edit.replace) {
        return { insert: toMonacoRange(edit.insert), replace: toMonacoRange(edit.replace) };
    }

    return toMonacoRange(edit.range ?? edit);
}

function toTextEdit(edit) {
    return { range: toMonacoRange(edit.range), text: edit.newText };
}

// Adds the details of a resolved item to Monaco's suggestion.
export function applyResolvedCompletionItem(suggestion, resolvedItem) {
    if (!resolvedItem) {
        return suggestion;
    }

    if (resolvedItem.documentation !== undefined) {
        suggestion.documentation = toDocumentation(resolvedItem.documentation);
    }

    if (resolvedItem.detail !== undefined) {
        suggestion.detail = resolvedItem.detail;
    }

    if (resolvedItem.additionalTextEdits?.length > 0) {
        suggestion.additionalTextEdits = resolvedItem.additionalTextEdits.map(toTextEdit);
    }

    return suggestion;
}

export function toHover(result) {
    if (!result || result.contents === null || result.contents === undefined) {
        return null;
    }

    const contents = Array.isArray(result.contents) ? result.contents : [result.contents];
    const hover = {
        contents: contents.map(toMarkdown).filter((markdown) => markdown && markdown.value.length > 0)
    };

    if (hover.contents.length === 0) {
        return null;
    }

    if (result.range) {
        hover.range = toMonacoRange(result.range);
    }

    return hover;
}

export function toSignatureHelp(result) {
    if (!result || !(result.signatures?.length > 0)) {
        return null;
    }

    return {
        value: {
            signatures: result.signatures.map((signature) => ({
                label: signature.label,
                documentation: toDocumentation(signature.documentation),
                parameters: (signature.parameters ?? []).map((parameter) => ({
                    label: parameter.label,
                    documentation: toDocumentation(parameter.documentation)
                })),
                activeParameter: signature.activeParameter ?? undefined
            })),
            activeSignature: result.activeSignature ?? 0,
            activeParameter: result.activeParameter ?? 0
        },
        dispose: () => {}
    };
}

// Returns Monaco's locations for a definition or references answer: one location, a list of locations or a
// list of location links. `resolveUri` returns the Monaco URI for a server's URI, or null to leave the location
// out.
export function toLocations(result, resolveUri) {
    if (!result) {
        return [];
    }

    const entries = Array.isArray(result) ? result : [result];
    const locations = [];

    for (const entry of entries) {
        const isLink = entry.targetUri !== undefined;
        const uri = resolveUri(isLink ? entry.targetUri : entry.uri);
        if (!uri) {
            continue;
        }

        if (isLink) {
            const link = {
                uri,
                range: toMonacoRange(entry.targetRange),
                targetSelectionRange: toMonacoRange(entry.targetSelectionRange ?? entry.targetRange)
            };
            if (entry.originSelectionRange) {
                link.originSelectionRange = toMonacoRange(entry.originSelectionRange);
            }
            locations.push(link);
        } else {
            locations.push({ uri, range: toMonacoRange(entry.range) });
        }
    }

    return locations;
}

export function toDocumentHighlights(result) {
    return (result ?? []).map((highlight) => {
        const kindName = highlightKindNames[(highlight.kind ?? 1) - 1] ?? 'Text';

        return {
            range: toMonacoRange(highlight.range),
            kind: monaco.languages.DocumentHighlightKind[kindName]
        };
    });
}

// Returns Monaco's symbols for a symbols answer: a tree of document symbols, or a flat list of symbol
// information.
export function toDocumentSymbols(result) {
    return (result ?? []).map(toDocumentSymbol);
}

function toDocumentSymbol(symbol) {
    const kindName = symbolKindNames[(symbol.kind ?? 1) - 1] ?? 'File';
    const range = toMonacoRange(symbol.range ?? symbol.location.range);

    const tags = [];
    if (symbol.deprecated === true || symbol.tags?.includes(deprecatedTag)) {
        tags.push(monaco.languages.SymbolTag.Deprecated);
    }

    return {
        name: symbol.name,
        detail: symbol.detail ?? symbol.containerName ?? '',
        kind: monaco.languages.SymbolKind[kindName],
        tags,
        range,
        selectionRange: symbol.selectionRange ? toMonacoRange(symbol.selectionRange) : range,
        children: (symbol.children ?? []).map(toDocumentSymbol)
    };
}

export function toFoldingRanges(result) {
    return (result ?? []).map((foldingRange) => {
        const range = {
            start: foldingRange.startLine + 1,
            end: foldingRange.endLine + 1
        };

        const kindName = foldingKindNames[foldingRange.kind];
        if (kindName) {
            range.kind = monaco.languages.FoldingRangeKind[kindName];
        }

        return range;
    });
}

// Returns one list of ranges for each position, from the innermost range outwards.
export function toSelectionRanges(result) {
    return (result ?? []).map((selectionRange) => {
        const ranges = [];
        for (let current = selectionRange; current; current = current.parent) {
            ranges.push({ range: toMonacoRange(current.range) });
        }

        return ranges;
    });
}

export function toLinks(result) {
    return {
        links: (result ?? []).map(toLink)
    };
}

export function toLink(documentLink) {
    return {
        range: toMonacoRange(documentLink.range),
        url: documentLink.target,
        tooltip: documentLink.tooltip,
        // The server's link, for the resolve request.
        lspLink: documentLink
    };
}

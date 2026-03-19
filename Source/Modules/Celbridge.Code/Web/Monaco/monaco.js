// Monaco Editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { monacoClient } from './monaco-client.js';

// State
let editor = null;
let client = null;
let isInitialized = false;
let currentLanguage = 'plaintext';
let pendingNavigation = null;

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], function() {
    initializeEditor();
});

function initializeEditor() {
    // Determine initial theme from system preference
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const initialTheme = prefersDark ? 'vs-dark' : 'vs-light';

    // Create editor with default options
    // scrollBeyondLastLine will be updated during handleEditorInitialize if specified
    editor = monaco.editor.create(document.getElementById('container'), {
        language: 'plaintext',
        automaticLayout: true,
        theme: initialTheme,
        minimap: { autohide: true },
        wordWrap: 'on'
    });

    setupLineEndings();
    setupContentChangeListener();
    setupScrollListener();
    setupThemeListener();

    // Signal that the editor is ready via JSON-RPC notification
    notifyClientReady();
}

function setupThemeListener() {
    // Listen for system color scheme changes (triggered by WebView2's PreferredColorScheme)
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
        const theme = e.matches ? 'vs-dark' : 'vs-light';
        monaco.editor.setTheme(theme);
    });
}

function notifyClientReady() {
    if (!window.isWebView) {
        return;
    }

    // Signal readiness via the standard celbridge document API
    celbridge.document.notifyClientReady();
}

function setupLineEndings() {
    const isWindows = /windows/i.test(
        navigator.userAgentData?.platform || 
        navigator.platform || 
        navigator.userAgent
    );

    const model = editor.getModel();
    model.setEOL(isWindows 
        ? monaco.editor.EndOfLineSequence.CRLF 
        : monaco.editor.EndOfLineSequence.LF
    );
}

function setupContentChangeListener() {
    editor.getModel().onDidChangeContent((event) => {
        if (window.isWebView && isInitialized && client) {
            client.document.notifyChanged();
        }
    });
}

function setupScrollListener() {
    // Throttle scroll events to avoid flooding the host
    let scrollThrottleTimeout = null;

    editor.onDidScrollChange((event) => {
        if (!window.isWebView || !isInitialized || !client) {
            return;
        }

        // Throttle to max ~30 updates per second
        if (scrollThrottleTimeout) {
            return;
        }

        scrollThrottleTimeout = setTimeout(() => {
            scrollThrottleTimeout = null;

            const scrollTop = editor.getScrollTop();
            const clientHeight = editor.getLayoutInfo().height;
            const contentHeight = editor.getContentHeight();
            const maxScroll = contentHeight - clientHeight;

            // Calculate scroll percentage (0.0 to 1.0)
            const percentage = maxScroll > 0
                ? Math.min(1, Math.max(0, scrollTop / maxScroll))
                : 0;

            // Notify host of scroll position change
            client.input.notifyScrollChanged(percentage);
        }, 33);
    });
}

function handleScrollToPercentage(percentage) {
    if (!editor) {
        return;
    }

    const clientHeight = editor.getLayoutInfo().height;
    const contentHeight = editor.getContentHeight();
    const maxScroll = contentHeight - clientHeight;
    const scrollTop = maxScroll * Math.max(0, Math.min(1, percentage));

    editor.setScrollTop(scrollTop);
}

function handleInsertText(text) {
    if (!editor) {
        return;
    }

    const selection = editor.getSelection();
    const range = {
        startLineNumber: selection.startLineNumber,
        startColumn: selection.startColumn,
        endLineNumber: selection.endLineNumber,
        endColumn: selection.endColumn
    };

    editor.executeEdits('insert', [{ range: range, text: text }]);
    editor.focus();
}

function handleApplyEdits(edits) {
    if (!editor || !edits || !Array.isArray(edits)) {
        return;
    }

    // Convert edits to Monaco format and apply as a single undo unit
    const monacoEdits = edits.map(edit => ({
        range: {
            startLineNumber: edit.line,
            startColumn: edit.column,
            endLineNumber: edit.endLine,
            endColumn: edit.endColumn
        },
        text: edit.newText
    }));

    // executeEdits groups all edits as a single undo operation
    editor.executeEdits('applyEdits', monacoEdits);
    editor.focus();
}

async function handleEditorInitialize(params) {
    if (!window.isWebView) {
        return;
    }

    try {
        client = celbridge;

        // Apply editor options from host
        var editorOptions = {};

        if (params.scrollBeyondLastLine !== undefined) {
            editorOptions.scrollBeyondLastLine = params.scrollBeyondLastLine;
        }

        if (params.wordWrap !== undefined) {
            editorOptions.wordWrap = params.wordWrap ? 'on' : 'off';
        }

        if (params.minimapEnabled !== undefined) {
            editorOptions.minimap = { enabled: params.minimapEnabled };
        }

        if (Object.keys(editorOptions).length > 0) {
            editor.updateOptions(editorOptions);
        }

        var language = params.language;

        // Set language before loading content
        if (language) {
            currentLanguage = language;
            monaco.editor.setModelLanguage(editor.getModel(), language);
        }

        // Initialize the host connection and get content
        const result = await client.initialize();

        // Set the content
        if (result.content) {
            editor.setValue(result.content);
        }

        // Register for save requests
        client.document.onRequestSave(async () => {
            const content = editor.getValue();
            await client.document.save(content);
        });

        // Register for external change notifications
        client.document.onExternalChange(async () => {
            const result = await client.document.load();
            if (result.content !== undefined) {
                editor.setValue(result.content);
            }
        });

        isInitialized = true;

        // Notify host that content is loaded and editor is ready for edits
        client.document.notifyContentLoaded();

        // Apply any navigation that arrived before content was loaded
        if (pendingNavigation) {
            const nav = pendingNavigation;
            pendingNavigation = null;
            applyNavigation(nav.lineNumber, nav.column, nav.endLineNumber, nav.endColumn);
        }
    }
    catch (ex) {
        console.error('Failed to initialize host connection:', ex);
    }
}

function handleEditorSetLanguage(language) {
    if (editor && language) {
        currentLanguage = language;
        monaco.editor.setModelLanguage(editor.getModel(), language);
    }
}

function applyNavigation(lineNumber, column, endLineNumber, endColumn) {
    // Use double-requestAnimationFrame to ensure Monaco has completed its internal
    // layout operations after setValue() before applying navigation
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            if (endLineNumber > 0) {
                // Select the matched text range (supports both single-line and multi-line selections)
                editor.setSelection({
                    startLineNumber: lineNumber,
                    startColumn: column,
                    endLineNumber: endLineNumber,
                    endColumn: endColumn
                });
            } else {
                // No range provided - just position the cursor
                editor.setPosition({ lineNumber: lineNumber, column: column });
            }

            // Reveal the line in the center of the editor viewport
            editor.revealLineInCenter(lineNumber);

            // Focus the editor to make the cursor visible
            editor.focus();
        });
    });
}

function handleEditorNavigateToLocation(lineNumber, column, endLineNumber, endColumn) {
    if (!editor) {
        return;
    }

    // Ensure valid values (Monaco uses 1-based line and column numbers)
    lineNumber = Math.max(1, lineNumber || 1);
    column = Math.max(1, column || 1);

    if (!isInitialized) {
        // Content has not been loaded yet - buffer this request and replay it after setValue
        pendingNavigation = { lineNumber, column, endLineNumber, endColumn };
        return;
    }

    applyNavigation(lineNumber, column, endLineNumber, endColumn);
}

async function handleApplyCustomization(scriptUrl) {
    if (!editor || !scriptUrl) {
        return;
    }

    try {
        /// The customize script should export an activate(monaco, editor, container, celbridge) function.
        var module = await import(scriptUrl);
        if (typeof module.activate === 'function') {
            module.activate(monaco, editor, document.getElementById('container'), celbridge);
        } else {
            console.warn('Customization script does not export an activate function:', scriptUrl);
        }
    } catch (ex) {
        console.error('Failed to load customization script:', scriptUrl, ex);
    }
}

// Register RPC handlers via monaco-client
if (window.isWebView) {
    monacoClient.onInitialize(handleEditorInitialize);
    monacoClient.onSetLanguage(handleEditorSetLanguage);
    monacoClient.onNavigateToLocation(handleEditorNavigateToLocation);
    monacoClient.onScrollToPercentage(handleScrollToPercentage);
    monacoClient.onInsertText(handleInsertText);
    monacoClient.onApplyEdits(handleApplyEdits);
    monacoClient.onApplyCustomization(handleApplyCustomization);
}

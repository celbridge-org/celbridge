// Monaco Editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { monacoClient } from './monaco-client.js';

// State
let editor = null;
let client = null;
let isInitialized = false;
let currentLanguage = 'plaintext';

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], function() {
    initializeEditor();
});

function initializeEditor() {
    // Determine initial theme from system preference
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const initialTheme = prefersDark ? 'vs-dark' : 'vs-light';

    editor = monaco.editor.create(document.getElementById('container'), {
        language: 'plaintext',
        automaticLayout: true,
        theme: initialTheme,
        minimap: { autohide: true },
        wordWrap: 'on',
        scrollBeyondLastLine: false
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
            client.document.notifyScrollChanged(percentage);
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

async function handleEditorInitialize(language) {
    if (!window.isWebView) {
        return;
    }

    try {
        client = celbridge;

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

function handleEditorNavigateToLocation(lineNumber, column) {
    if (!editor) {
        return;
    }

    // Ensure valid values (Monaco uses 1-based line and column numbers)
    lineNumber = Math.max(1, lineNumber || 1);
    column = Math.max(1, column || 1);

    // Set the cursor position
    editor.setPosition({ lineNumber: lineNumber, column: column });

    // Reveal the line in the center of the editor viewport
    editor.revealLineInCenter(lineNumber);

    // Focus the editor to make the cursor visible
    editor.focus();
}

// Register RPC handlers via monaco-client
if (window.isWebView) {
    monacoClient.onInitialize(handleEditorInitialize);
    monacoClient.onSetLanguage(handleEditorSetLanguage);
    monacoClient.onNavigateToLocation(handleEditorNavigateToLocation);
    monacoClient.onScrollToPercentage(handleScrollToPercentage);
}

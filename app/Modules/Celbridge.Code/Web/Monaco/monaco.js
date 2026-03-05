// Monaco Editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge.js';
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
        wordWrap: 'on'
    });

    setupLineEndings();
    setupContentChangeListener();
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

    // Send standard JSON-RPC 2.0 notification to signal readiness
    const message = {
        jsonrpc: '2.0',
        method: 'client/ready',
        params: {}
    };

    try {
        if (window.chrome && typeof chrome.webview !== 'undefined') {
            chrome.webview.postMessage(JSON.stringify(message));
        }
    }
    catch (ex) {
        console.error('Failed to send clientReady notification:', ex);
    }
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
}

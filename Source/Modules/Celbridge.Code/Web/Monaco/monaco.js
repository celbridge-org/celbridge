// Monaco Editor initialization for Celbridge WebView integration.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { monacoClient } from './monaco-client.js';
import { log } from './monaco-logger.js';
import { EditorController } from './editor-controller.js';
import { ViewModeController, ViewMode } from './view-mode-controller.js';
import { PreviewController } from './preview-controller.js';
import { attachDividerDrag } from './divider-drag.js';

let editorController = null;
let viewModeController = null;
let previewController = null;

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], () => {
    initialize();
});

function initialize() {
    log('initialize: start');
    const splitRoot = document.getElementById('split-root');
    const editorPane = document.getElementById('editor-pane');
    const dividerElement = document.getElementById('divider');
    const previewPane = document.getElementById('preview-pane');
    const previewIframe = document.getElementById('preview-iframe');
    const container = document.getElementById('container');

    editorController = new EditorController();
    editorController.create(container);

    viewModeController = new ViewModeController({
        splitRoot,
        editorPane,
        previewPane,
        onLayoutChanged: () => editorController.layout()
    });

    previewController = new PreviewController(previewIframe, {
        onOpenResource: (href) => {
            if (window.isWebView) {
                celbridge.input.notifyOpenResource(href);
            }
        },
        onOpenExternal: (href) => {
            if (window.isWebView) {
                celbridge.input.notifyOpenExternal(href);
            }
        },
        onSyncToEditor: (percentage) => {
            editorController.scrollToPercentage(percentage);
            if (window.isWebView) {
                monacoClient.notifyPreviewScrolled(percentage);
            }
        }
    });

    editorController.onContentChanged(() => {
        previewController.render(editorController.getValue());
    });

    attachDividerDrag(dividerElement, viewModeController);

    registerRpcHandlers();

    if (window.isWebView) {
        log('initialize: notifying client ready');
        celbridge.document.notifyClientReady();
    }
}

function registerRpcHandlers() {
    if (!window.isWebView) {
        return;
    }

    const handlers = [
        ['codeEditor/initialize', (params) => handleEditorInitialize(params)],
        ['codeEditor/setLanguage', ({ language }) => editorController.setLanguage(language)],
        ['codeEditor/navigateToLocation', ({ lineNumber, column, endLineNumber, endColumn }) =>
            editorController.navigateToLocation(lineNumber, column, endLineNumber || 0, endColumn || 0)],
        ['codeEditor/scrollToPercentage', ({ percentage }) => editorController.scrollToPercentage(percentage)],
        ['codeEditor/insertText', ({ text }) => editorController.insertText(text)],
        ['codeEditor/applyEdits', ({ edits }) => editorController.applyEdits(edits)],
        ['codeEditor/applyCustomization', ({ scriptUrl }) => editorController.applyCustomization(scriptUrl)],
        ['codeEditor/setPreviewRenderer', ({ rendererUrl }) => handleSetPreviewRenderer(rendererUrl ?? null)],
        ['codeEditor/setViewMode', ({ viewMode }) => viewModeController.setMode(viewMode)],
        ['codeEditor/setBasePath', ({ basePath }) => previewController.setBasePath(basePath)],
        ['codeEditor/setPreviewScrollPercentage', ({ percentage }) => previewController.setScrollPercentage(percentage)]
    ];

    for (const [wireName, handler] of handlers) {
        monacoClient.onRequest(wireName, handler);
    }
}

async function handleEditorInitialize(params) {
    if (!window.isWebView) {
        return;
    }

    log('codeEditor/initialize received', {
        language: params.language,
        hasPreviewRenderer: !!params.previewRendererUrl
    });

    try {
        editorController.applyOptions({
            scrollBeyondLastLine: params.scrollBeyondLastLine,
            wordWrap: params.wordWrap,
            minimapEnabled: params.minimapEnabled
        });

        if (params.language) {
            editorController.setLanguage(params.language);
        }

        // Start loading the preview renderer in parallel with document init
        // when the host specified one. Fire-and-forget: document init continues
        // regardless; the handler renders currentContent once loaded, so a
        // late init still catches up.
        if (typeof params.previewRendererUrl === 'string' &&
            params.previewRendererUrl.length > 0) {
            handleSetPreviewRenderer(params.previewRendererUrl);
        }

        await editorController.initializeHost({
            onExternalReloadContent: (content) => previewController.render(content)
        });
    } catch (ex) {
        console.error('Failed to initialize host connection:', ex);
    }
}

async function handleSetPreviewRenderer(rendererUrl) {
    await previewController.setRenderer(rendererUrl);

    if (!rendererUrl) {
        // Detaching: return to Source view so the user doesn't see an empty
        // preview pane.
        viewModeController.setMode(ViewMode.Source);
        return;
    }

    if (previewController.isActive()) {
        previewController.render(editorController.getValue());
    }
}

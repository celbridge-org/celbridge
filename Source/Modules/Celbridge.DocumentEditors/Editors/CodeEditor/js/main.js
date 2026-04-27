// Entry point for the Celbridge code-editor contribution package.
// Creates the Monaco editor, wires up the optional snippet toolbar, and —
// when the document's options opt in — constructs a PreviewPipeline that
// owns the preview pane, view-mode switcher, and source-to-preview sync.
// The same bundle serves both the code and markdown document contributions;
// the options decide which parts to activate at runtime.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { EditorController } from './editor-controller.js';
import { ViewMode } from './view-mode-controller.js';
import { PreviewPipeline } from './preview-pipeline.js';
import { initializeToolbar } from './toolbar.js';
import { initializeLanguageMap, getLanguageForFile } from './language-mapper.js';
import { log } from './logger.js';

let editorController = null;
let previewPipeline = null;

const options = parseOptions();

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], () => {
    initialize();
});

function parseOptions() {
    // celbridge.options is populated by celbridge.js on first access from
    // window.__celbridgeContext.options. Reading before Monaco finishes
    // loading is safe because the context is already injected when the
    // WebView navigates.
    const raw = celbridge.options || {};
    return {
        previewRendererUrl: raw.preview_renderer_url || null,
        initialViewMode: raw.initial_view_mode || ViewMode.Source,
        enableSnippetToolbar: raw.enable_snippet_toolbar === 'true',
        snippetSet: raw.snippet_set || null
    };
}

async function initialize() {
    log('initialize: start', options);

    const container = document.getElementById('container');

    editorController = new EditorController();
    editorController.create(container);

    if (options.previewRendererUrl) {
        previewPipeline = new PreviewPipeline({
            editorController,
            initialViewMode: options.initialViewMode,
            panes: {
                splitRoot: document.getElementById('split-root'),
                editorPane: document.getElementById('editor-pane'),
                previewPane: document.getElementById('preview-pane'),
                dividerElement: document.getElementById('divider'),
                previewIframe: document.getElementById('preview-iframe')
            },
            onLinkClicked: (href) => {
                if (window.isWebView) {
                    celbridge.input.notifyLinkClicked(href);
                }
            }
        });
    }

    initializeToolbar({
        showViewMode: previewPipeline !== null,
        showSnippets: options.enableSnippetToolbar,
        snippetSet: options.snippetSet,
        viewModeController: previewPipeline?.viewModeController ?? null,
        onInsertSnippet: (text) => editorController.insertText(text)
    });

    await initializeLanguageMap();

    if (!window.isWebView) {
        return;
    }

    log('initialize: notifying client ready');
    celbridge.document.notifyClientReady();

    // Host-to-editor notification for navigate-to-location. A dedicated
    // `editor/*` namespace keeps it distinct from the generic `document/*`
    // lifecycle (initialize, load, save, state).
    celbridge.onNotification('editor/navigateToLocation', (params) => {
        const p = params ?? {};
        editorController.navigateToLocation(
            p.lineNumber ?? 1,
            p.column ?? 1,
            p.endLineNumber ?? 0,
            p.endColumn ?? 0);
    });

    if (previewPipeline) {
        previewPipeline.attachRenderer(options.previewRendererUrl);
    }

    try {
        await editorController.initializeHost({
            onInitialContent: (content, metadata) => {
                const language = getLanguageForFile(metadata?.fileName || '');
                editorController.setLanguage(language);

                if (previewPipeline) {
                    previewPipeline.handleInitialContent(content, metadata?.resourceKey);
                }
            },
            onExternalReloadContent: (content) => {
                previewPipeline?.handleExternalReload(content);
            },
            onRequestState: () => captureState(),
            onRestoreState: (stateJson) => restoreState(stateJson)
        });
    } catch (ex) {
        console.error('Failed to initialize host connection:', ex);
    }
}

function captureState() {
    if (!editorController) {
        return null;
    }

    const state = {
        editorScrollPercentage: editorController.getScrollPercentage()
    };

    if (previewPipeline) {
        Object.assign(state, previewPipeline.captureState());
    }

    return JSON.stringify(state);
}

function restoreState(stateJson) {
    if (!stateJson || !editorController) {
        return;
    }

    try {
        const state = JSON.parse(stateJson);

        // Restore preview layout (flex share, view mode, preview scroll) first
        // so the editor's scroll percentage is applied against the final layout.
        if (previewPipeline) {
            previewPipeline.restoreState(state);
        }

        if (typeof state.editorScrollPercentage === 'number') {
            editorController.scrollToPercentage(state.editorScrollPercentage);
        }
    } catch (ex) {
        log('restoreState: ignoring corrupt state', ex);
    }
}

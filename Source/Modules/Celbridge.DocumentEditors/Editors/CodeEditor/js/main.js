// Entry point for the Celbridge code-editor contribution package.
// Configures Monaco, the optional preview pane, the view-mode switcher,
// and the optional snippet toolbar based on the document's [options] table
// exposed through celbridge.options. The same bundle serves both the code
// and markdown document contributions; the options decide which parts to
// activate at runtime.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { EditorController } from './editor-controller.js';
import { ViewModeController, ViewMode } from './view-mode-controller.js';
import { PreviewController } from './preview-controller.js';
import { attachDividerDrag } from './divider-drag.js';
import { initializeToolbar, updateViewModeButtons, syncSnippetButtonForViewMode } from './toolbar.js';
import { initializeLanguageMap, getLanguageForFile } from './language-mapper.js';
import { log } from './logger.js';

let editorController = null;
let viewModeController = null;
let previewController = null;

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

    const splitRoot = document.getElementById('split-root');
    const editorPane = document.getElementById('editor-pane');
    const dividerElement = document.getElementById('divider');
    const previewPane = document.getElementById('preview-pane');
    const previewIframe = document.getElementById('preview-iframe');
    const container = document.getElementById('container');

    editorController = new EditorController();
    editorController.create(container);

    // Pin scrollBeyondLastLine=false whenever a preview is active. The
    // current percentage-based scroll sync drifts at end-of-document because
    // the editor can scroll past EOF while the preview cannot. This is a
    // stopgap; the design for content-aware (source-map-based) scroll sync
    // lives in unified_document_editors.md. When that lands, remove this
    // block and let Monaco use its default scrollBeyondLastLine=true.
    if (options.previewRendererUrl) {
        editorController.applyOptions({ scrollBeyondLastLine: false });
    }

    viewModeController = new ViewModeController({
        splitRoot,
        editorPane,
        previewPane,
        onLayoutChanged: () => editorController.layout(),
        onModeChanged: (mode) => {
            updateViewModeButtons(mode);
            syncSnippetButtonForViewMode(mode);
        }
    });

    previewController = new PreviewController(previewIframe, {
        onLinkClicked: (href) => {
            if (window.isWebView) {
                celbridge.input.notifyLinkClicked(href);
            }
        },
        onSyncToEditor: (percentage) => {
            editorController.scrollToPercentage(percentage);
        }
    });

    editorController.onContentChanged(() => {
        previewController.render(editorController.getValue());
    });

    attachDividerDrag(dividerElement, viewModeController);

    initializeToolbar({
        showViewMode: !!options.previewRendererUrl,
        showSnippets: options.enableSnippetToolbar,
        snippetSet: options.snippetSet,
        viewModeController,
        onInsertSnippet: (text) => editorController.insertText(text)
    });

    await initializeLanguageMap();

    if (!window.isWebView) {
        return;
    }

    log('initialize: notifying client ready');
    celbridge.document.notifyClientReady();

    // Host-to-editor notifications for navigate-to-location and apply-edits.
    // A dedicated `editor/*` namespace keeps them distinct from the generic
    // `document/*` lifecycle (initialize, load, save, state). The plan to
    // replace these with editor-registered MCP tools is tracked in
    // unified_document_editors.md.
    celbridge.onNotification('editor/navigateToLocation', (params) => {
        const p = params ?? {};
        editorController.navigateToLocation(
            p.lineNumber ?? 1,
            p.column ?? 1,
            p.endLineNumber ?? 0,
            p.endColumn ?? 0);
    });
    celbridge.onNotification('editor/applyEdits', (params) => {
        editorController.applyEdits((params ?? {}).edits);
    });

    if (options.previewRendererUrl) {
        // Fire-and-forget: the renderer loads in parallel with the rest of
        // the initialize flow. render() catches up once the module resolves.
        previewController.setRenderer(options.previewRendererUrl);
    }

    try {
        await editorController.initializeHost({
            onInitialContent: (content, metadata) => {
                const language = getLanguageForFile(metadata?.fileName || '');
                editorController.setLanguage(language);

                if (options.previewRendererUrl) {
                    const basePath = extractParentPath(metadata?.resourceKey || '');
                    previewController.setBasePath(basePath);
                    previewController.render(content || '');
                    viewModeController.setMode(options.initialViewMode);
                }
            },
            onExternalReloadContent: (content) => {
                if (options.previewRendererUrl) {
                    previewController.render(content || '');
                }
            },
            onRequestState: () => captureState(),
            onRestoreState: (stateJson) => restoreState(stateJson)
        });
    } catch (ex) {
        console.error('Failed to initialize host connection:', ex);
    }
}

function extractParentPath(resourceKey) {
    if (!resourceKey) {
        return '';
    }
    const slashIndex = resourceKey.lastIndexOf('/');
    return slashIndex >= 0 ? resourceKey.substring(0, slashIndex + 1) : '';
}

function captureState() {
    if (!editorController) {
        return null;
    }

    const state = {
        editorScrollPercentage: editorController.getScrollPercentage(),
        previewScrollPercentage: previewController && previewController.isActive()
            ? previewController.getScrollPercentage()
            : 0,
        viewMode: viewModeController ? viewModeController.getMode() : ViewMode.Source,
        editorFlexShare: viewModeController ? viewModeController.getFlexShare() : 0.5
    };

    return JSON.stringify(state);
}

function restoreState(stateJson) {
    if (!stateJson || !editorController) {
        return;
    }

    try {
        const state = JSON.parse(stateJson);

        // Apply the saved flex share before setMode so that when setMode transitions
        // into Split mode it uses the persisted ratio rather than the 0.5 default.
        if (options.previewRendererUrl &&
            typeof state.editorFlexShare === 'number' &&
            viewModeController) {
            viewModeController.setFlexShare(state.editorFlexShare);
        }

        if (options.previewRendererUrl &&
            typeof state.viewMode === 'string' &&
            viewModeController) {
            viewModeController.setMode(state.viewMode);
        }

        if (typeof state.editorScrollPercentage === 'number') {
            editorController.scrollToPercentage(state.editorScrollPercentage);
        }

        // Don't guard on previewController.isActive() here: setRenderer() is
        // fire-and-forget and the module may not be loaded yet at restore time.
        // PreviewController buffers the scroll percentage in #pendingScrollPercentage
        // and replays it after the first render completes.
        if (options.previewRendererUrl &&
            typeof state.previewScrollPercentage === 'number' &&
            previewController) {
            previewController.setScrollPercentage(state.previewScrollPercentage);
        }
    } catch (ex) {
        log('restoreState: ignoring corrupt state', ex);
    }
}

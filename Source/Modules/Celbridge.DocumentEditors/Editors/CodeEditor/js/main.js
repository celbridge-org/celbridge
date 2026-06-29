// Entry point for the Celbridge code-editor contribution package.
// Creates the Monaco editor, wires up the optional snippet toolbar, and —
// when the document's options opt in — constructs a PreviewPipeline that
// owns the preview pane, view-mode switcher, and source-to-preview sync.
// The same bundle serves both the code and markdown document contributions;
// the options decide which parts to activate at runtime.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { EditorController } from './editor-controller.js';
import { ViewMode } from './view-mode-controller.js';
import { PreviewPipeline } from './preview-pipeline.js';
import { initializeToolbar, setToolbarReadOnly } from './toolbar.js';
import { initializeLanguageMap, getLanguageForFile } from './language-mapper.js';
import { log } from './logger.js';

let editorController = null;
let previewPipeline = null;

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], () => {
    initialize();
});

function parseOptions() {
    // celbridge.options is populated from the host capability context. Callers must await
    // celbridge.ready() first so the context is resolved (from the injected global on the
    // packaged WinUI head, or over the bridge via host/getContext on the Skia head).
    const raw = celbridge.options || {};
    return {
        previewRendererUrl: resolveRendererUrl(raw.preview_renderer_url),
        initialViewMode: raw.initial_view_mode || ViewMode.Source,
        enableSnippetToolbar: raw.enable_snippet_toolbar === 'true',
        snippetSet: raw.snippet_set || null
    };
}

// The preview renderer ships inside the package and is addressed in the document options as a
// package-root-relative path. Resolve it against the page's own URL so the absolute URL handed to
// import() is correct on every head (the loopback origin or the in-process virtual host), without
// the option needing to name a host. An option that already supplies an absolute URL is preserved.
function resolveRendererUrl(rawUrl) {
    if (!rawUrl) {
        return null;
    }

    return new URL(rawUrl, document.baseURI).href;
}

async function initialize() {
    // Resolve the host capability context before reading celbridge.options. On the Skia head
    // this fetches the context over the bridge (host/getContext); on the packaged WinUI head
    // it resolves immediately from the pre-injected global.
    await celbridge.ready();
    const options = parseOptions();

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
                if (celbridge.isHosted) {
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

    if (!celbridge.isHosted) {
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

    // The host (a menu or keyboard shortcut) routes an edit verb here when the editor holds focus;
    // Monaco runs its own command.
    celbridge.onNotification('input/performEdit', (params) => {
        editorController.performEdit(params?.command);
    });

    // Host-mediated clipboard: the host fetches the selection for copy/cut and pushes text for
    // paste / cut-delete, because the WebView's own JS clipboard write is blocked on the Skia WKWebView.
    celbridge.onRequest('editor/getSelectedText', () => editorController.getSelectedText());
    celbridge.onNotification('editor/insertText', (params) => {
        editorController.insertText(params?.text ?? '');
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

                // Reveal the editor now that Monaco has the first buffer. Until this point
                // #split-root is opacity:0 so the user never sees the empty pre-content view.
                document.getElementById('split-root').classList.add('is-loaded');
            },
            onExternalReloadContent: (content) => {
                previewPipeline?.handleExternalReload(content);
            },
            onRequestState: () => captureState(),
            onRestoreState: (stateJson) => restoreState(stateJson),
            onWritableStateChanged: ({ readOnly }) => {
                // Monaco's readOnly option blocks keyboard input, but the
                // toolbar's mutating affordances (snippet insertion) wrap it
                // and would otherwise sneak edits past the option.
                setToolbarReadOnly(readOnly);
            }
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

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
import { log, warn } from './logger.js';

let editorController = null;
let previewPipeline = null;

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], () => {
    routeClipboardCommandsThroughHost();
    declareClipboardShortcuts();
    initialize();
});

// Points Cut and Paste at the host, because the macOS WebView refuses Monaco's own clipboard access.
// Registering a command under an existing id wins, since Monaco resolves an id to the most recently
// registered handler. Copy stays with Monaco, whose clipboard flavour carries the highlighting and the
// multi-cursor metadata a plain text clipboard would drop.
function routeClipboardCommandsThroughHost() {
    const hostCommands = {
        'editor.action.clipboardCutAction': 'cut',
        'editor.action.clipboardPasteAction': 'paste'
    };

    for (const [commandId, verb] of Object.entries(hostCommands)) {
        monaco.editor.registerCommand(commandId, () => {
            celbridge.input.requestEdit(verb).catch((error) => {
                warn(`${verb} request failed`, error);
            });
        });
    }
}

// A context key nothing ever defines, so a rule guarded by it never fires.
const displayOnlyShortcut = 'celbridgeShortcutHint';

// Monaco leaves the clipboard verbs unbound in a browser build, so its context menu lists Cut, Copy and
// Paste with no chord beside them. These rules give the menu a chord to show: it reads the keybinding
// registered for a command without checking the guard, while the keystroke itself still reaches whoever
// handles it today, because the guard is never satisfied.
function declareClipboardShortcuts() {
    const chords = {
        'editor.action.clipboardCutAction': monaco.KeyCode.KeyX,
        'editor.action.clipboardCopyAction': monaco.KeyCode.KeyC,
        'editor.action.clipboardPasteAction': monaco.KeyCode.KeyV
    };

    monaco.editor.addKeybindingRules(
        Object.entries(chords).map(([command, keyCode]) => ({
            command,
            keybinding: monaco.KeyMod.CtrlCmd | keyCode,
            when: displayOnlyShortcut
        })));
}

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
    // this fetches the context over the bridge (host/getContext). On the packaged WinUI head
    // it resolves immediately from the pre-injected global.
    await celbridge.ready();
    const options = parseOptions();

    log('initialize: start', options);

    const container = document.getElementById('container');

    editorController = new EditorController();
    editorController.create(container);

    // WKWebView lazy-decodes Monaco's inlined codicon @font-face and paints tofu (e.g. in the find widget)
    // until the font is forced. Loading it explicitly makes the glyphs render.
    if (document.fonts?.load) {
        document.fonts.load('16px "codicon"').catch(() => {});
    }

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

    // The host (a menu or keyboard shortcut) routes an edit verb here when the editor holds focus.
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

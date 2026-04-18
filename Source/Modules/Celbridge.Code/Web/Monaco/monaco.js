// Monaco Editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';
import { monacoClient } from './monaco-client.js';

const ViewMode = Object.freeze({
    Source: 'source',
    Split: 'split',
    Preview: 'preview'
});

// State
let editor = null;
let client = null;
let isInitialized = false;
let currentLanguage = 'plaintext';
let pendingNavigation = null;
let isReloadingExternally = false;

// Split-view / preview state. The preview renderer is format-agnostic: the host
// supplies a URL to an ES module that implements the preview contract
// (initialize / render / setBasePath / setScrollPercentage / getScrollPercentage).
// Markdown, AsciiDoc, RST, or any future format is just a different module URL.
let splitRootElement = null;
let editorPaneElement = null;
let dividerElement = null;
let previewPaneElement = null;
let previewIframe = null;
let currentViewMode = ViewMode.Source;
let previewRendererUrl = null;
let previewModule = null;
let previewModulePromise = null;
let pendingBasePath = '';

// Split-mode flex ratio (editor share of the total). Survives drags and mode
// switches so returning to Split preserves the user's chosen proportion.
let editorFlexShare = 0.5;
const minFlexShare = 0.1;
const maxFlexShare = 0.9;

// Configure AMD loader and load Monaco
require.config({ paths: { 'vs': './min/vs' } });
require(['vs/editor/editor.main'], function() {
    initializeEditor();
});

function initializeEditor() {
    cachePreviewDomElements();
    setupDividerDrag();

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

function cachePreviewDomElements() {
    splitRootElement = document.getElementById('split-root');
    editorPaneElement = document.getElementById('editor-pane');
    dividerElement = document.getElementById('divider');
    previewPaneElement = document.getElementById('preview-pane');
    previewIframe = document.getElementById('preview-iframe');
}

function setupThemeListener() {
    // Listen for color scheme changes (triggered by WebView2's PreferredColorScheme).
    // The CSS @media (prefers-color-scheme) queries don't consistently re-evaluate on
    // WebView2 theme swaps, so we drive theme-dependent CSS via a data-theme attribute
    // on <html> and toggle it from this listener alongside Monaco's built-in theme.
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

    const applyTheme = (isDark) => {
        document.documentElement.dataset.theme = isDark ? 'dark' : 'light';
        monaco.editor.setTheme(isDark ? 'vs-dark' : 'vs-light');
    };

    applyTheme(mediaQuery.matches);
    mediaQuery.addEventListener('change', (e) => applyTheme(e.matches));
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
        if (window.isWebView && isInitialized && client && !isReloadingExternally) {
            client.document.notifyChanged();
        }

        // Sync the preview to the latest editor content. No RPC boundary; the
        // renderer runs inside this same Monaco page and writes HTML into the
        // sandboxed preview iframe.
        if (previewModule) {
            previewModule.render(editor.getValue());
        }
    });
}

function setupScrollListener() {
    // Throttle scroll events to avoid flooding the host
    let scrollThrottleTimeout = null;

    editor.onDidScrollChange((event) => {
        if (!window.isWebView || !isInitialized || !client || isReloadingExternally) {
            return;
        }

        // Throttle to max ~30 updates per second
        if (scrollThrottleTimeout) {
            return;
        }

        scrollThrottleTimeout = setTimeout(() => {
            scrollThrottleTimeout = null;

            // Skip scroll sync when the editor is collapsed (e.g., Preview mode).
            // A collapsed editor always reports scrollTop=0, which would incorrectly
            // scroll the preview to the top.
            const clientHeight = editor.getLayoutInfo().height;
            if (clientHeight === 0) {
                return;
            }

            const scrollTop = editor.getScrollTop();
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

        // Start loading the preview renderer in parallel with document init when the
        // host specified one. Fire-and-forget: document init continues regardless;
        // the module calls render(currentContent) once loaded, so a late init still
        // catches up.
        if (typeof params.previewRendererUrl === 'string' && params.previewRendererUrl.length > 0) {
            handleSetPreviewRenderer(params.previewRendererUrl);
        }

        // Initialize the host connection, load content, and register handlers.
        // notifyContentLoaded() is called automatically after this completes.
        await client.initializeDocument({
            onContent: (content) => {
                if (content) {
                    editor.setValue(content);
                }
            },
            onRequestSave: async () => {
                const content = editor.getValue();
                await client.document.save(content);
            },
            onExternalChange: async () => {
                // Capture editor state before reload
                const savedScrollTop = editor.getScrollTop();
                const savedPosition = editor.getPosition();
                const savedSelections = editor.getSelections();

                // Suppress content change and scroll notifications during the entire
                // reload cycle, including the deferred state restoration. This prevents
                // setValue() from sending a scroll-position-zero event to the preview.
                isReloadingExternally = true;

                const result = await client.document.load();
                if (result.content !== undefined) {
                    editor.setValue(result.content);
                }

                // Re-render the preview with the new content. The content-change
                // listener is gated on isReloadingExternally, so we drive the
                // preview explicitly here.
                if (previewModule) {
                    previewModule.render(editor.getValue());
                }

                // Signal to the host that new content has been loaded so consumers (e.g. the code
                // editor preview pane) can refresh. This must fire outside the rAF chain below:
                // when the Monaco WebView is collapsed (Preview mode), requestAnimationFrame is
                // throttled by the browser and the rAF callbacks don't run until the WebView
                // becomes visible again. The preview's refresh is independent of Monaco's internal
                // visual layout, so sending the signal here is safe.
                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);

                // Restore editor state after setValue. One requestAnimationFrame is enough to let
                // Monaco flush its internal view-layout scheduler; setValue itself updates the model
                // synchronously. Keep isReloadingExternally true until restoration is complete.
                // Note: this callback is throttled when Monaco is collapsed, but that's fine because
                // the state it restores is only visible when the editor is shown.
                requestAnimationFrame(() => {
                    try {
                        const model = editor.getModel();

                        // Restore selections, clamping each end against the new document bounds.
                        // Use Monaco's ISelection shape (selectionStart*/position*) rather than the
                        // IRange shape — setSelections requires ISelection fields or it throws.
                        // model.validatePosition handles clamping to valid line/column ranges.
                        if (savedSelections && savedSelections.length > 0) {
                            const clampedSelections = savedSelections.map(selection => {
                                const anchor = model.validatePosition({
                                    lineNumber: selection.selectionStartLineNumber,
                                    column: selection.selectionStartColumn
                                });
                                const cursor = model.validatePosition({
                                    lineNumber: selection.positionLineNumber,
                                    column: selection.positionColumn
                                });
                                return {
                                    selectionStartLineNumber: anchor.lineNumber,
                                    selectionStartColumn: anchor.column,
                                    positionLineNumber: cursor.lineNumber,
                                    positionColumn: cursor.column
                                };
                            });
                            editor.setSelections(clampedSelections);
                        } else if (savedPosition) {
                            const clamped = model.validatePosition({
                                lineNumber: savedPosition.lineNumber,
                                column: savedPosition.column
                            });
                            editor.setPosition(clamped);
                        }

                        editor.setScrollTop(savedScrollTop);
                    } catch (err) {
                        // Defensive: if anything unexpected throws, we still need the flag reset
                        // below to run so future edits aren't silently dropped.
                        console.error('[monaco] External-reload state restore failed:', err);
                    }

                    isReloadingExternally = false;
                });
            }
        });

        isInitialized = true;

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
    // Use a requestAnimationFrame to let Monaco flush any pending view layout after a
    // preceding setValue() so the line/column we navigate to resolves against the committed state.
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
        console.error('Customization script does not export an activate function:', scriptUrl, ex);
    }
}

async function handleSetPreviewRenderer(rendererUrl) {
    if (rendererUrl === previewRendererUrl) {
        return;
    }

    if (!rendererUrl) {
        // Detaching the renderer: return to Source view and reset the iframe so
        // stale preview DOM does not persist if a renderer is re-attached later.
        previewRendererUrl = null;
        previewModule = null;
        previewModulePromise = null;
        applyViewMode(ViewMode.Source);
        if (previewIframe) {
            previewIframe.src = 'about:blank';
        }
        return;
    }

    // Switching renderers (from one URL to another, or from null to a URL) requires
    // reloading the module. Drop any existing module so ensurePreviewModuleLoaded
    // imports the new URL.
    if (previewRendererUrl !== rendererUrl) {
        previewModule = null;
        previewModulePromise = null;
    }
    previewRendererUrl = rendererUrl;

    await ensurePreviewModuleLoaded();

    if (previewModule && editor) {
        previewModule.render(editor.getValue());
    }
}

function handleSetViewMode(viewMode) {
    if (!viewMode) {
        return;
    }
    applyViewMode(viewMode);
}

function handleSetBasePath(basePath) {
    pendingBasePath = basePath || '';
    if (previewModule) {
        previewModule.setBasePath(pendingBasePath);
    }
}

function applyViewMode(viewMode) {
    if (viewMode !== ViewMode.Source &&
        viewMode !== ViewMode.Split &&
        viewMode !== ViewMode.Preview) {
        return;
    }

    if (!splitRootElement) {
        return;
    }

    currentViewMode = viewMode;
    splitRootElement.classList.remove('mode-source', 'mode-split', 'mode-preview');
    splitRootElement.classList.add(`mode-${viewMode}`);

    // In Split mode, apply the current ratio to both panes. In Source/Preview
    // we clear the inline flex styles so the editor pane can expand to fill
    // the available space (the Split-mode drag pins flex-grow values that
    // would otherwise stick around and leave the editor at its previous
    // split-width after switching modes).
    if (editorPaneElement && previewPaneElement) {
        if (viewMode === ViewMode.Split) {
            applyFlexShare();
        } else {
            editorPaneElement.style.flex = '';
            previewPaneElement.style.flex = '';
        }
    }

    // Monaco does not re-measure until its container gets a real size; give
    // it one frame after the CSS change, then force a layout pass.
    requestAnimationFrame(() => {
        if (editor) {
            editor.layout();
        }
    });
}

function applyFlexShare() {
    if (!editorPaneElement || !previewPaneElement) {
        return;
    }
    editorPaneElement.style.flex = `${editorFlexShare} 1 0`;
    previewPaneElement.style.flex = `${1 - editorFlexShare} 1 0`;
}

async function ensurePreviewModuleLoaded() {
    if (previewModule) {
        return previewModule;
    }

    if (!previewModulePromise) {
        previewModulePromise = loadPreviewModule();
    }

    previewModule = await previewModulePromise;
    return previewModule;
}

async function loadPreviewModule() {
    if (!previewIframe) {
        throw new Error('Preview iframe element is missing');
    }
    if (!previewRendererUrl) {
        throw new Error('No preview renderer URL configured');
    }

    // The preview module owns its own iframe shell setup; monaco.js stays generic
    // across preview formats by just importing the URL the host gave it.
    const module = await import(/* @vite-ignore */ previewRendererUrl);

    await module.initialize(previewIframe, {
        onOpenResource: (href) => {
            if (window.isWebView && client) {
                client.input.notifyOpenResource(href);
            }
        },
        onOpenExternal: (href) => {
            if (window.isWebView && client) {
                client.input.notifyOpenExternal(href);
            }
        },
        onSyncToEditor: (percentage) => {
            handleScrollToPercentage(percentage);
            if (window.isWebView) {
                monacoClient.notifyPreviewScrolled(percentage);
            }
        }
    });

    if (pendingBasePath) {
        module.setBasePath(pendingBasePath);
    }

    return module;
}

function setupDividerDrag() {
    if (!dividerElement || !editorPaneElement || !splitRootElement) {
        return;
    }

    // Matches the XAML Splitter's DoubleClickDebounceMs: suppress drag updates
    // for a short window after a double-click so the reset isn't immediately
    // overwritten by a trailing drag delta.
    const doubleClickDebounceMs = 500;
    let lastDoubleClickTime = 0;

    let dragStartX = 0;
    let dragStartWidth = 0;
    let isDragging = false;

    function onPointerMove(event) {
        if (!isDragging) {
            return;
        }
        if (performance.now() - lastDoubleClickTime < doubleClickDebounceMs) {
            return;
        }

        const delta = event.clientX - dragStartX;
        const totalWidth = splitRootElement.clientWidth;
        if (totalWidth <= 0) {
            return;
        }

        const newEditorWidth = dragStartWidth + delta;
        const share = newEditorWidth / totalWidth;
        editorFlexShare = Math.max(minFlexShare, Math.min(maxFlexShare, share));
        applyFlexShare();
        if (editor) {
            editor.layout();
        }
    }

    function onPointerUp(event) {
        if (!isDragging) {
            return;
        }
        isDragging = false;
        dividerElement.classList.remove('dragging');
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
        try {
            dividerElement.releasePointerCapture(event.pointerId);
        } catch {
            // Ignore if pointer capture was already released
        }
    }

    dividerElement.addEventListener('pointerdown', (event) => {
        if (currentViewMode !== ViewMode.Split) {
            return;
        }
        if (performance.now() - lastDoubleClickTime < doubleClickDebounceMs) {
            return;
        }
        isDragging = true;
        dragStartX = event.clientX;
        dragStartWidth = editorPaneElement.getBoundingClientRect().width;
        dividerElement.classList.add('dragging');
        try {
            dividerElement.setPointerCapture(event.pointerId);
        } catch {
            // Some environments don't support pointer capture; fall back to window listeners
        }
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
    });

    dividerElement.addEventListener('dblclick', () => {
        lastDoubleClickTime = performance.now();
        editorFlexShare = 0.5;
        applyFlexShare();
        if (editor) {
            editor.layout();
        }
    });
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
    monacoClient.onSetPreviewRenderer(handleSetPreviewRenderer);
    monacoClient.onSetViewMode(handleSetViewMode);
    monacoClient.onSetBasePath(handleSetBasePath);
}

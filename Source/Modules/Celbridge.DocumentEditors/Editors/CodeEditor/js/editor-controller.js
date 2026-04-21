// Owns the Monaco editor instance and its direct behaviour: lifecycle,
// theming, line-endings, content/scroll change listeners, navigation buffering,
// host-driven editor operations (insertText, applyEdits, navigate, scroll),
// and the external-reload state capture/restore flow.
//
// The module expects the global `monaco` AMD namespace to be loaded before
// create() is called.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';
import { log } from './logger.js';

export class EditorController {
    #editor = null;
    #containerElement = null;
    #isInitialized = false;
    #isReloadingExternally = false;
    #currentLanguage = 'plaintext';
    #pendingNavigation = null;
    #onContentChanged = () => {};

    create(containerElement) {
        this.#containerElement = containerElement;

        // Determine initial theme from system preference
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        const initialTheme = prefersDark ? 'vs-dark' : 'vs-light';

        this.#editor = monaco.editor.create(containerElement, {
            language: 'plaintext',
            automaticLayout: true,
            theme: initialTheme,
            minimap: { autohide: true },
            wordWrap: 'on'
        });

        this.#setupLineEndings();
        this.#setupContentChangeListener();
        this.#setupScrollListener();
        this.#setupThemeListener();
    }

    getValue() {
        return this.#editor ? this.#editor.getValue() : '';
    }

    layout() {
        if (this.#editor) {
            this.#editor.layout();
        }
    }

    setLanguage(language) {
        if (this.#editor && language) {
            this.#currentLanguage = language;
            monaco.editor.setModelLanguage(this.#editor.getModel(), language);
        }
    }

    applyOptions(options) {
        if (!this.#editor) {
            return;
        }

        const editorOptions = {};

        if (options.scrollBeyondLastLine !== undefined) {
            editorOptions.scrollBeyondLastLine = options.scrollBeyondLastLine;
        }

        if (options.wordWrap !== undefined) {
            editorOptions.wordWrap = options.wordWrap ? 'on' : 'off';
        }

        if (options.minimapEnabled !== undefined) {
            editorOptions.minimap = { enabled: options.minimapEnabled };
        }

        if (Object.keys(editorOptions).length > 0) {
            this.#editor.updateOptions(editorOptions);
        }
    }

    insertText(text) {
        if (!this.#editor) {
            return;
        }

        const selection = this.#editor.getSelection();
        const range = {
            startLineNumber: selection.startLineNumber,
            startColumn: selection.startColumn,
            endLineNumber: selection.endLineNumber,
            endColumn: selection.endColumn
        };

        this.#editor.executeEdits('insert', [{ range: range, text: text }]);
        this.#editor.focus();
    }

    applyEdits(edits) {
        if (!this.#editor ||
            !edits ||
            !Array.isArray(edits)) {
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
        this.#editor.executeEdits('applyEdits', monacoEdits);
        this.#editor.focus();
    }

    scrollToPercentage(percentage) {
        if (!this.#editor) {
            return;
        }

        const clientHeight = this.#editor.getLayoutInfo().height;
        const contentHeight = this.#editor.getContentHeight();
        const maxScroll = contentHeight - clientHeight;
        const scrollTop = maxScroll * Math.max(0, Math.min(1, percentage));

        this.#editor.setScrollTop(scrollTop);
    }

    getScrollPercentage() {
        if (!this.#editor) {
            return 0;
        }

        const clientHeight = this.#editor.getLayoutInfo().height;
        const contentHeight = this.#editor.getContentHeight();
        const maxScroll = contentHeight - clientHeight;
        if (maxScroll <= 0) {
            return 0;
        }

        return Math.max(0, Math.min(1, this.#editor.getScrollTop() / maxScroll));
    }

    navigateToLocation(lineNumber, column, endLineNumber, endColumn) {
        if (!this.#editor) {
            return;
        }

        // Ensure valid values (Monaco uses 1-based line and column numbers)
        const clampedLine = Math.max(1, lineNumber || 1);
        const clampedColumn = Math.max(1, column || 1);

        if (!this.#isInitialized) {
            // Content has not been loaded yet - buffer this request and replay it after setValue
            this.#pendingNavigation = {
                lineNumber: clampedLine,
                column: clampedColumn,
                endLineNumber,
                endColumn
            };
            return;
        }

        this.#applyNavigation(clampedLine, clampedColumn, endLineNumber, endColumn);
    }

    async applyCustomization(scriptUrl) {
        if (!this.#editor || !scriptUrl) {
            return;
        }

        try {
            // The customize script should export an activate(monaco, editor, container, celbridge) function.
            const module = await import(scriptUrl);
            if (typeof module.activate === 'function') {
                module.activate(monaco, this.#editor, this.#containerElement, celbridge);
            } else {
                console.warn('Customization script does not export an activate function:', scriptUrl);
            }
        } catch (ex) {
            console.error('Customization script does not export an activate function:', scriptUrl, ex);
        }
    }

    async initializeHost({
        onInitialContent,
        onExternalReloadContent,
        onRequestState,
        onRestoreState
    } = {}) {
        // Initialize the host connection, load content, and register handlers.
        // notifyContentLoaded() is called automatically after this completes.
        log('editor: initializeHost starting');
        await celbridge.initializeDocument({
            onContent: (content, metadata) => {
                log('editor: initial content received', { length: content ? content.length : 0 });
                if (content) {
                    this.#editor.setValue(content);
                }
                if (onInitialContent) {
                    onInitialContent(content, metadata);
                }
            },
            onRequestSave: async () => {
                const content = this.#editor.getValue();
                await celbridge.document.save(content);
            },
            onExternalChange: async () => {
                await this.#handleExternalChange(onExternalReloadContent);
            },
            onRequestState,
            onRestoreState
        });

        this.#isInitialized = true;
        log('editor: initializeHost complete');

        // Apply any navigation that arrived before content was loaded
        if (this.#pendingNavigation) {
            const nav = this.#pendingNavigation;
            this.#pendingNavigation = null;
            this.#applyNavigation(nav.lineNumber, nav.column, nav.endLineNumber, nav.endColumn);
        }
    }

    onContentChanged(callback) {
        this.#onContentChanged = callback ?? (() => {});
    }

    async #handleExternalChange(onExternalReloadContent) {
        // Capture editor state before reload
        const savedScrollTop = this.#editor.getScrollTop();
        const savedPosition = this.#editor.getPosition();
        const savedSelections = this.#editor.getSelections();

        // Suppress content change and scroll notifications during the entire
        // reload cycle, including the deferred state restoration. This prevents
        // setValue() from sending a scroll-position-zero event to the preview.
        this.#isReloadingExternally = true;

        const result = await celbridge.document.load();
        if (result.content !== undefined) {
            this.#editor.setValue(result.content);
        }

        // Let the caller drive dependent surfaces (e.g. the preview pane)
        // before the content-loaded signal fires.
        if (onExternalReloadContent) {
            onExternalReloadContent(this.#editor.getValue());
        }

        // Signal to the host that new content has been loaded so consumers can
        // refresh. This must fire outside the rAF chain below: when the Monaco
        // WebView is collapsed (Preview mode), requestAnimationFrame is
        // throttled by the browser and the rAF callbacks don't run until the
        // WebView becomes visible again. The signal is safe to send here.
        celbridge.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);

        // Restore editor state after setValue. One requestAnimationFrame is
        // enough to let Monaco flush its internal view-layout scheduler;
        // setValue itself updates the model synchronously. Keep
        // isReloadingExternally true until restoration is complete.
        // Note: this callback is throttled when Monaco is collapsed, but that's
        // fine because the state it restores is only visible when shown.
        requestAnimationFrame(() => {
            try {
                this.#restoreEditorState({
                    scrollTop: savedScrollTop,
                    position: savedPosition,
                    selections: savedSelections
                });
            } catch (err) {
                // Defensive: if anything unexpected throws, we still need the
                // flag reset below to run so future edits aren't silently dropped.
                console.error('[monaco] External-reload state restore failed:', err);
            }

            this.#isReloadingExternally = false;
        });
    }

    #restoreEditorState({ scrollTop, position, selections }) {
        const model = this.#editor.getModel();

        // Restore selections, clamping each end against the new document bounds.
        // Use Monaco's ISelection shape (selectionStart*/position*) rather than
        // the IRange shape — setSelections requires ISelection fields or it
        // throws. model.validatePosition handles clamping to valid ranges.
        if (selections && selections.length > 0) {
            const clampedSelections = selections.map(selection => {
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
            this.#editor.setSelections(clampedSelections);
        } else if (position) {
            const clamped = model.validatePosition({
                lineNumber: position.lineNumber,
                column: position.column
            });
            this.#editor.setPosition(clamped);
        }

        this.#editor.setScrollTop(scrollTop);
    }

    #applyNavigation(lineNumber, column, endLineNumber, endColumn) {
        // Use a requestAnimationFrame to let Monaco flush any pending view
        // layout after a preceding setValue() so the line/column we navigate
        // to resolves against the committed state.
        requestAnimationFrame(() => {
            if (endLineNumber > 0) {
                // Select the matched text range (supports both single-line and multi-line selections)
                this.#editor.setSelection({
                    startLineNumber: lineNumber,
                    startColumn: column,
                    endLineNumber: endLineNumber,
                    endColumn: endColumn
                });
            } else {
                // No range provided - just position the cursor
                this.#editor.setPosition({ lineNumber: lineNumber, column: column });
            }

            // Reveal the line in the center of the editor viewport
            this.#editor.revealLineInCenter(lineNumber);

            // Focus the editor to make the cursor visible
            this.#editor.focus();
        });
    }

    #shouldNotifyHost() {
        return window.isWebView &&
            this.#isInitialized &&
            !this.#isReloadingExternally;
    }

    #setupLineEndings() {
        const isWindows = /windows/i.test(
            navigator.userAgentData?.platform ||
            navigator.platform ||
            navigator.userAgent
        );

        const model = this.#editor.getModel();
        model.setEOL(isWindows
            ? monaco.editor.EndOfLineSequence.CRLF
            : monaco.editor.EndOfLineSequence.LF
        );
    }

    #setupContentChangeListener() {
        this.#editor.getModel().onDidChangeContent(() => {
            if (this.#shouldNotifyHost()) {
                celbridge.document.notifyChanged();
            }

            // Fire unconditionally so the preview stays in sync with in-flight
            // edits; the gating above is only for host-direction notifications.
            this.#onContentChanged();
        });
    }

    #setupScrollListener() {
        // Throttle scroll events to avoid flooding the host
        let scrollThrottleTimeout = null;

        this.#editor.onDidScrollChange(() => {
            if (!this.#shouldNotifyHost()) {
                return;
            }

            // Throttle to max ~30 updates per second
            if (scrollThrottleTimeout) {
                return;
            }

            scrollThrottleTimeout = setTimeout(() => {
                scrollThrottleTimeout = null;

                // Skip scroll sync when the editor is collapsed (e.g., Preview
                // mode). A collapsed editor always reports scrollTop=0, which
                // would incorrectly scroll the preview to the top.
                const clientHeight = this.#editor.getLayoutInfo().height;
                if (clientHeight === 0) {
                    return;
                }

                const scrollTop = this.#editor.getScrollTop();
                const contentHeight = this.#editor.getContentHeight();
                const maxScroll = contentHeight - clientHeight;

                // Calculate scroll percentage (0.0 to 1.0)
                const percentage = maxScroll > 0
                    ? Math.min(1, Math.max(0, scrollTop / maxScroll))
                    : 0;

                celbridge.input.notifyScrollChanged(percentage);
            }, 33);
        });
    }

    #setupThemeListener() {
        // Listen for color scheme changes (triggered by WebView2's
        // PreferredColorScheme). The CSS @media (prefers-color-scheme) queries
        // don't consistently re-evaluate on WebView2 theme swaps, so we drive
        // theme-dependent CSS via a data-theme attribute on <html> and toggle
        // it from this listener alongside Monaco's built-in theme.
        const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

        const applyTheme = (isDark) => {
            document.documentElement.dataset.theme = isDark ? 'dark' : 'light';
            monaco.editor.setTheme(isDark ? 'vs-dark' : 'vs-light');
        };

        applyTheme(mediaQuery.matches);
        mediaQuery.addEventListener('change', (e) => applyTheme(e.matches));
    }
}

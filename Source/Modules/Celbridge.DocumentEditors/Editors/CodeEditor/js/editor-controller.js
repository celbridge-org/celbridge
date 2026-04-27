// Owns the Monaco editor instance and its direct behaviour: lifecycle,
// theming, line-endings, content/scroll change listeners, navigation buffering,
// host-driven editor operations (insertText, navigate, scroll), and the
// external-reload state capture/restore flow.
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
    #onScrollChanged = () => {};
    #suppressScrollNotify = false;

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

    scrollToPercentage(percentage) {
        if (!this.#editor) {
            return;
        }

        const clientHeight = this.#editor.getLayoutInfo().height;
        const contentHeight = this.#editor.getContentHeight();
        const maxScroll = contentHeight - clientHeight;
        const scrollTop = maxScroll * Math.max(0, Math.min(1, percentage));

        this.#suppressScrollNotify = true;
        this.#editor.setScrollTop(scrollTop);
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                this.#suppressScrollNotify = false;
            });
        });
    }

    /**
     * Scrolls the editor so the given 1-based model line is at the top of the
     * viewport. `fraction` (0-1) nudges the scroll further into the line.
     *
     * Word-wrap-aware: a model line that wraps to N view lines occupies
     * N * lineHeight of scroll range. `getTopForLineNumber(n+1) - getTopForLineNumber(n)`
     * gives that rendered height, so `fraction * renderedHeight` positions
     * accurately inside the wrapped block.
     */
    scrollToSourceLine(line, fraction = 0) {
        if (!this.#editor) {
            return;
        }

        const model = this.#editor.getModel();
        if (!model) {
            return;
        }

        const lineCount = model.getLineCount();
        const clampedLine = Math.max(1, Math.min(line | 0, lineCount));
        const lineTop = this.#editor.getTopForLineNumber(clampedLine);
        const renderedHeight = this.#modelLineRenderedHeight(clampedLine, lineCount);
        const offset = Math.max(0, Math.min(1, fraction)) * renderedHeight;

        this.#suppressScrollNotify = true;
        this.#editor.setScrollTop(lineTop + offset);
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                this.#suppressScrollNotify = false;
            });
        });
    }

    /**
     * Returns {line, fraction} for the topmost visible *model* line, or null
     * if the editor isn't laid out yet.
     *
     * Word-wrap-aware: locates the model line whose rendered extent brackets
     * the current scrollTop via binary search over `getTopForLineNumber`. The
     * previous implementation used `floor(scrollTop / lineHeight)` which
     * returns a *view* line index - treating it as a model line overshoots
     * into the preview's past-last-block region in any document with long
     * wrapped paragraphs.
     */
    getTopSourceLine() {
        if (!this.#editor) {
            return null;
        }

        const model = this.#editor.getModel();
        if (!model) {
            return null;
        }

        const clientHeight = this.#editor.getLayoutInfo().height;
        if (clientHeight === 0) {
            return null;
        }

        const scrollTop = this.#editor.getScrollTop();
        const lineCount = model.getLineCount();

        // Binary search for the greatest line whose top <= scrollTop.
        let lo = 1;
        let hi = lineCount;
        while (lo < hi) {
            const mid = (lo + hi + 1) >>> 1;
            const midTop = this.#editor.getTopForLineNumber(mid);
            if (midTop <= scrollTop) {
                lo = mid;
            } else {
                hi = mid - 1;
            }
        }
        const topLine = lo;

        const lineTop = this.#editor.getTopForLineNumber(topLine);
        const renderedHeight = this.#modelLineRenderedHeight(topLine, lineCount);
        const fraction = Math.max(0, Math.min(1, (scrollTop - lineTop) / renderedHeight));

        return { line: topLine, fraction };
    }

    /**
     * Rendered height of a model line, accounting for word wrap. The last
     * model line has no next line to subtract against; fall back to the
     * editor's configured lineHeight (unwrapped last lines are the common
     * case).
     */
    #modelLineRenderedHeight(lineNumber, lineCount) {
        const lineHeight = this.#editor.getOption(monaco.editor.EditorOption.lineHeight);
        if (lineNumber >= lineCount) {
            return Math.max(1, lineHeight);
        }
        const top = this.#editor.getTopForLineNumber(lineNumber);
        const nextTop = this.#editor.getTopForLineNumber(lineNumber + 1);
        return Math.max(1, nextTop - top);
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

    /**
     * Registers a callback fired on editor scroll changes.
     * Receives `{line, fraction}` for the topmost visible line, or null when
     * the editor has no layout (collapsed preview mode). The callback is
     * throttled to the browser's animation frame and suppressed while the
     * editor is being programmatically scrolled (to avoid echoing scrolls
     * back from the preview).
     */
    onScrollChanged(callback) {
        this.#onScrollChanged = callback ?? (() => {});
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
            // setValue replaces the buffer and clears Monaco's undo history,
            // so Ctrl+Z after an external reload cannot replay a prior edit
            // against the new baseline.
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
        // Dispatch scroll events on an rAF so fast scrolling coalesces into
        // one sync per frame. Skip dispatch when Monaco is collapsed (Preview
        // mode) because the layout reports scrollTop=0 while invisible.
        let scrollFramePending = false;

        this.#editor.onDidScrollChange(() => {
            if (!this.#shouldNotifyHost() || this.#suppressScrollNotify) {
                return;
            }

            if (scrollFramePending) {
                return;
            }
            scrollFramePending = true;

            requestAnimationFrame(() => {
                scrollFramePending = false;

                if (this.#suppressScrollNotify) {
                    return;
                }

                const target = this.getTopSourceLine();
                if (target !== null) {
                    this.#onScrollChanged(target);
                }
            });
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

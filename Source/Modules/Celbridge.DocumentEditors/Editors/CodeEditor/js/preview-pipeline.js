// Orchestrates the editor-and-preview experience. Owns the preview iframe
// controller, the view-mode layout controller, the divider drag handler,
// and the wiring that keeps the preview pane up to date with the editor or,
// for a renderer that shows the saved file, with the file on disk.
//
// Only instantiated when a document's options supply a preview_renderer_url,
// so plain code documents never construct this pipeline and pay no
// preview-related cost at runtime.

import { PreviewController } from './preview-controller.js';
import { ViewModeController, ViewMode } from './view-mode-controller.js';
import { attachSplitter } from '/assets/celbridge-client/ui/splitter.js';
import { projectUrl } from '/assets/celbridge-client/api/document-api.js';
import { updateViewModeButtons, syncSnippetButtonForViewMode } from './toolbar.js';

export class PreviewPipeline {
    #initialViewMode;
    #onLinkClicked;
    #editorController;
    #viewModeController;
    #previewController;

    // The loopback URL of the document's file, known once the initial content arrives.
    #documentUrl = null;

    // Settles once the renderer has loaded, or has failed to load.
    #rendererSettled = Promise.resolve();

    constructor({
        editorController,
        initialViewMode,
        panes,
        onLinkClicked
    }) {
        this.#initialViewMode = initialViewMode ?? ViewMode.Source;
        this.#onLinkClicked = onLinkClicked ?? (() => {});
        this.#editorController = editorController;

        this.#viewModeController = new ViewModeController({
            splitRoot: panes.splitRoot,
            editorPane: panes.editorPane,
            previewPane: panes.previewPane,
            onLayoutChanged: () => editorController.layout(),
            onModeChanged: (mode) => {
                updateViewModeButtons(mode);
                syncSnippetButtonForViewMode(mode);
                editorController.setHidden(mode === ViewMode.Preview);
            }
        });

        // Focus entering the preview raises no focus event in this document, so the editor would keep
        // claiming the clipboard while the preview's find bar holds the keyboard. Reattached on each load,
        // which replaces the iframe's document.
        panes.previewIframe?.addEventListener('load', () => {
            panes.previewIframe.contentDocument?.addEventListener(
                'focusin',
                () => editorController.refreshEditAvailability());
        });

        this.#previewController = new PreviewController(panes.previewIframe, {
            onLinkClicked: (href) => this.#onLinkClicked(href),
            onSyncToEditor: (target) => {
                // target: {line, fraction}. The preview reports the topmost visible
                // source block and the editor reveals that exact line rather than a
                // proportional guess, giving precise sync between rendered blocks
                // and their source locations.
                editorController.scrollToSourceLine(target.line, target.fraction);
            }
        });

        this.#attachDivider(panes.dividerElement);

        editorController.onContentChanged(() => {
            // A renderer that shows the saved file does not use the buffer, so skip copying it on every edit.
            if (this.#previewController.canRefresh()) {
                return;
            }

            this.#previewController.render(editorController.getValue());
        });
    }

    get viewModeController() {
        return this.#viewModeController;
    }

    // Callers do not wait on this: the renderer loads in parallel with the
    // rest of the initialize flow, and render() catches up once the module
    // resolves.
    attachRenderer(rendererUrl) {
        const attaching = this.#attachRenderer(rendererUrl);
        this.#rendererSettled = attaching.catch(() => {});

        return attaching;
    }

    async #attachRenderer(rendererUrl) {
        await this.#previewController.setRenderer(rendererUrl);

        // Scroll sync is only wired for a renderer that maps its output to source lines, so the controller
        // never holds a scroll target it cannot apply.
        if (this.#previewController.canScrollToSourceLine()) {
            this.#editorController.onScrollChanged((target) => {
                this.#previewController.scrollToSourceLine(target.line, target.fraction);
            });
        }
    }

    // Opens the find that belongs to the visible pane: the preview's own bar in Preview mode, and
    // otherwise the editor's, which is what Command+F reaches in each mode.
    beginFind() {
        if (this.#viewModeController.getMode() !== ViewMode.Preview) {
            return false;
        }

        return this.#previewController.beginFind();
    }

    // Resolves once the renderer has loaded and been handed the initial content, so the preview has started
    // showing it.
    async handleInitialContent(content, resourceKey) {
        const basePath = extractParentPath(resourceKey ?? '');
        this.#previewController.setBasePath(basePath);
        this.#previewController.render(content || '');
        this.#viewModeController.setMode(this.#initialViewMode);

        if (resourceKey) {
            this.#documentUrl = projectUrl(resourceKey);
        }

        this.#updatePreview();

        await this.#rendererSettled;
    }

    handleExternalReload(content) {
        this.#previewController.render(content || '');
        this.#updatePreview();
    }

    // Called once a save made by the editor has reached disk.
    handleSaved() {
        this.#updatePreview();
    }

    captureState() {
        return {
            previewScrollPercentage: this.#previewController.isActive()
                ? this.#previewController.getScrollPercentage()
                : 0,
            viewMode: this.#viewModeController.getMode(),
            editorFlexShare: this.#viewModeController.getFlexShare()
        };
    }

    restoreState(state) {
        // Apply the saved flex share before setMode so that when setMode transitions
        // into Split mode it uses the persisted ratio rather than the 0.5 default.
        if (typeof state.editorFlexShare === 'number') {
            this.#viewModeController.setFlexShare(state.editorFlexShare);
        }

        if (typeof state.viewMode === 'string') {
            this.#viewModeController.setMode(state.viewMode);
        }

        // Don't guard on previewController.isActive() here: attachRenderer() is
        // fire-and-forget and the module may not be loaded yet at restore time.
        // PreviewController buffers the scroll percentage and replays it after
        // the first render completes.
        if (typeof state.previewScrollPercentage === 'number') {
            this.#previewController.setScrollPercentage(state.previewScrollPercentage);
        }
    }

    // Reloads a preview of the saved file. It reloads even while Source mode hides it, so it always shows the
    // saved file.
    #updatePreview() {
        if (this.#documentUrl === null) {
            return;
        }

        this.#previewController.refresh(this.#documentUrl);
    }

    #attachDivider(dividerElement) {
        let dragStartWidth = 0;

        attachSplitter(dividerElement, {
            isEnabled: () => this.#viewModeController.isSplitMode(),
            onDragStart: () => {
                dragStartWidth = this.#viewModeController.getEditorPaneWidth();
            },
            onDrag: (deltaX) => {
                const totalWidth = this.#viewModeController.getSplitRootWidth();
                if (totalWidth <= 0) {
                    return;
                }
                this.#viewModeController.setFlexShare((dragStartWidth + deltaX) / totalWidth);
            },
            onReset: () => {
                this.#viewModeController.setFlexShare(0.5);
            }
        });
    }
}

// Returns the parent path of a resource key, stripped of the "project:" prefix
// so callers can resolve it through projectUrl() without producing a bogus
// "project:..." segment in the URL.
function extractParentPath(resourceKey) {
    if (!resourceKey) {
        return '';
    }
    const stripped = resourceKey.startsWith('project:')
        ? resourceKey.substring('project:'.length)
        : resourceKey;
    const slashIndex = stripped.lastIndexOf('/');
    return slashIndex >= 0 ? stripped.substring(0, slashIndex + 1) : '';
}

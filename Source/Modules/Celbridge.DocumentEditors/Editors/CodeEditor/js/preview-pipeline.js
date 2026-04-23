// Orchestrates the editor-and-preview experience. Owns the preview iframe
// controller, the view-mode layout controller, the divider drag handler,
// and the wiring that keeps editor content and scroll position in sync
// with the preview pane.
//
// Only instantiated when a document's options supply a preview_renderer_url,
// so plain code documents never construct this pipeline and pay no
// preview-related cost at runtime.

import { PreviewController } from './preview-controller.js';
import { ViewModeController, ViewMode } from './view-mode-controller.js';
import { attachDividerDrag } from './divider-drag.js';
import { updateViewModeButtons, syncSnippetButtonForViewMode } from './toolbar.js';

export class PreviewPipeline {
    #editorController;
    #initialViewMode;
    #onLinkClicked;
    #viewModeController;
    #previewController;

    constructor({
        editorController,
        initialViewMode,
        panes,
        onLinkClicked
    }) {
        this.#editorController = editorController;
        this.#initialViewMode = initialViewMode ?? ViewMode.Source;
        this.#onLinkClicked = onLinkClicked ?? (() => {});

        this.#viewModeController = new ViewModeController({
            splitRoot: panes.splitRoot,
            editorPane: panes.editorPane,
            previewPane: panes.previewPane,
            onLayoutChanged: () => editorController.layout(),
            onModeChanged: (mode) => {
                updateViewModeButtons(mode);
                syncSnippetButtonForViewMode(mode);
            }
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

        attachDividerDrag(panes.dividerElement, this.#viewModeController);

        editorController.onContentChanged(() => {
            this.#previewController.render(editorController.getValue());
        });

        // Editor-to-preview scroll sync. PreviewController buffers the target
        // internally while the renderer module is still loading, so no external
        // guard is needed here.
        editorController.onScrollChanged((target) => {
            this.#previewController.scrollToSourceLine(target.line, target.fraction);
        });
    }

    get viewModeController() {
        return this.#viewModeController;
    }

    attachRenderer(rendererUrl) {
        // Fire-and-forget: the renderer loads in parallel with the rest of
        // the initialize flow. render() catches up once the module resolves.
        this.#previewController.setRenderer(rendererUrl);
    }

    handleInitialContent(content, resourceKey) {
        const basePath = extractParentPath(resourceKey ?? '');
        this.#previewController.setBasePath(basePath);
        this.#previewController.render(content || '');
        this.#viewModeController.setMode(this.#initialViewMode);
    }

    handleExternalReload(content) {
        this.#previewController.render(content || '');
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
}

function extractParentPath(resourceKey) {
    if (!resourceKey) {
        return '';
    }
    const slashIndex = resourceKey.lastIndexOf('/');
    return slashIndex >= 0 ? resourceKey.substring(0, slashIndex + 1) : '';
}

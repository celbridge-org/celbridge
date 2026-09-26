// Orchestrates the editor-and-preview experience. Owns the preview iframe
// controller, the view-mode layout controller, the divider drag handler,
// and the wiring that keeps the preview pane up to date with the editor.
// A renderer that shows the saved file loads it when the document opens or
// moves, and otherwise only when the user reloads it.
//
// Only instantiated when a document's options supply a preview_renderer_url,
// so plain code documents never construct this pipeline and pay no
// preview-related cost at runtime.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { PreviewController } from './preview-controller.js';
import { ViewModeController, ViewMode } from './view-mode-controller.js';
import { attachSplitter } from '/assets/celbridge-client/ui/splitter.js';
import { projectUrl } from '/assets/celbridge-client/api/document-api.js';
import {
    setToolbarViewMode,
    setToolbarPreviewStale,
    showToolbarReloadButton
} from './toolbar.js';

export class PreviewPipeline {
    #initialViewMode;
    #onLinkClicked;
    #editorController;
    #viewModeController;
    #previewController;

    // The loopback URL of the document's file, known once the initial content arrives. A rename or a move
    // changes it.
    #documentUrl = null;

    // Settles once the renderer has loaded, or has failed to load.
    #rendererSettled = Promise.resolve();

    // Set when the user reloads while edits are still unsaved. The preview reloads once a save has written them
    // all, so it shows them.
    #isReloadAwaitingSave = false;

    // The editor's version id of the text the file held when the preview last loaded it, or null before the
    // first load.
    #loadedVersionId = null;

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
                setToolbarViewMode(mode);
                editorController.setHidden(mode === ViewMode.Preview);
            }
        });

        // Focus and keys inside the preview raise no events in this document. Without the focus event the
        // editor would keep claiming the clipboard while the preview's find bar holds the keyboard. Without
        // the keys, F5 in the preview would reload the whole editor page. Reattached on each load, which
        // replaces the iframe's document.
        panes.previewIframe?.addEventListener('load', () => {
            const frameDocument = panes.previewIframe.contentDocument;
            if (!frameDocument) {
                return;
            }

            frameDocument.addEventListener(
                'focusin',
                () => editorController.refreshEditAvailability());
            celbridge.input.watchReloadKeys(frameDocument);
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
                this.#syncStale();
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

        // A renderer that shows the saved file changes only when it is reloaded, so it gets the reload button.
        // A renderer of the buffer follows every edit and never needs one.
        if (this.#previewController.canRefresh()) {
            showToolbarReloadButton(this.#viewModeController.getMode());
            this.#syncStale();
        }

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

    // A preview of the buffer renders the new content. A preview of the saved file waits to be reloaded, unless
    // the user asked for a reload that was waiting on a save, which the change on disk has dropped.
    handleExternalReload(content, resourceKey) {
        this.#setResourceKey(resourceKey);
        this.#previewController.render(content || '');

        if (this.#isReloadAwaitingSave) {
            this.#isReloadAwaitingSave = false;
            this.#updatePreview();
        }
    }

    // Called when a rename or a move gives the open document a new name and path. A preview of the saved file
    // reloads at the new address, and a preview of the buffer renders again against the new folder.
    handleRenamed(resourceKey) {
        if (!resourceKey) {
            return;
        }

        this.#setResourceKey(resourceKey);

        if (!this.#previewController.canRefresh()) {
            this.#previewController.render(this.#editorController.getValue());
        }

        this.#updatePreview();
    }

    // Called once a save made by the editor has reached disk. The preview does not follow saves, so this only
    // completes a reload that was waiting, and only once the save has left no edit unsaved.
    handleSaved() {
        if (!this.#isReloadAwaitingSave ||
            this.#editorController.hasUnsavedEdits()) {
            return;
        }

        this.#isReloadAwaitingSave = false;
        this.#updatePreview();
    }

    // Called when the user reloads the preview. The frame goes back to the document even when the page has
    // navigated itself somewhere else. The host saves only after the document has gone a second without
    // changing, so while edits are unsaved the reload waits for the save that includes them.
    reload() {
        if (this.#editorController.hasUnsavedEdits()) {
            this.#isReloadAwaitingSave = true;
            return;
        }

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

    // Takes the document's name and path, which a rename or a move changes while the document stays open.
    #setResourceKey(resourceKey) {
        if (!resourceKey) {
            return;
        }

        this.#previewController.setBasePath(extractParentPath(resourceKey));
        this.#documentUrl = projectUrl(resourceKey);
    }

    // Loads the saved file into a preview of it, at the document's current address.
    #updatePreview() {
        if (this.#documentUrl === null) {
            return;
        }

        this.#previewController.refresh(this.#documentUrl);
        this.#loadedVersionId = this.#editorController.getSavedVersionId();
        this.#syncStale();
    }

    // A preview of the saved file is stale while the buffer differs from the text it last loaded. An undo
    // back to that text makes it current again. A change to a file the page links does not count, since
    // the document watches only its own file.
    #syncStale() {
        const isStale = this.#previewController.canRefresh() &&
            this.#loadedVersionId !== null &&
            this.#editorController.getVersionId() !== this.#loadedVersionId;

        setToolbarPreviewStale(isStale);
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

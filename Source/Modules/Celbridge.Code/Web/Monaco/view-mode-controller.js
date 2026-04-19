// Owns the layout between the editor and preview panes. Toggles the mode-*
// CSS class on the split root, manages the editor/preview flex ratio in
// Split mode, and clears inline flex in Source/Preview so the visible pane
// expands to fill the available space. Fires onLayoutChanged after layout
// transitions so the Monaco editor can re-measure its viewport.

import { log } from './monaco-logger.js';

export const ViewMode = Object.freeze({
    Source: 'source',
    Split: 'split',
    Preview: 'preview'
});

const minFlexShare = 0.1;
const maxFlexShare = 0.9;

export class ViewModeController {
    #splitRoot;
    #editorPane;
    #previewPane;
    #onLayoutChanged;
    #currentMode = ViewMode.Source;
    #editorFlexShare = 0.5;

    constructor({ splitRoot, editorPane, previewPane, onLayoutChanged }) {
        this.#splitRoot = splitRoot;
        this.#editorPane = editorPane;
        this.#previewPane = previewPane;
        this.#onLayoutChanged = onLayoutChanged ?? (() => {});
    }

    setMode(mode) {
        if (mode !== ViewMode.Source &&
            mode !== ViewMode.Split &&
            mode !== ViewMode.Preview) {
            return;
        }

        if (!this.#splitRoot) {
            return;
        }

        const previousMode = this.#currentMode;
        this.#currentMode = mode;
        this.#splitRoot.classList.remove('mode-source', 'mode-split', 'mode-preview');
        this.#splitRoot.classList.add(`mode-${mode}`);
        log('viewMode: changed', { from: previousMode, to: mode });

        // In Split mode apply the current ratio to both panes. In Source/Preview
        // we clear the inline flex styles so the visible pane can expand to
        // fill the available space (the Split-mode drag pins flex-grow values
        // that would otherwise stick around).
        if (this.#editorPane && this.#previewPane) {
            if (mode === ViewMode.Split) {
                this.#applyFlexShare();
            } else {
                this.#editorPane.style.flex = '';
                this.#previewPane.style.flex = '';
            }
        }

        // Monaco does not re-measure until its container gets a real size; give
        // it one frame after the CSS change, then fire the layout callback.
        requestAnimationFrame(() => {
            this.#onLayoutChanged();
        });
    }

    getMode() {
        return this.#currentMode;
    }

    isSplitMode() {
        return this.#currentMode === ViewMode.Split;
    }

    setFlexShare(share) {
        const clamped = Math.max(minFlexShare, Math.min(maxFlexShare, share));
        this.#editorFlexShare = clamped;
        this.#applyFlexShare();
        this.#onLayoutChanged();
    }

    getSplitRootWidth() {
        return this.#splitRoot ? this.#splitRoot.clientWidth : 0;
    }

    getEditorPaneWidth() {
        return this.#editorPane ? this.#editorPane.getBoundingClientRect().width : 0;
    }

    #applyFlexShare() {
        if (!this.#editorPane || !this.#previewPane) {
            return;
        }
        this.#editorPane.style.flex = `${this.#editorFlexShare} 1 0`;
        this.#previewPane.style.flex = `${1 - this.#editorFlexShare} 1 0`;
    }
}

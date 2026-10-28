// Scales the document's text: the source editor's font size and, when the
// document has one, the preview. The level multiplies the editor's default
// font size, and a level of 1 shows that default.

const ZOOM_LEVELS = [0.5, 0.67, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3];
const DEFAULT_ZOOM = 1;

export class ZoomController {
    #editorController;
    #previewPipeline;
    #baseFontSize;
    #zoom = DEFAULT_ZOOM;
    #listeners = [];

    constructor({ editorController, previewPipeline }) {
        this.#editorController = editorController;
        this.#previewPipeline = previewPipeline;
        this.#baseFontSize = editorController.getFontSize();
    }

    getZoom() {
        return this.#zoom;
    }

    onChanged(listener) {
        this.#listeners.push(listener);
    }

    zoomIn() {
        const next = ZOOM_LEVELS.find((level) => level > this.#zoom + 0.001);
        if (next !== undefined) {
            this.setZoom(next);
        }
    }

    zoomOut() {
        const previous = [...ZOOM_LEVELS].reverse().find((level) => level < this.#zoom - 0.001);
        if (previous !== undefined) {
            this.setZoom(previous);
        }
    }

    reset() {
        this.setZoom(DEFAULT_ZOOM);
    }

    setZoom(zoom) {
        if (typeof zoom !== 'number' || !Number.isFinite(zoom)) {
            return;
        }

        const min = ZOOM_LEVELS[0];
        const max = ZOOM_LEVELS[ZOOM_LEVELS.length - 1];
        this.#zoom = Math.min(max, Math.max(min, zoom));

        this.#editorController.setFontSize(Math.round(this.#baseFontSize * this.#zoom));
        this.#previewPipeline?.setZoom(this.#zoom);

        for (const listener of this.#listeners) {
            listener(this.#zoom);
        }
    }
}

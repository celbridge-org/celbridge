// View API: whether this page can trust the box it is laid out in, and how to wait until it can.

// How long a settle waits for a size to stop changing before the caller acts on whatever it has.
const SETTLE_TIMEOUT_MS = 4000;

// How often the size is sampled while the page is off screen, where there are no animation frames.
const HIDDEN_SAMPLE_MS = 100;

/**
 * Viewport trust for a hosted surface. The host gives a surface the geometry its content will be read at and
 * reports when it has, which is what separates a real layout from the placeholder an unarranged surface is
 * left with. A page that sizes something off its own box, such as a terminal's cell grid or a canvas backing
 * store, has to wait for that: sizing the work off a placeholder and correcting it afterwards costs whatever
 * was drawn at the wrong size.
 */
export class ViewAPI {
    /** @type {import('../core/state-store.js').Store} */
    #viewState;

    /**
     * @param {import('../core/state-store.js').Store} viewState - This view's own host state store.
     */
    constructor(viewState) {
        this.#viewState = viewState;
    }

    /**
     * Whether the host has reported this surface sized, so its geometry is the one its content will be read
     * at. False until it has.
     * @returns {boolean}
     */
    get isSized() {
        return this.#viewState.current?.isSized === 'true';
    }

    /**
     * Whether no size is coming while the page stays off screen. The platform does not lay out a page it is
     * not displaying, and where the host cannot give an unarranged surface a viewport it has none to hand
     * over either. Page visibility is what covers every reason for being off screen at once: a tab that is
     * not selected, a collapsed area, a hidden panel.
     * @returns {boolean}
     */
    get sizeUnavailable() {
        return isPageHidden() &&
            this.#viewState.current?.canSizeUnarranged !== 'true';
    }

    /**
     * Whether a measurement of an element is worth acting on: the host has reported the surface sized and the
     * element has a box.
     * @param {Element} element - The element the page measures its content against.
     * @returns {boolean}
     */
    canMeasure(element) {
        return this.isSized &&
            element.clientWidth > 0 &&
            element.clientHeight > 0;
    }

    /**
     * Resolves once an element's box has stopped changing, so work that is sized once is sized at the
     * geometry the surface keeps. Returns immediately when no size is coming, and gives up after the
     * timeout, so a size that never settles cannot hold the caller. Whether what it has is then worth
     * measuring is canMeasure's answer.
     * @param {Element} element - The element the page measures its content against.
     * @param {Object} [options]
     * @param {number} [options.timeoutMs] - How long to wait before giving up.
     * @returns {Promise<void>}
     */
    async waitForStableSize(element, options = {}) {
        if (this.sizeUnavailable) {
            return;
        }

        let waiting = true;

        const settled = (async () => {
            let previousWidth = -1;
            let previousHeight = -1;

            while (waiting) {
                await nextSizeSample();

                if (this.sizeUnavailable) {
                    return;
                }

                const width = element.clientWidth;
                const height = element.clientHeight;
                if (this.isSized &&
                    width > 0 &&
                    height > 0 &&
                    width === previousWidth &&
                    height === previousHeight) {
                    return;
                }

                previousWidth = width;
                previousHeight = height;
            }
        })();

        // A timer keeps the caller moving even when the size never settles.
        const timeoutMs = options.timeoutMs ?? SETTLE_TIMEOUT_MS;
        const deadline = new Promise((resolve) => setTimeout(resolve, timeoutMs));

        await Promise.race([settled, deadline]);
        waiting = false;
    }
}

/**
 * The next moment worth measuring at. A hidden page's geometry only changes when the host sizes its surface,
 * so a timer samples it.
 * @returns {Promise<void>}
 */
function nextSizeSample() {
    if (isPageHidden() ||
        typeof requestAnimationFrame === 'undefined') {
        return new Promise((resolve) => setTimeout(resolve, HIDDEN_SAMPLE_MS));
    }

    return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

function isPageHidden() {
    return typeof document !== 'undefined' && document.hidden === true;
}

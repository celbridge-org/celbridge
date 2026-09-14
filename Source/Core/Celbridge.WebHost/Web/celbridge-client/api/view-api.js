// View API: whether this page can trust the box it is laid out in, and how to wait until it can.

// How often the size is sampled while the page is off screen, where there are no animation frames. Also the
// bound on a sample taken while the page is on screen, since a page hidden after its frame is requested
// runs no frames to release it.
const HIDDEN_SAMPLE_MS = 100;

// Fallback settle bound for a caller with no budget of its own. A caller whose host holds work open while it
// waits should pass that budget as timeoutMs rather than inherit this.
const SETTLE_TIMEOUT_MS = 4000;

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
     * Registers a handler called whenever the host reports state for this view, which is when the answers
     * above can change. A surface that sizes content off its own box re-measures here, so it follows the
     * geometry the host gives it rather than measuring once. The handler runs immediately if state has
     * already arrived.
     * @param {() => void} handler
     */
    onChanged(handler) {
        this.#viewState.onChanged(() => handler());
    }

    /**
     * Whether an element's box can be trusted for sizing content. That needs the host to have reported the
     * surface sized, sizeUnavailable to be false, and the element to have a non-zero box. A hidden page can
     * still have a placeholder size after the host reports it sized. A missing element returns false.
     * @param {Element|null} element - The element the page measures its content against.
     * @returns {boolean}
     */
    canMeasure(element) {
        return this.#measures(measureBox(element));
    }

    /**
     * Resolves once an element's box has stopped changing, so work that is sized once is sized at the
     * geometry the surface keeps. Returns immediately when no size is coming, and gives up after the
     * timeout, so a size that never settles cannot hold the caller. Whether what it has is then worth
     * measuring is canMeasure's answer.
     * @param {Element|null} element - The element the page measures its content against.
     * @param {Object} [options]
     * @param {number} [options.timeoutMs] - How long to wait before giving up.
     * @returns {Promise<void>}
     */
    async waitForStableSize(element, options = {}) {
        if (this.sizeUnavailable) {
            return;
        }

        const timeoutMs = options.timeoutMs ?? SETTLE_TIMEOUT_MS;

        let waiting = true;
        let deadlineTimer = null;

        const settled = (async () => {
            let previousWidth = -1;
            let previousHeight = -1;

            while (waiting) {
                await nextSizeSample();

                if (!waiting ||
                    this.sizeUnavailable) {
                    return;
                }

                const box = measureBox(element);
                if (this.#measures(box) &&
                    box.width === previousWidth &&
                    box.height === previousHeight) {
                    return;
                }

                previousWidth = box.width;
                previousHeight = box.height;
            }
        })();

        // A timer keeps the caller moving even when the size never settles.
        const deadline = new Promise((resolve) => {
            deadlineTimer = setTimeout(resolve, timeoutMs);
        });

        try {
            await Promise.race([settled, deadline]);
        } finally {
            waiting = false;
            clearTimeout(deadlineTimer);
        }
    }

    /**
     * @param {{ width: number, height: number }} box
     * @returns {boolean}
     */
    #measures(box) {
        return this.isSized &&
            !this.sizeUnavailable &&
            box.width > 0 &&
            box.height > 0;
    }
}

/**
 * An element's box, read once so both dimensions come from the same measurement. A missing element measures
 * as nothing.
 * @param {Element|null} element
 * @returns {{ width: number, height: number }}
 */
function measureBox(element) {
    if (!element) {
        return {
            width: 0,
            height: 0,
        };
    }

    return {
        width: element.clientWidth,
        height: element.clientHeight,
    };
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

    // The page can be hidden after the frame is requested, and a hidden page runs no frames, so a timer
    // releases the sample too. Whichever arrives second finds the promise already settled.
    return new Promise((resolve) => {
        const timer = setTimeout(resolve, HIDDEN_SAMPLE_MS);

        requestAnimationFrame(() => {
            clearTimeout(timer);
            resolve();
        });
    });
}

function isPageHidden() {
    return typeof document !== 'undefined' && document.hidden === true;
}

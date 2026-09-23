// Converts wheel events into whole steps of terminal scrolling. The terminal scrolls a step at a time,
// while a trackpad reports a dense stream of small pixel deltas, so the fraction of a step one event
// leaves over carries into the next rather than being rounded away.

// A wheel delta is measured in pixels, lines or pages, depending on the device and the browser.
const PIXEL_DELTA_MODE = 0;
const PAGE_DELTA_MODE = 2;

// A wheel reports one notch at a time, where a trackpad reports a continuous stream while the finger
// moves. An event this soon after the last one belongs to a stream. Distance does not tell the two apart:
// the platform accelerates a fast swipe well past the distance a notch reports. Kept under the gap a
// spun wheel can reach, so a fast wheel is not mistaken for a finger and damped.
const GESTURE_GAP_MS = 40;

// A finger covers far more ground than the wheel notches it stands in for, so its travel is scaled back
// to reach a comparable scroll distance.
const TRACKPAD_SCALE = 0.3;

// Acceleration can put tens of lines of travel in a single event, which reads as a jump rather than a
// scroll, so no one event is allowed to move more than this.
const MAX_LINES_PER_EVENT = 3;

/**
 * Creates a counter that converts a gesture's wheel events into whole steps of scrolling, where a step is
 * one line by default and `linesPerStep` lines otherwise. The counter keeps the leftover fraction of a
 * step between events, so a trackpad scrolls by the distance the finger actually travelled instead of a
 * step per event.
 */
export function createWheelStepCounter() {
    let partialSteps = 0;
    // No previous event, so the first one of a session is never taken for a continuation.
    let previousTimestamp = Number.NEGATIVE_INFINITY;

    return function countSteps(event, terminalMetrics) {
        const lineHeight = terminalMetrics.lineHeight;

        if (event.deltaY === 0 ||
            lineHeight <= 0) {
            return 0;
        }

        const isGestureStream = event.timeStamp - previousTimestamp < GESTURE_GAP_MS;
        previousTimestamp = event.timeStamp;

        let lines = event.deltaY * terminalMetrics.sensitivity;

        if (event.deltaMode === PIXEL_DELTA_MODE) {
            lines /= lineHeight;
            if (isGestureStream) {
                lines *= TRACKPAD_SCALE;
            }
            lines = Math.max(-MAX_LINES_PER_EVENT, Math.min(MAX_LINES_PER_EVENT, lines));
        } else if (event.deltaMode === PAGE_DELTA_MODE) {
            lines *= terminalMetrics.rows;
        }

        const linesPerStep = terminalMetrics.linesPerStep || 1;
        partialSteps += lines / linesPerStep;

        const wholeSteps = Math.trunc(partialSteps);
        partialSteps -= wholeSteps;

        return wholeSteps;
    };
}

// Notches leave on a clock rather than in the bursts a trackpad delivers them in. A swipe arrives as a
// dense run of events a few milliseconds apart, and the terminal passes each notch straight to whatever
// is running, so without this the run lands as one jump.
const NOTCH_INTERVAL_MS = 60;

// The most notches the queue will hold. A swipe that outruns the clock is capped here rather than
// scrolling on after the finger has stopped.
const MAX_PENDING_NOTCHES = 8;

/**
 * Creates a pacer that releases queued notches one at a time on a fixed interval, calling emitNotch with
 * 1 or -1 for each. The first notch of a gesture goes out at once, so scrolling still starts on the
 * movement rather than on the clock.
 */
export function createNotchPacer(emitNotch) {
    let pendingNotches = 0;
    let intervalId = 0;

    function releaseNotch() {
        if (pendingNotches === 0) {
            clearInterval(intervalId);
            intervalId = 0;

            return;
        }

        const direction = pendingNotches > 0 ? 1 : -1;
        pendingNotches -= direction;
        emitNotch(direction);
    }

    return {
        queue(notches) {
            if (notches === 0) {
                return;
            }

            // Turning back replaces what is queued, so a reversal scrolls the other way at once rather
            // than waiting out the notches already counted.
            if (pendingNotches !== 0 &&
                (notches > 0) !== (pendingNotches > 0)) {
                pendingNotches = 0;
            }

            const queuedNotches = pendingNotches + notches;
            pendingNotches = Math.max(-MAX_PENDING_NOTCHES, Math.min(MAX_PENDING_NOTCHES, queuedNotches));

            if (intervalId === 0) {
                releaseNotch();
                if (pendingNotches !== 0) {
                    intervalId = setInterval(releaseNotch, NOTCH_INTERVAL_MS);
                }
            }
        },
    };
}

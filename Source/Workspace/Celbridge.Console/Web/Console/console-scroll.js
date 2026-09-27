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

/**
 * The wheel scaling for WebView2 on Windows, which reports a notch as 100 pixels and a touchpad in the
 * same inflated units.
 */
export const DEFAULT_WHEEL_PROFILE = {
    // Terminal lines are shorter than the document lines a browser assumes, so a notch of a wheel would
    // otherwise cover twice the ground here that it covers elsewhere.
    sensitivity: 0.5,
    // A finger covers far more ground than the wheel notches it stands in for, so its travel is scaled
    // back to reach a comparable scroll distance.
    trackpadScale: 0.3,
    // Acceleration can put tens of lines of travel in a single event, which reads as a jump rather than
    // a scroll, so no one event is allowed to move more than this.
    maxLinesPerEvent: 3,
    // A TUI scrolls about three lines for each wheel event it is sent, so one forwarded event stands in
    // for that much travel.
    linesPerForwardedEvent: 3,
};

/**
 * The wheel scaling for WebKit on macOS, which reports the distance native content scrolls with the
 * system's acceleration already applied, so the terminal follows it one to one.
 */
export const MACOS_WHEEL_PROFILE = {
    sensitivity: 1,
    trackpadScale: 1,
    maxLinesPerEvent: Number.POSITIVE_INFINITY,
    // One wheel event for each line of travel, as native macOS terminals send. The TUI sets its own speed.
    linesPerForwardedEvent: 1,
};

/**
 * Creates a counter that converts a gesture's wheel events into whole steps of scrolling, where a step is
 * one line by default and `linesPerStep` lines otherwise. The counter keeps the leftover fraction of a
 * step between events, so a trackpad scrolls by the distance the finger actually travelled instead of a
 * step per event. `wheelProfile` scales the deltas to the platform's units.
 */
export function createWheelStepCounter(wheelProfile) {
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

        let lines = event.deltaY * wheelProfile.sensitivity;

        if (event.deltaMode === PIXEL_DELTA_MODE) {
            const maxLines = wheelProfile.maxLinesPerEvent;
            lines /= lineHeight;
            if (isGestureStream) {
                lines *= wheelProfile.trackpadScale;
            }
            lines = Math.max(-maxLines, Math.min(maxLines, lines));
        } else if (event.deltaMode === PAGE_DELTA_MODE) {
            lines *= terminalMetrics.rows;
        }

        const linesPerStep = terminalMetrics.linesPerStep || 1;
        partialSteps += lines / linesPerStep;

        const wholeSteps = Math.trunc(partialSteps);
        if (wholeSteps !== 0) {
            partialSteps -= wholeSteps;

            return wholeSteps;
        }

        if (isGestureStream) {
            return 0;
        }

        // A notch always moves at least one step. macOS reports a slow notch as a tenth of a line.
        partialSteps = 0;

        return Math.sign(lines);
    };
}

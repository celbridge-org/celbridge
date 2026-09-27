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

// WebKit on macOS reports a wheel notch as the system's accelerated line count times a 40 pixel line
// step, and the system counts in steps of a fixed-point tenth of a line, so a notch's delta is always a
// whole multiple of this. A trackpad's deltas do not land on one.
const MACOS_NOTCH_DELTA = 40 * 6554 / 65536;

/**
 * The wheel scaling for WebView2 on Windows, which reports a notch as 100 pixels and a touchpad in the
 * same inflated units.
 */
export const DEFAULT_WHEEL_PROFILE = {
    // Scrolling the terminal's own scrollback.
    scrollback: {
        // Terminal lines are shorter than the document lines a browser assumes, so a notch of a wheel
        // would otherwise cover twice the ground here that it covers elsewhere.
        sensitivity: 0.5,
        // A finger covers far more ground than the wheel notches it stands in for, so its travel is
        // scaled back to reach a comparable scroll distance.
        trackpadScale: 0.3,
        // Acceleration can put tens of lines of travel in a single event, which reads as a jump rather
        // than a scroll, so no one event is allowed to move more than this.
        maxLinesPerEvent: 3,
        notchDelta: 0,
        stepPixels: 0,
    },
    // Forwarding the wheel to a TUI, one event per notch of travel as Windows Terminal sends. The TUI
    // sets its own speed.
    forwarding: {
        sensitivity: 1,
        trackpadScale: 1,
        maxLinesPerEvent: Number.POSITIVE_INFINITY,
        notchDelta: 0,
        stepPixels: 100,
    },
};

// WebKit on macOS reports the distance native content scrolls with the system's acceleration already
// applied, so the scrollback follows it one to one, and a TUI is sent one event per line of travel as
// native macOS terminals send. A slow notch reports a tenth of a line, so a notch is always given a step.
const MACOS_WHEEL_SCALE = {
    sensitivity: 1,
    trackpadScale: 1,
    maxLinesPerEvent: Number.POSITIVE_INFINITY,
    notchDelta: MACOS_NOTCH_DELTA,
    stepPixels: 0,
};

/**
 * The wheel scaling for WebKit on macOS.
 */
export const MACOS_WHEEL_PROFILE = {
    scrollback: MACOS_WHEEL_SCALE,
    forwarding: MACOS_WHEEL_SCALE,
};

/**
 * Creates a counter that converts a gesture's wheel events into whole steps, where a step is one line,
 * or `stepPixels` of travel when the scale sets it. The counter keeps the leftover fraction of a step
 * between events, so a trackpad scrolls by the distance the finger actually travelled instead of a step
 * per event. `wheelScale` is one of a profile's scales.
 */
export function createWheelStepCounter(wheelScale) {
    let partialSteps = 0;
    // No previous event, so the first one of a session is never taken for a continuation.
    let previousTimestamp = Number.NEGATIVE_INFINITY;

    return function countSteps(event, terminalMetrics) {
        // Every event keeps a gesture going, including one that only moves sideways.
        const isGestureStream = event.timeStamp - previousTimestamp < GESTURE_GAP_MS;
        previousTimestamp = event.timeStamp;

        const lineHeight = terminalMetrics.lineHeight;

        if (event.deltaY === 0 ||
            lineHeight <= 0) {
            return 0;
        }

        let lines = event.deltaY * wheelScale.sensitivity;
        const isNotch = event.deltaMode === PIXEL_DELTA_MODE &&
            wheelScale.notchDelta > 0 &&
            event.deltaY % wheelScale.notchDelta === 0;

        if (event.deltaMode === PIXEL_DELTA_MODE) {
            // No one event moves more than a screen, whatever the scale allows.
            const maxLines = Math.min(wheelScale.maxLinesPerEvent, terminalMetrics.rows);
            lines /= lineHeight;
            if (isGestureStream) {
                lines *= wheelScale.trackpadScale;
            }
            lines = Math.max(-maxLines, Math.min(maxLines, lines));
        } else if (event.deltaMode === PAGE_DELTA_MODE) {
            lines *= terminalMetrics.rows;
        }

        // Turning back starts the count afresh, so the leftover from the other direction does not hold
        // up the reversal.
        if (partialSteps * lines < 0) {
            partialSteps = 0;
        }

        const linesPerStep = wheelScale.stepPixels > 0 ? wheelScale.stepPixels / lineHeight : 1;
        partialSteps += lines / linesPerStep;

        const wholeSteps = Math.trunc(partialSteps);
        if (wholeSteps !== 0) {
            partialSteps -= wholeSteps;

            return wholeSteps;
        }

        if (!isNotch) {
            return 0;
        }

        partialSteps = 0;

        return Math.sign(lines);
    };
}

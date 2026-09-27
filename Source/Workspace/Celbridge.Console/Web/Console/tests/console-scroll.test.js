import { describe, it, expect } from 'vitest';
import {
    createWheelStepCounter,
    DEFAULT_WHEEL_PROFILE,
    MACOS_WHEEL_PROFILE,
} from '../console-scroll.js';

const PIXEL_DELTA_MODE = 0;
const LINE_DELTA_MODE = 1;
const PAGE_DELTA_MODE = 2;

// Far enough apart that each event reads as its own notch rather than part of a gesture.
const NOTCH_GAP_MS = 500;

// The default profile at a sensitivity of one, so the expected counts read as raw lines.
const UNIT_PROFILE = { ...DEFAULT_WHEEL_PROFILE, sensitivity: 1 };

function wheelEvent(deltaY, timeStamp, deltaMode = PIXEL_DELTA_MODE) {
    return { deltaY, timeStamp, deltaMode };
}

function terminalMetrics(overrides = {}) {
    return {
        lineHeight: 20,
        rows: 24,
        ...overrides,
    };
}

// Plays a continuous gesture: events a few milliseconds apart, as a trackpad reports them.
function playGesture(countSteps, metrics, deltaY, eventCount, startTime = 10000) {
    let total = 0;
    for (let event = 0; event < eventCount; event++) {
        total += countSteps(wheelEvent(deltaY, startTime + event * 8), metrics);
    }

    return total;
}

describe('createWheelStepCounter', () => {
    it('converts an isolated wheel notch into the lines its pixels cover', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(60, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('scrolls up for a negative delta', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(-60, NOTCH_GAP_MS), terminalMetrics())).toBe(-3);
    });

    it('damps a continuous trackpad stream but not isolated wheel notches', () => {
        const wheel = createWheelStepCounter(UNIT_PROFILE);
        const trackpad = createWheelStepCounter(UNIT_PROFILE);
        const metrics = terminalMetrics();

        // Same delta, same number of events: only the cadence differs. 43px is 2.15 lines here, so the
        // wheel covers 21.5 lines and the trackpad, damped after its first event, covers under 8.
        let wheelTotal = 0;
        for (let event = 0; event < 10; event++) {
            wheelTotal += wheel(wheelEvent(43, (event + 1) * NOTCH_GAP_MS), metrics);
        }
        const trackpadTotal = playGesture(trackpad, metrics, 43, 10);

        expect(wheelTotal).toBe(21);
        expect(trackpadTotal).toBe(7);
    });

    it('moves a lone notch one line even when it reports less than a line', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(4, NOTCH_GAP_MS), terminalMetrics())).toBe(1);
        expect(countSteps(wheelEvent(-4, NOTCH_GAP_MS * 2), terminalMetrics())).toBe(-1);
    });

    it('moves a lone notch in its own direction whatever the last one left over', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);
        const metrics = terminalMetrics();

        // 38px leaves nine tenths of a line over in the downward direction.
        expect(countSteps(wheelEvent(38, NOTCH_GAP_MS), metrics)).toBe(1);
        expect(countSteps(wheelEvent(-4, NOTCH_GAP_MS * 2), metrics)).toBe(-1);
    });

    it('holds a slow gesture back instead of scrolling a line per event', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        // The first event reads as a notch and moves a line. The nine after it travel half a line.
        expect(playGesture(countSteps, terminalMetrics(), 4, 10)).toBe(1);
    });

    it('scrolls on once a gesture has travelled a further full line', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(playGesture(countSteps, terminalMetrics(), 4, 20)).toBe(2);
    });

    it('caps a single accelerated event so it scrolls rather than jumps', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        // 1200px is 60 lines of raw travel; the cap keeps it to three.
        expect(countSteps(wheelEvent(1200, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('carries the leftover fraction of a line into the next event', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);
        const metrics = terminalMetrics();

        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS), metrics)).toBe(2);
        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS * 2), metrics)).toBe(3);
    });

    it('counts notches rather than lines when a step spans several lines', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);
        const metrics = terminalMetrics({ linesPerStep: 3 });

        // Three capped events are nine lines of travel, which is three notches.
        let notches = 0;
        for (let event = 0; event < 3; event++) {
            notches += countSteps(wheelEvent(1200, NOTCH_GAP_MS * (event + 1)), metrics);
        }

        expect(notches).toBe(3);
    });

    it('forwards a notch for every lone wheel notch when a notch spans several lines', () => {
        const countSteps = createWheelStepCounter(DEFAULT_WHEEL_PROFILE);
        const metrics = terminalMetrics({ lineHeight: 18, linesPerStep: 3 });

        // A 100px notch covers just under three lines at this line height.
        let notches = 0;
        for (let event = 0; event < 5; event++) {
            notches += countSteps(wheelEvent(100, NOTCH_GAP_MS * (event + 1)), metrics);
        }

        expect(notches).toBe(5);
    });

    it('scales the travel by the profile sensitivity', () => {
        const countSteps = createWheelStepCounter({ ...UNIT_PROFILE, sensitivity: 0.5 });

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics())).toBe(2);
    });

    it('takes a line mode delta as lines already', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(3, NOTCH_GAP_MS, LINE_DELTA_MODE), terminalMetrics())).toBe(3);
    });

    it('scrolls a page mode delta by the rows on screen, past the per-event cap', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(1, NOTCH_GAP_MS, PAGE_DELTA_MODE), terminalMetrics())).toBe(24);
    });

    it('scrolls nothing for a delta of zero', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(0, NOTCH_GAP_MS), terminalMetrics())).toBe(0);
    });

    it('scrolls nothing when the line height is not measurable', () => {
        const countSteps = createWheelStepCounter(UNIT_PROFILE);

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics({ lineHeight: 0 }))).toBe(0);
    });
});

describe('createWheelStepCounter with the macOS profile', () => {
    it('moves a slow notch one line', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE);

        // macOS reports a slow notch as a tenth of WebKit's 40px line step.
        expect(countSteps(wheelEvent(4.000244140625, NOTCH_GAP_MS), terminalMetrics())).toBe(1);
    });

    it('scrolls a trackpad gesture by the distance the finger travelled', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE);

        // 40 events of 10px are 400px, which is twenty lines.
        expect(playGesture(countSteps, terminalMetrics(), 10, 40)).toBe(20);
    });

    it('lets a fast swipe move more than a few lines in one event', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE);

        expect(playGesture(countSteps, terminalMetrics(), 200, 2)).toBe(20);
    });
});

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

// macOS reports a slow notch as a fixed-point tenth of WebKit's 40px line step.
const MACOS_SLOW_NOTCH = 40 * 6554 / 65536;

// The Windows scrollback scale at a sensitivity of one, so the expected counts read as raw lines.
const UNIT_SCALE = { ...DEFAULT_WHEEL_PROFILE.scrollback, sensitivity: 1 };

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

// Plays a run of equal events. The default gap is a trackpad's, a few milliseconds.
function playGesture(countSteps, metrics, deltaY, eventCount, gapMs = 8, startTime = 10000) {
    let total = 0;
    for (let event = 0; event < eventCount; event++) {
        total += countSteps(wheelEvent(deltaY, startTime + event * gapMs), metrics);
    }

    return total;
}

describe('createWheelStepCounter', () => {
    it('converts an isolated wheel notch into the lines its pixels cover', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(60, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('scrolls up for a negative delta', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(-60, NOTCH_GAP_MS), terminalMetrics())).toBe(-3);
    });

    it('damps a continuous trackpad stream but not isolated wheel notches', () => {
        const wheel = createWheelStepCounter(UNIT_SCALE);
        const trackpad = createWheelStepCounter(UNIT_SCALE);
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

    it('holds a gesture below one line back instead of scrolling a line per event', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(playGesture(countSteps, terminalMetrics(), 4, 10)).toBe(0);
    });

    it('scrolls once a gesture has travelled a full line', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(playGesture(countSteps, terminalMetrics(), 4, 15)).toBe(1);
    });

    it('scrolls widely spaced small events by their travel rather than a line each', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        // Eight events of a quarter line each, too far apart to count as a stream.
        expect(playGesture(countSteps, terminalMetrics(), 5, 8, 60)).toBe(2);
    });

    it('caps a single accelerated event so it scrolls rather than jumps', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        // 1200px is 60 lines of raw travel. The cap keeps it to three.
        expect(countSteps(wheelEvent(1200, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('moves no more than a screen in one event, whatever the scale allows', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        // 2000px is 100 lines, on a screen of 24 rows.
        expect(countSteps(wheelEvent(2000, NOTCH_GAP_MS), terminalMetrics())).toBe(24);
    });

    it('carries the leftover fraction of a line into the next event', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);
        const metrics = terminalMetrics();

        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS), metrics)).toBe(2);
        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS * 2), metrics)).toBe(3);
    });

    it('drops the leftover fraction when the direction reverses', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);
        const metrics = terminalMetrics();

        // 38px leaves nine tenths of a line over downward, which would swallow the 1.2 lines upward.
        expect(countSteps(wheelEvent(38, NOTCH_GAP_MS), metrics)).toBe(1);
        expect(countSteps(wheelEvent(-24, NOTCH_GAP_MS * 2), metrics)).toBe(-1);
    });

    it('keeps a gesture going through events that only move sideways', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);
        const metrics = terminalMetrics();

        // The sideways event sits between two vertical ones 60ms apart, so the second is still damped.
        expect(countSteps(wheelEvent(20, 10000), metrics)).toBe(1);
        expect(countSteps(wheelEvent(0, 10030), metrics)).toBe(0);
        expect(countSteps(wheelEvent(20, 10060), metrics)).toBe(0);
    });

    it('counts steps of a fixed distance when the scale sets one', () => {
        const countSteps = createWheelStepCounter({ ...UNIT_SCALE, stepPixels: 60 });
        const metrics = terminalMetrics();

        // Three capped events are nine lines of travel, which is three steps of 60px.
        let steps = 0;
        for (let event = 0; event < 3; event++) {
            steps += countSteps(wheelEvent(1200, NOTCH_GAP_MS * (event + 1)), metrics);
        }

        expect(steps).toBe(3);
    });

    it('scales the travel by the scale sensitivity', () => {
        const countSteps = createWheelStepCounter({ ...UNIT_SCALE, sensitivity: 0.5 });

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics())).toBe(2);
    });

    it('takes a line mode delta as lines already', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(3, NOTCH_GAP_MS, LINE_DELTA_MODE), terminalMetrics())).toBe(3);
    });

    it('scrolls a page mode delta by the rows on screen, past the per-event cap', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(1, NOTCH_GAP_MS, PAGE_DELTA_MODE), terminalMetrics())).toBe(24);
    });

    it('scrolls nothing for a delta of zero', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(0, NOTCH_GAP_MS), terminalMetrics())).toBe(0);
    });

    it('scrolls nothing when the line height is not measurable', () => {
        const countSteps = createWheelStepCounter(UNIT_SCALE);

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics({ lineHeight: 0 }))).toBe(0);
    });
});

describe('createWheelStepCounter with the Windows forwarding scale', () => {
    it('forwards one event per wheel notch', () => {
        const countSteps = createWheelStepCounter(DEFAULT_WHEEL_PROFILE.forwarding);

        expect(playGesture(countSteps, terminalMetrics(), 100, 5, NOTCH_GAP_MS)).toBe(5);
    });

    it('forwards a high-resolution wheel once per notch of travel', () => {
        const countSteps = createWheelStepCounter(DEFAULT_WHEEL_PROFILE.forwarding);

        // Eight quarter notches, far enough apart to read as separate events.
        expect(playGesture(countSteps, terminalMetrics(), 25, 8, 60)).toBe(2);
    });

    it('forwards a touchpad undamped, once per notch of travel', () => {
        const countSteps = createWheelStepCounter(DEFAULT_WHEEL_PROFILE.forwarding);

        expect(playGesture(countSteps, terminalMetrics(), 20, 10)).toBe(2);
    });
});

describe('createWheelStepCounter with the macOS profile', () => {
    it('moves a slow notch one line', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        expect(countSteps(wheelEvent(MACOS_SLOW_NOTCH, NOTCH_GAP_MS), terminalMetrics())).toBe(1);
    });

    it('moves each notch of a spun wheel at least one line', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        // Three tenths of a line step is 0.6 lines here, and the notches arrive as a stream.
        expect(playGesture(countSteps, terminalMetrics(), 3 * MACOS_SLOW_NOTCH, 5, 30)).toBe(5);
    });

    it('does not take a slow trackpad drag for a run of notches', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        // Twenty 1px events, widely spaced, are one line of travel.
        expect(playGesture(countSteps, terminalMetrics(), 1, 20, 60)).toBe(1);
    });

    it('scrolls a trackpad gesture by the distance the finger travelled', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        // 40 events of 10px are 400px, which is twenty lines.
        expect(playGesture(countSteps, terminalMetrics(), 10, 40)).toBe(20);
    });

    it('lets a fast swipe move more than a few lines in one event', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.scrollback);

        expect(playGesture(countSteps, terminalMetrics(), 200, 2)).toBe(20);
    });

    it('forwards one event per line of travel', () => {
        const countSteps = createWheelStepCounter(MACOS_WHEEL_PROFILE.forwarding);

        expect(playGesture(countSteps, terminalMetrics(), 36, 10)).toBe(18);
    });
});

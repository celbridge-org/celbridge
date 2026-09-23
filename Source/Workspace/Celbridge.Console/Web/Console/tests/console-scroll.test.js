import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createWheelStepCounter, createNotchPacer } from '../console-scroll.js';

const PIXEL_DELTA_MODE = 0;
const LINE_DELTA_MODE = 1;
const PAGE_DELTA_MODE = 2;

// Far enough apart that each event reads as its own notch rather than part of a gesture.
const NOTCH_GAP_MS = 500;

function wheelEvent(deltaY, timeStamp, deltaMode = PIXEL_DELTA_MODE) {
    return { deltaY, timeStamp, deltaMode };
}

function terminalMetrics(overrides = {}) {
    return {
        lineHeight: 20,
        rows: 24,
        sensitivity: 1,
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
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(60, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('scrolls up for a negative delta', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(-60, NOTCH_GAP_MS), terminalMetrics())).toBe(-3);
    });

    it('damps a continuous trackpad stream but not isolated wheel notches', () => {
        const wheel = createWheelStepCounter();
        const trackpad = createWheelStepCounter();
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
        const countSteps = createWheelStepCounter();

        expect(playGesture(countSteps, terminalMetrics(), 4, 10)).toBe(0);
    });

    it('scrolls once a trackpad gesture has travelled a full line', () => {
        const countSteps = createWheelStepCounter();

        expect(playGesture(countSteps, terminalMetrics(), 4, 15)).toBe(1);
    });

    it('caps a single accelerated event so it scrolls rather than jumps', () => {
        const countSteps = createWheelStepCounter();

        // 1200px is 60 lines of raw travel; the cap keeps it to three.
        expect(countSteps(wheelEvent(1200, NOTCH_GAP_MS), terminalMetrics())).toBe(3);
    });

    it('carries the leftover fraction of a line into the next event', () => {
        const countSteps = createWheelStepCounter();
        const metrics = terminalMetrics();

        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS), metrics)).toBe(2);
        expect(countSteps(wheelEvent(50, NOTCH_GAP_MS * 2), metrics)).toBe(3);
    });

    it('counts notches rather than lines when a step spans several lines', () => {
        const countSteps = createWheelStepCounter();
        const metrics = terminalMetrics({ linesPerStep: 3 });

        // Three capped events are nine lines of travel, which is three notches.
        let notches = 0;
        for (let event = 0; event < 3; event++) {
            notches += countSteps(wheelEvent(1200, NOTCH_GAP_MS * (event + 1)), metrics);
        }

        expect(notches).toBe(3);
    });

    it('scales the travel by the terminal sensitivity', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics({ sensitivity: 0.5 }))).toBe(2);
    });

    it('takes a line mode delta as lines already', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(3, NOTCH_GAP_MS, LINE_DELTA_MODE), terminalMetrics())).toBe(3);
    });

    it('scrolls a page mode delta by the rows on screen, past the per-event cap', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(1, NOTCH_GAP_MS, PAGE_DELTA_MODE), terminalMetrics())).toBe(24);
    });

    it('scrolls nothing for a delta of zero', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(0, NOTCH_GAP_MS), terminalMetrics())).toBe(0);
    });

    it('scrolls nothing when the line height is not measurable', () => {
        const countSteps = createWheelStepCounter();

        expect(countSteps(wheelEvent(100, NOTCH_GAP_MS), terminalMetrics({ lineHeight: 0 }))).toBe(0);
    });
});

describe('createNotchPacer', () => {
    const NOTCH_INTERVAL_MS = 60;

    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('releases the first notch of a gesture at once', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        pacer.queue(1);

        expect(released).toEqual([1]);
    });

    it('spreads a burst out over the interval rather than releasing it at once', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        pacer.queue(4);
        expect(released).toEqual([1]);

        vi.advanceTimersByTime(NOTCH_INTERVAL_MS);
        expect(released).toEqual([1, 1]);

        vi.advanceTimersByTime(NOTCH_INTERVAL_MS * 2);
        expect(released).toEqual([1, 1, 1, 1]);
    });

    it('stops releasing once the queue is empty', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        pacer.queue(2);
        vi.advanceTimersByTime(NOTCH_INTERVAL_MS * 10);

        expect(released).toEqual([1, 1]);
    });

    it('releases downward notches as a negative direction', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        pacer.queue(-2);
        vi.advanceTimersByTime(NOTCH_INTERVAL_MS * 4);

        expect(released).toEqual([-1, -1]);
    });

    it('caps the queue so a fast swipe does not scroll on after it ends', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        // Far more than the queue holds, as a fast swipe delivers.
        for (let burst = 0; burst < 10; burst++) {
            pacer.queue(5);
        }
        vi.advanceTimersByTime(NOTCH_INTERVAL_MS * 100);

        expect(released.length).toBeLessThanOrEqual(9);
    });

    it('drops what is queued when the gesture reverses', () => {
        const released = [];
        const pacer = createNotchPacer((direction) => released.push(direction));

        pacer.queue(5);
        expect(released).toEqual([1]);

        pacer.queue(-1);
        vi.advanceTimersByTime(NOTCH_INTERVAL_MS * 10);

        expect(released).toEqual([1, -1]);
    });
});

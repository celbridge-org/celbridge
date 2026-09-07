// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { attachSplitter } from '../ui/splitter.js';

// jsdom raises plain Events for pointer types, so the pointer fields the module reads are set by hand.
function firePointerEvent(target, type, init = {}) {
    const event = new Event(type, { bubbles: true });
    Object.assign(event, { button: 0, clientX: 0, pointerId: 1, ...init });
    target.dispatchEvent(event);
}

describe('attachSplitter', () => {
    let splitter;
    let callbacks;

    beforeEach(() => {
        splitter = document.createElement('div');
        document.body.appendChild(splitter);
        splitter.setPointerCapture = vi.fn();
        splitter.releasePointerCapture = vi.fn();
        callbacks = {
            onDragStart: vi.fn(),
            onDrag: vi.fn(),
            onReset: vi.fn(),
            isEnabled: vi.fn().mockReturnValue(true)
        };
        attachSplitter(splitter, callbacks);
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('no-ops when the element is null', () => {
        expect(() => attachSplitter(null, callbacks)).not.toThrow();
    });

    it('reports the delta from the drag start', () => {
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        expect(callbacks.onDragStart).toHaveBeenCalled();
        expect(splitter.classList.contains('dragging')).toBe(true);

        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(callbacks.onDrag).toHaveBeenCalledWith(100);

        firePointerEvent(window, 'pointermove', { clientX: 400 });
        expect(callbacks.onDrag).toHaveBeenLastCalledWith(-100);
    });

    it('ignores a pointerdown while isEnabled is false', () => {
        callbacks.isEnabled.mockReturnValue(false);
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(callbacks.onDragStart).not.toHaveBeenCalled();
        expect(callbacks.onDrag).not.toHaveBeenCalled();
    });

    it('ignores a non-primary button', () => {
        firePointerEvent(splitter, 'pointerdown', { clientX: 500, button: 2 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(callbacks.onDrag).not.toHaveBeenCalled();
    });

    it('stops dragging on pointerup', () => {
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointerup', { pointerId: 1 });
        expect(splitter.classList.contains('dragging')).toBe(false);

        firePointerEvent(window, 'pointermove', { clientX: 700 });
        expect(callbacks.onDrag).not.toHaveBeenCalled();
    });

    it('stops dragging on pointercancel', () => {
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointercancel', { pointerId: 1 });
        expect(splitter.classList.contains('dragging')).toBe(false);

        firePointerEvent(window, 'pointermove', { clientX: 700 });
        expect(callbacks.onDrag).not.toHaveBeenCalled();
    });

    it('resets on a double click', () => {
        splitter.dispatchEvent(new Event('dblclick'));
        expect(callbacks.onReset).toHaveBeenCalled();
    });

    it('suppresses the gesture for 500ms after a reset', () => {
        const nowSpy = vi.spyOn(performance, 'now');

        nowSpy.mockReturnValue(1000);
        splitter.dispatchEvent(new Event('dblclick'));

        nowSpy.mockReturnValue(1400);
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(callbacks.onDrag).not.toHaveBeenCalled();

        nowSpy.mockReturnValue(1600);
        firePointerEvent(splitter, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(callbacks.onDrag).toHaveBeenCalledWith(100);

        nowSpy.mockRestore();
    });
});

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { attachDividerDrag } from '../js/divider-drag.js';

function createViewModeController(overrides = {}) {
    return {
        isSplitMode: vi.fn().mockReturnValue(true),
        getSplitRootWidth: vi.fn().mockReturnValue(1000),
        getEditorPaneWidth: vi.fn().mockReturnValue(500),
        setFlexShare: vi.fn(),
        ...overrides
    };
}

function firePointerEvent(target, type, init = {}) {
    const event = new Event(type, { bubbles: true });
    Object.assign(event, { clientX: 0, pointerId: 1, ...init });
    target.dispatchEvent(event);
}

describe('attachDividerDrag', () => {
    let divider;
    let viewModeController;

    beforeEach(() => {
        divider = document.createElement('div');
        document.body.appendChild(divider);
        divider.setPointerCapture = vi.fn();
        divider.releasePointerCapture = vi.fn();
        viewModeController = createViewModeController();
        attachDividerDrag(divider, viewModeController);
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('no-ops when dividerElement is null', () => {
        expect(() => attachDividerDrag(null, viewModeController)).not.toThrow();
    });

    it('ignores pointerdown when not in Split mode', () => {
        viewModeController.isSplitMode.mockReturnValue(false);
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(viewModeController.setFlexShare).not.toHaveBeenCalled();
    });

    it('updates flex share during drag based on delta and total width', () => {
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        expect(divider.classList.contains('dragging')).toBe(true);

        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(viewModeController.setFlexShare).toHaveBeenCalledWith(0.6);

        firePointerEvent(window, 'pointermove', { clientX: 400 });
        expect(viewModeController.setFlexShare).toHaveBeenLastCalledWith(0.4);
    });

    it('stops dragging on pointerup', () => {
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointerup', { pointerId: 1 });
        expect(divider.classList.contains('dragging')).toBe(false);

        viewModeController.setFlexShare.mockClear();
        firePointerEvent(window, 'pointermove', { clientX: 700 });
        expect(viewModeController.setFlexShare).not.toHaveBeenCalled();
    });

    it('dblclick resets to 50/50', () => {
        divider.dispatchEvent(new Event('dblclick'));
        expect(viewModeController.setFlexShare).toHaveBeenCalledWith(0.5);
    });

    it('suppresses drag for 500ms after a double click', () => {
        const nowSpy = vi.spyOn(performance, 'now');

        nowSpy.mockReturnValue(1000);
        divider.dispatchEvent(new Event('dblclick'));
        expect(viewModeController.setFlexShare).toHaveBeenCalledWith(0.5);
        viewModeController.setFlexShare.mockClear();

        // 400ms later: pointerdown should be swallowed by the debounce.
        nowSpy.mockReturnValue(1400);
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(viewModeController.setFlexShare).not.toHaveBeenCalled();

        // 600ms after the double click: drag works again.
        nowSpy.mockReturnValue(1600);
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(viewModeController.setFlexShare).toHaveBeenCalledWith(0.6);

        nowSpy.mockRestore();
    });

    it('does nothing when getSplitRootWidth returns zero', () => {
        viewModeController.getSplitRootWidth.mockReturnValue(0);
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(viewModeController.setFlexShare).not.toHaveBeenCalled();
    });
});

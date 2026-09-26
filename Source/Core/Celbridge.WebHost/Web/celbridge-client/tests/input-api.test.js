import { describe, it, expect, vi } from 'vitest';
import { InputAPI } from '../api/input-api.js';

function createInput({ isHosted = true } = {}) {
    const transport = {
        isHosted,
        notify: vi.fn(),
        request: vi.fn()
    };
    return new InputAPI(transport);
}

function pressKey(target, key, { ctrlKey = false, shiftKey = false, repeat = false } = {}) {
    const event = new Event('keydown', { bubbles: true, cancelable: true });
    Object.assign(event, { key, ctrlKey, shiftKey, altKey: false, metaKey: false, repeat });
    target.dispatchEvent(event);
    return event;
}

describe('InputAPI reload keys', () => {
    it('keeps F5, Ctrl+R and Ctrl+Shift+R from reloading the page', () => {
        const input = createInput();
        const page = new EventTarget();
        input.watchReloadKeys(page);

        expect(pressKey(page, 'F5').defaultPrevented).toBe(true);
        expect(pressKey(page, 'F5', { ctrlKey: true }).defaultPrevented).toBe(true);
        expect(pressKey(page, 'r', { ctrlKey: true }).defaultPrevented).toBe(true);
        expect(pressKey(page, 'R', { ctrlKey: true, shiftKey: true }).defaultPrevented).toBe(true);
    });

    it('leaves every other key to the page', () => {
        const input = createInput();
        const page = new EventTarget();
        input.watchReloadKeys(page);

        expect(pressKey(page, 'r').defaultPrevented).toBe(false);
        expect(pressKey(page, 'f', { ctrlKey: true }).defaultPrevented).toBe(false);
        expect(pressKey(page, 'F6').defaultPrevented).toBe(false);
    });

    it('hands F5 to the registered handler, and not Ctrl+R', () => {
        const input = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchReloadKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');
        pressKey(page, 'r', { ctrlKey: true });

        expect(handler).toHaveBeenCalledOnce();
    });

    it('calls the handler once for a held F5', () => {
        const input = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchReloadKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');
        const repeated = pressKey(page, 'F5', { repeat: true });

        expect(handler).toHaveBeenCalledOnce();
        expect(repeated.defaultPrevented).toBe(true);
    });

    it('leaves F5 to a control that took it first', () => {
        const input = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        page.addEventListener('keydown', (event) => event.preventDefault());
        input.watchReloadKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');

        expect(handler).not.toHaveBeenCalled();
    });

    it('watches a target once however often it is passed', () => {
        const input = createInput();
        const frameDocument = new EventTarget();
        const handler = vi.fn();
        input.onReloadKey(handler);

        input.watchReloadKeys(frameDocument);
        input.watchReloadKeys(frameDocument);
        pressKey(frameDocument, 'F5');

        expect(handler).toHaveBeenCalledOnce();
    });

    it('leaves the reload keys alone in a page opened outside the host', () => {
        const input = createInput({ isHosted: false });
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchReloadKeys(page);
        input.onReloadKey(handler);

        const event = pressKey(page, 'F5');

        expect(event.defaultPrevented).toBe(false);
        expect(handler).not.toHaveBeenCalled();
    });
});

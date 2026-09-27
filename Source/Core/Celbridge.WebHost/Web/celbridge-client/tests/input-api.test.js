import { describe, it, expect, vi, afterEach } from 'vitest';
import { InputAPI } from '../api/input-api.js';

function createInput({ isHosted = true } = {}) {
    const transport = {
        isHosted,
        notify: vi.fn(),
        request: vi.fn()
    };
    return { input: new InputAPI(transport), transport };
}

function pressKey(target, key, { ctrlKey = false, shiftKey = false, metaKey = false, repeat = false } = {}) {
    const event = new Event('keydown', { bubbles: true, cancelable: true });
    Object.assign(event, { key, ctrlKey, shiftKey, altKey: false, metaKey, repeat });
    target.dispatchEvent(event);
    return event;
}

function shortcutsSent(transport) {
    return transport.notify.mock.calls
        .filter(([method]) => method === 'input/keyboardShortcut')
        .map(([, params]) => params);
}

describe('InputAPI reload keys', () => {
    it('keeps F5, Ctrl+R and Ctrl+Shift+R from reloading the page', () => {
        const { input } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        expect(pressKey(page, 'F5').defaultPrevented).toBe(true);
        expect(pressKey(page, 'F5', { ctrlKey: true }).defaultPrevented).toBe(true);
        expect(pressKey(page, 'r', { ctrlKey: true }).defaultPrevented).toBe(true);
        expect(pressKey(page, 'R', { ctrlKey: true, shiftKey: true }).defaultPrevented).toBe(true);
    });

    it('leaves every other key to the page', () => {
        const { input } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        expect(pressKey(page, 'r').defaultPrevented).toBe(false);
        expect(pressKey(page, 'f', { ctrlKey: true }).defaultPrevented).toBe(false);
        expect(pressKey(page, 'F6').defaultPrevented).toBe(false);
    });

    it('hands F5 to the registered handler, and not Ctrl+R', () => {
        const { input } = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchShortcutKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');
        pressKey(page, 'r', { ctrlKey: true });

        expect(handler).toHaveBeenCalledOnce();
    });

    it('calls the handler once for a held F5', () => {
        const { input } = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchShortcutKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');
        const repeated = pressKey(page, 'F5', { repeat: true });

        expect(handler).toHaveBeenCalledOnce();
        expect(repeated.defaultPrevented).toBe(true);
    });

    it('leaves F5 to a control that took it first', () => {
        const { input } = createInput();
        const page = new EventTarget();
        const handler = vi.fn();
        page.addEventListener('keydown', (event) => event.preventDefault());
        input.watchShortcutKeys(page);
        input.onReloadKey(handler);

        pressKey(page, 'F5');

        expect(handler).not.toHaveBeenCalled();
    });

    it('watches a target once however often it is passed', () => {
        const { input } = createInput();
        const frameDocument = new EventTarget();
        const handler = vi.fn();
        input.onReloadKey(handler);

        input.watchShortcutKeys(frameDocument);
        input.watchShortcutKeys(frameDocument);
        pressKey(frameDocument, 'F5');

        expect(handler).toHaveBeenCalledOnce();
    });

    it('leaves the reload keys alone in a page opened outside the host', () => {
        const { input } = createInput({ isHosted: false });
        const page = new EventTarget();
        const handler = vi.fn();
        input.watchShortcutKeys(page);
        input.onReloadKey(handler);

        const event = pressKey(page, 'F5');

        expect(event.defaultPrevented).toBe(false);
        expect(handler).not.toHaveBeenCalled();
    });
});

describe('InputAPI close shortcuts', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('sends Ctrl+W and Ctrl+Shift+W to the host and keeps them from the page', () => {
        vi.stubGlobal('navigator', { platform: 'Win32' });
        const { input, transport } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        const close = pressKey(page, 'w', { ctrlKey: true });
        const closeAll = pressKey(page, 'W', { ctrlKey: true, shiftKey: true });

        expect(close.defaultPrevented).toBe(true);
        expect(closeAll.defaultPrevented).toBe(true);
        expect(shortcutsSent(transport)).toEqual([
            { key: 'W', ctrlKey: true, shiftKey: false, altKey: false },
            { key: 'W', ctrlKey: true, shiftKey: true, altKey: false }
        ]);
    });

    it('sends a held Ctrl+W once', () => {
        vi.stubGlobal('navigator', { platform: 'Win32' });
        const { input, transport } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        pressKey(page, 'w', { ctrlKey: true });
        pressKey(page, 'w', { ctrlKey: true, repeat: true });

        expect(shortcutsSent(transport)).toHaveLength(1);
    });

    it('leaves Ctrl+W to a control that took it first', () => {
        vi.stubGlobal('navigator', { platform: 'Win32' });
        const { input, transport } = createInput();
        const page = new EventTarget();
        page.addEventListener('keydown', (event) => event.preventDefault());
        input.watchShortcutKeys(page);

        pressKey(page, 'w', { ctrlKey: true });

        expect(shortcutsSent(transport)).toHaveLength(0);
    });

    it('leaves Control+W to the page on macOS, which closes on Command+W', () => {
        vi.stubGlobal('navigator', { platform: 'MacIntel' });
        const { input, transport } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        const event = pressKey(page, 'w', { ctrlKey: true });

        expect(event.defaultPrevented).toBe(false);
        expect(shortcutsSent(transport)).toHaveLength(0);
    });

    it('leaves W without Ctrl to the page', () => {
        vi.stubGlobal('navigator', { platform: 'Win32' });
        const { input, transport } = createInput();
        const page = new EventTarget();
        input.watchShortcutKeys(page);

        const event = pressKey(page, 'w');

        expect(event.defaultPrevented).toBe(false);
        expect(shortcutsSent(transport)).toHaveLength(0);
    });
});

import { describe, it, expect, vi } from 'vitest';
import { ViewModeController, ViewMode } from '../js/view-mode-controller.js';

function createController(onLayoutChanged = () => {}) {
    const splitRoot = document.createElement('div');
    const editorPane = document.createElement('div');
    const previewPane = document.createElement('div');
    const controller = new ViewModeController({
        splitRoot,
        editorPane,
        previewPane,
        onLayoutChanged
    });
    return { controller, splitRoot, editorPane, previewPane };
}

function waitForAnimationFrame() {
    return new Promise((resolve) => requestAnimationFrame(resolve));
}

function parseFlexShare(flexString) {
    return parseFloat(flexString.split(' ')[0]);
}

describe('ViewModeController', () => {
    it('defaults to Source mode', () => {
        const { controller } = createController();
        expect(controller.getMode()).toBe(ViewMode.Source);
        expect(controller.isSplitMode()).toBe(false);
    });

    it('ignores invalid modes', () => {
        const { controller } = createController();
        controller.setMode('invalid');
        expect(controller.getMode()).toBe(ViewMode.Source);
    });

    it('applies flex share in Split mode', () => {
        const { controller, splitRoot, editorPane, previewPane } = createController();
        controller.setMode(ViewMode.Split);

        expect(splitRoot.classList.contains('mode-split')).toBe(true);
        expect(parseFlexShare(editorPane.style.flex)).toBeCloseTo(0.5, 10);
        expect(parseFlexShare(previewPane.style.flex)).toBeCloseTo(0.5, 10);
        expect(controller.isSplitMode()).toBe(true);
    });

    it('clears inline flex when leaving Split for Source', () => {
        const { controller, splitRoot, editorPane, previewPane } = createController();
        controller.setMode(ViewMode.Split);
        controller.setMode(ViewMode.Source);

        expect(splitRoot.classList.contains('mode-split')).toBe(false);
        expect(splitRoot.classList.contains('mode-source')).toBe(true);
        expect(editorPane.style.flex).toBe('');
        expect(previewPane.style.flex).toBe('');
    });

    it('clears inline flex when leaving Split for Preview', () => {
        const { controller, splitRoot, editorPane, previewPane } = createController();
        controller.setMode(ViewMode.Split);
        controller.setMode(ViewMode.Preview);

        expect(splitRoot.classList.contains('mode-preview')).toBe(true);
        expect(editorPane.style.flex).toBe('');
        expect(previewPane.style.flex).toBe('');
    });

    it('fires onLayoutChanged once on next animation frame after setMode', async () => {
        const onLayoutChanged = vi.fn();
        const { controller } = createController(onLayoutChanged);
        controller.setMode(ViewMode.Split);

        expect(onLayoutChanged).not.toHaveBeenCalled();
        await waitForAnimationFrame();
        expect(onLayoutChanged).toHaveBeenCalledOnce();
    });

    it('clamps setFlexShare to [0.1, 0.9]', () => {
        const { controller, editorPane } = createController();
        controller.setMode(ViewMode.Split);

        controller.setFlexShare(0.0);
        expect(parseFlexShare(editorPane.style.flex)).toBeCloseTo(0.1, 10);

        controller.setFlexShare(1.0);
        expect(parseFlexShare(editorPane.style.flex)).toBeCloseTo(0.9, 10);
    });

    it('setFlexShare fires onLayoutChanged synchronously', () => {
        const onLayoutChanged = vi.fn();
        const { controller } = createController(onLayoutChanged);
        controller.setMode(ViewMode.Split);
        onLayoutChanged.mockClear();

        controller.setFlexShare(0.6);
        expect(onLayoutChanged).toHaveBeenCalledOnce();
    });

    it('setFlexShare skips inline flex when mode is not Split', () => {
        // Regression test: restoring state in Preview mode used to leave inline
        // flex styles on the panes because setFlexShare applied unconditionally.
        // The stored share must still round-trip so a later setMode(Split) can
        // apply it — but inline styles must stay clear outside Split.
        const { controller, editorPane, previewPane } = createController();
        controller.setMode(ViewMode.Preview);

        controller.setFlexShare(0.3);

        expect(editorPane.style.flex).toBe('');
        expect(previewPane.style.flex).toBe('');
        expect(controller.getFlexShare()).toBeCloseTo(0.3, 10);
    });

    it('setFlexShare-then-setMode(Split) applies the stored share', () => {
        const { controller, editorPane } = createController();
        controller.setMode(ViewMode.Preview);
        controller.setFlexShare(0.3);

        controller.setMode(ViewMode.Split);

        expect(parseFlexShare(editorPane.style.flex)).toBeCloseTo(0.3, 10);
    });
});

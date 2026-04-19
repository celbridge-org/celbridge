import { describe, it, expect, vi, beforeEach } from 'vitest';
import { PreviewController } from '../preview-controller.js';

// The dynamic import in PreviewController#loadModule is impossible to stub
// without refactoring the controller. Instead we serve a tiny fake preview
// module via a data: URL whose methods proxy to a mutable globalThis object,
// so each test can swap the underlying spies.
function makeFakeRendererUrl() {
    const code = `
        export function initialize(...args) { return globalThis.__fakePreviewModule.initialize(...args); }
        export function render(...args) { return globalThis.__fakePreviewModule.render(...args); }
        export function setBasePath(...args) { return globalThis.__fakePreviewModule.setBasePath(...args); }
        export function setScrollPercentage(...args) { return globalThis.__fakePreviewModule.setScrollPercentage(...args); }
        export function getScrollPercentage(...args) { return globalThis.__fakePreviewModule.getScrollPercentage(...args); }
    `;
    const encoded = Buffer.from(code).toString('base64');
    return `data:text/javascript;base64,${encoded}`;
}

function createFakeModule() {
    return {
        initialize: vi.fn().mockResolvedValue(undefined),
        render: vi.fn(),
        setBasePath: vi.fn(),
        setScrollPercentage: vi.fn().mockReturnValue(true),
        getScrollPercentage: vi.fn().mockReturnValue(0)
    };
}

function waitForAnimationFrame() {
    return new Promise((resolve) => requestAnimationFrame(resolve));
}

describe('PreviewController', () => {
    let fakeModule;

    beforeEach(() => {
        fakeModule = createFakeModule();
        globalThis.__fakePreviewModule = fakeModule;
    });

    it('is inactive before a renderer is set', () => {
        const controller = new PreviewController(document.createElement('iframe'));
        expect(controller.isActive()).toBe(false);
    });

    it('render is a no-op before a module is loaded', () => {
        const controller = new PreviewController(document.createElement('iframe'));
        expect(() => controller.render('# hello')).not.toThrow();
        expect(fakeModule.render).not.toHaveBeenCalled();
    });

    it('setScrollPercentage is safe before a module is loaded', () => {
        const controller = new PreviewController(document.createElement('iframe'));
        expect(() => controller.setScrollPercentage(0.5)).not.toThrow();
    });

    it('becomes active after setRenderer and forwards initialize', async () => {
        const iframe = document.createElement('iframe');
        const callbacks = { onOpenResource: vi.fn() };
        const controller = new PreviewController(iframe, callbacks);

        await controller.setRenderer(makeFakeRendererUrl());

        expect(controller.isActive()).toBe(true);
        expect(fakeModule.initialize).toHaveBeenCalledOnce();
        expect(fakeModule.initialize).toHaveBeenCalledWith(iframe, callbacks);
    });

    it('flushes pending basePath to the module after load', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        controller.setBasePath('/docs/guide/');

        await controller.setRenderer(makeFakeRendererUrl());

        expect(fakeModule.setBasePath).toHaveBeenCalledWith('/docs/guide/');
    });

    it('buffered scroll is replayed after the first render', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        await controller.setRenderer(makeFakeRendererUrl());

        // Buffered: module loaded but nothing has rendered yet.
        controller.setScrollPercentage(0.75);
        expect(fakeModule.setScrollPercentage).not.toHaveBeenCalled();

        controller.render('# hello');
        expect(fakeModule.render).toHaveBeenCalledWith('# hello');

        // Replay is deferred one animation frame so the render can lay out.
        expect(fakeModule.setScrollPercentage).not.toHaveBeenCalled();
        await waitForAnimationFrame();
        expect(fakeModule.setScrollPercentage).toHaveBeenCalledWith(0.75);
    });

    it('setScrollPercentage goes straight to the module after first render', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        await controller.setRenderer(makeFakeRendererUrl());
        controller.render('# hello');

        controller.setScrollPercentage(0.25);
        expect(fakeModule.setScrollPercentage).toHaveBeenCalledWith(0.25);
    });

    it('setRenderer with the same URL is idempotent', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        const url = makeFakeRendererUrl();

        await controller.setRenderer(url);
        await controller.setRenderer(url);

        expect(fakeModule.initialize).toHaveBeenCalledOnce();
    });

    it('setRenderer(null) detaches the module and blanks the iframe', async () => {
        const iframe = document.createElement('iframe');
        const controller = new PreviewController(iframe);
        await controller.setRenderer(makeFakeRendererUrl());
        expect(controller.isActive()).toBe(true);

        await controller.setRenderer(null);

        expect(controller.isActive()).toBe(false);
        expect(iframe.src).toBe('about:blank');
    });
});

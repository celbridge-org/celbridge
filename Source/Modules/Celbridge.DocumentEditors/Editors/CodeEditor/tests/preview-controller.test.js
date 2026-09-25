import { describe, it, expect, vi, beforeEach } from 'vitest';
import { JSDOM, VirtualConsole } from 'jsdom';
import { PreviewController, LinkAction, resolveLinkClick } from '../js/preview-controller.js';

// A renderer of the editor buffer with a source map, like the markdown module.
const bufferRendererExports = [
    'initialize',
    'render',
    'setBasePath',
    'setScrollPercentage',
    'getScrollPercentage',
    'scrollToSourceLine',
    'getTopSourceLine'
];

// A renderer of the saved file, like the HTML module: refreshed rather than rendered, with no source map.
const fileRendererExports = [
    'initialize',
    'render',
    'setBasePath',
    'setScrollPercentage',
    'getScrollPercentage',
    'refresh'
];

// The dynamic import in PreviewController#loadModule is impossible to stub
// without refactoring the controller. Instead we serve a tiny fake preview
// module via a data: URL whose methods proxy to a mutable globalThis object,
// so each test can swap the underlying spies.
function makeFakeRendererUrl(exportNames = bufferRendererExports) {
    const code = exportNames
        .map((name) =>
            `export function ${name}(...args) { return globalThis.__fakePreviewModule.${name}(...args); }`)
        .join('\n');
    const encoded = Buffer.from(code).toString('base64');
    return `data:text/javascript;base64,${encoded}`;
}

function createFakeModule() {
    return {
        initialize: vi.fn().mockResolvedValue(undefined),
        render: vi.fn(),
        setBasePath: vi.fn(),
        setScrollPercentage: vi.fn().mockReturnValue(true),
        getScrollPercentage: vi.fn().mockReturnValue(0),
        scrollToSourceLine: vi.fn().mockReturnValue(true),
        getTopSourceLine: vi.fn().mockReturnValue(null),
        refresh: vi.fn()
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

    it('forwards refresh to a renderer of the file', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        await controller.setRenderer(makeFakeRendererUrl(fileRendererExports));

        controller.refresh('/project/docs/page.html');

        expect(fakeModule.refresh).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('replays a refresh that arrived before the renderer loaded', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        controller.refresh('/project/docs/page.html');

        await controller.setRenderer(makeFakeRendererUrl(fileRendererExports));

        expect(fakeModule.refresh).toHaveBeenCalledOnce();
        expect(fakeModule.refresh).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('ignores refresh for a renderer of the buffer', async () => {
        const controller = new PreviewController(document.createElement('iframe'));
        await controller.setRenderer(makeFakeRendererUrl(bufferRendererExports));

        expect(() => controller.refresh('/project/notes.md')).not.toThrow();
        expect(fakeModule.refresh).not.toHaveBeenCalled();
    });

    it('reports source-line scrolling only for a renderer that maps source lines', async () => {
        const bufferController = new PreviewController(document.createElement('iframe'));
        const fileController = new PreviewController(document.createElement('iframe'));
        expect(bufferController.canScrollToSourceLine()).toBe(false);

        await bufferController.setRenderer(makeFakeRendererUrl(bufferRendererExports));
        await fileController.setRenderer(makeFakeRendererUrl(fileRendererExports));

        expect(bufferController.canScrollToSourceLine()).toBe(true);
        expect(fileController.canScrollToSourceLine()).toBe(false);
    });
});

describe('resolveLinkClick', () => {
    const documentUrl = 'http://127.0.0.1:5000/project/docs/page.html';

    it.each([
        ['#install', 'install'],
        [`${documentUrl}#install`, 'install'],
        ['page.html#install', 'install'],
        ['', '']
    ])('scrolls for %j, which names a place in the page', (href, fragment) => {
        expect(resolveLinkClick(href, documentUrl, documentUrl))
            .toEqual({ action: LinkAction.Scroll, fragment });
    });

    it.each([
        ['guide.html', 'guide.html'],
        ['../index.html', '../index.html'],
        ['/assets/manual.pdf', '/assets/manual.pdf'],
        ['https://example.com/docs', 'https://example.com/docs'],
        ['//example.com/docs', 'http://example.com/docs']
    ])('routes %j to the host', (href, routedHref) => {
        expect(resolveLinkClick(href, documentUrl, documentUrl))
            .toEqual({ action: LinkAction.Route, href: routedHref });
    });

    it.each([
        'mailto:someone@example.com',
        'blob:http://127.0.0.1:5000/0b7c1a54',
        'data:text/plain,hello',
        'javascript:void(0)'
    ])('leaves %j to the page', (href) => {
        expect(resolveLinkClick(href, documentUrl, documentUrl))
            .toEqual({ action: LinkAction.Ignore });
    });

    it('scrolls for a bare fragment however the document sets its base', () => {
        const result = resolveLinkClick('#top', documentUrl, 'https://cdn.example.com/');

        expect(result).toEqual({ action: LinkAction.Scroll, fragment: 'top' });
    });
});

describe('PreviewController link clicks', () => {
    let frameWindow;
    let frameDocument;
    let iframe;
    let onLinkClicked;

    // A stand-in for the preview iframe. Its document needs a real URL, which a jsdom iframe cannot have.
    function loadFrame(bodyHtml) {
        const dom = new JSDOM(`<!DOCTYPE html><html><body>${bodyHtml}</body></html>`, {
            url: 'http://127.0.0.1:5000/project/docs/page.html',
            // Silences jsdom's error that it cannot navigate, raised when a test lets a link click through.
            virtualConsole: new VirtualConsole()
        });
        frameWindow = dom.window;
        frameDocument = dom.window.document;

        const loadListeners = [];
        iframe = {
            contentDocument: frameDocument,
            addEventListener: (type, listener) => {
                if (type === 'load') {
                    loadListeners.push(listener);
                }
            }
        };

        onLinkClicked = vi.fn();
        new PreviewController(iframe, { onLinkClicked });
        loadListeners.forEach((listener) => listener());
    }

    function click(element) {
        const event = new frameWindow.MouseEvent('click', { bubbles: true, cancelable: true });
        element.dispatchEvent(event);
        return event;
    }

    it('routes a link to another file and keeps the preview where it is', () => {
        loadFrame('<a id="link" href="guide.html" target="_blank">Guide</a>');

        const event = click(frameDocument.getElementById('link'));

        expect(event.defaultPrevented).toBe(true);
        expect(onLinkClicked).toHaveBeenCalledWith('guide.html');
    });

    it('scrolls to the place a fragment link names rather than routing it', () => {
        loadFrame('<a id="link" href="#details">Details</a><h2 id="details">Details</h2>');
        const target = frameDocument.getElementById('details');
        target.scrollIntoView = vi.fn();

        const event = click(frameDocument.getElementById('link'));

        expect(event.defaultPrevented).toBe(true);
        expect(target.scrollIntoView).toHaveBeenCalledOnce();
        expect(onLinkClicked).not.toHaveBeenCalled();
    });

    it('finds a named anchor when no element has the id', () => {
        loadFrame('<a id="link" href="#legacy">Legacy</a><a name="legacy"></a>');
        const target = frameDocument.getElementsByName('legacy')[0];
        target.scrollIntoView = vi.fn();

        click(frameDocument.getElementById('link'));

        expect(target.scrollIntoView).toHaveBeenCalledOnce();
    });

    it('lets a download run in the frame, clearing a target that asks for a new window', () => {
        loadFrame('<a id="link" href="report.csv" download target="_blank">Report</a>');
        const link = frameDocument.getElementById('link');

        expect(link.hasAttribute('target')).toBe(false);

        const event = click(link);

        expect(event.defaultPrevented).toBe(false);
        expect(onLinkClicked).not.toHaveBeenCalled();
    });

    it('clears the target of a download link the page adds after loading', () => {
        loadFrame('');
        const link = frameDocument.createElement('a');
        link.href = 'export.zip';
        link.setAttribute('download', '');
        link.target = '_blank';
        frameDocument.body.appendChild(link);

        click(link);

        expect(link.hasAttribute('target')).toBe(false);
    });

    it('leaves a link the page handled itself', () => {
        loadFrame('<a id="link" href="guide.html">Guide</a>');
        const link = frameDocument.getElementById('link');
        link.addEventListener('click', (event) => event.preventDefault());

        click(link);

        expect(onLinkClicked).not.toHaveBeenCalled();
    });

    it('leaves a link with a scheme the host cannot resolve to the page', () => {
        loadFrame('<a id="link" href="mailto:someone@example.com">Mail</a>');

        const event = click(frameDocument.getElementById('link'));

        expect(event.defaultPrevented).toBe(false);
        expect(onLinkClicked).not.toHaveBeenCalled();
    });
});

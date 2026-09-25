import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import vm from 'node:vm';
import { JSDOM } from 'jsdom';

const here = path.dirname(fileURLToPath(import.meta.url));
const shimSource = readFileSync(
    path.join(here, '..', 'core', 'webview-tools-shim.js'),
    'utf-8'
);

const BRIDGE_KEY = Symbol.for('__cel_webview_tools');

// Run the shim in a fresh vm Context so that the non-configurable property
// the shim installs on globalThis does not leak between tests. Inside the
// context, globalThis is the context object itself, so the shim's
// Object.defineProperty(globalThis, ...) writes back to `context`.
function runShim(hostname) {
    const context = { Symbol, Object, location: { hostname } };
    vm.createContext(context);
    vm.runInContext(shimSource, context);
    return context;
}

// Build a JSDOM-backed VM context so the shim can run against a real DOM,
// console, and window. The handlers (getHtml, query, inspect, flushConsole)
// are exercised end-to-end against this context.
function runShimInDom(html, hostname = '127.0.0.1', domOptions = {}) {
    const dom = new JSDOM(html, { url: `https://${hostname}/`, ...domOptions });
    const context = {
        Symbol,
        Object,
        Array,
        JSON,
        Date,
        String,
        Number,
        Error,
        Math,
        NodeFilter: dom.window.NodeFilter,
        Node: dom.window.Node,
        location: dom.window.location,
        document: dom.window.document,
        window: dom.window,
        console: dom.window.console,
        getComputedStyle: dom.window.getComputedStyle.bind(dom.window),
        XMLSerializer: dom.window.XMLSerializer,
        CSS: dom.window.CSS,
        addEventListener: dom.window.addEventListener.bind(dom.window),
        setTimeout: dom.window.setTimeout.bind(dom.window),
        clearTimeout: dom.window.clearTimeout.bind(dom.window)
    };
    vm.createContext(context);
    vm.runInContext(shimSource, context);
    return { context, dom };
}

describe('webview-tools-shim install', () => {
    it('bails on a non-celbridge origin', () => {
        const context = runShim('evil.example.com');
        expect(context[BRIDGE_KEY]).toBeUndefined();
    });

    it('installs the bridge on the loopback origin', () => {
        const context = runShim('127.0.0.1');
        expect(context[BRIDGE_KEY]).toBeDefined();
    });

    it('installs the bridge on a synthetic-origin .celbridge host', () => {
        const context = runShim('my-package.celbridge');
        expect(context[BRIDGE_KEY]).toBeDefined();
    });

    it('bails on a hostname that ends with the literal string celbridge but no dot', () => {
        // Guards against /\.celbridge$/ being relaxed to /celbridge$/ — that would
        // let attackers register e.g. "evilcelbridge".
        const context = runShim('evilcelbridge');
        expect(context[BRIDGE_KEY]).toBeUndefined();
    });

    it('installed bridge exposes registerHandler, getHandler, and invoke', () => {
        const context = runShim('127.0.0.1');
        const bridge = context[BRIDGE_KEY];

        const handler = () => 'result';
        bridge.registerHandler('test.method', handler);

        expect(bridge.getHandler('test.method')).toBe(handler);
        expect(bridge.getHandler('unknown')).toBeNull();
        expect(typeof bridge.invoke).toBe('function');
    });
});

describe('webview-tools-shim console capture', () => {
    it('captures console.log entries via flushConsole', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        context.console.log('hello', 42);
        context.console.warn('uh oh');

        const result = bridge.invoke('flushConsole', '{}');
        expect(result.ok).toBe(true);
        expect(result.value).toHaveLength(2);
        expect(result.value[0].level).toBe('log');
        expect(result.value[0].args).toEqual(['hello', '42']);
        expect(result.value[1].level).toBe('warn');
    });

    it('flushConsole drains the buffer', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        context.console.log('one');
        const first = bridge.invoke('flushConsole', '{}');
        const second = bridge.invoke('flushConsole', '{}');

        expect(first.value).toHaveLength(1);
        expect(second.value).toHaveLength(0);
    });

    it('captures uncaught errors via window.error', () => {
        const { context, dom } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        // Synthesise the event the runtime would dispatch for an uncaught error.
        const evt = new dom.window.ErrorEvent('error', {
            message: 'boom',
            error: new dom.window.Error('boom')
        });
        dom.window.dispatchEvent(evt);

        const result = bridge.invoke('flushConsole', '{}');
        expect(result.value.length).toBeGreaterThanOrEqual(1);
        const errorEntry = result.value.find(e => e.level === 'error');
        expect(errorEntry).toBeDefined();
        expect(errorEntry.args[0]).toContain('boom');
    });

    it('truncates long arguments past the per-arg cap', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        const giant = 'x'.repeat(10000);
        context.console.log(giant);

        const result = bridge.invoke('flushConsole', '{}');
        expect(result.value[0].args[0].length).toBeLessThan(giant.length);
        expect(result.value[0].args[0]).toContain('truncated');
    });
});

describe('webview-tools-shim getHtml handler', () => {
    it('returns the document outerHTML when no selector is given', () => {
        const { context } = runShimInDom('<!doctype html><html><body><div id="main">hi</div></body></html>');
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('getHtml', JSON.stringify({}));
        expect(result.ok).toBe(true);
        expect(result.value.html).toContain('<div id="main">');
    });

    it('redacts script and style bodies', () => {
        const html = '<!doctype html><html><head><style>body{color:red}</style></head><body><script>alert(1)</script></body></html>';
        const { context } = runShimInDom(html);
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('getHtml', JSON.stringify({}));
        expect(result.ok).toBe(true);
        expect(result.value.html).not.toContain('alert(1)');
        expect(result.value.html).not.toContain('color:red');
        expect(result.value.html).toContain('omitted');
    });

    it('reports an error for an unknown selector', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('getHtml', JSON.stringify({ selector: '#no-such-thing' }));
        expect(result.ok).toBe(false);
        expect(result.error).toContain('no element matches');
    });
});

describe('webview-tools-shim query handler', () => {
    it('matches by ARIA role + accessible name', () => {
        const html = '<!doctype html><html><body><button>Save</button><button>Cancel</button></body></html>';
        const { context } = runShimInDom(html);
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('query', JSON.stringify({ role: 'button', name: 'Save' }));
        expect(result.ok).toBe(true);
        expect(result.value.totalMatches).toBe(1);
        expect(result.value.elements[0].accessibleName.toLowerCase()).toContain('save');
    });

    it('matches by visible text', () => {
        const html = '<!doctype html><html><body><span>hello world</span><span>goodbye</span></body></html>';
        const { context } = runShimInDom(html);
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('query', JSON.stringify({ text: 'hello' }));
        expect(result.ok).toBe(true);
        expect(result.value.totalMatches).toBe(1);
    });

    it('matches by CSS selector', () => {
        const html = '<!doctype html><html><body><p class="warn">x</p><p class="warn">y</p></body></html>';
        const { context } = runShimInDom(html);
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('query', JSON.stringify({ selector: '.warn' }));
        expect(result.ok).toBe(true);
        expect(result.value.totalMatches).toBe(2);
        expect(result.value.elements[0].selector).toBeTruthy();
    });

    it('rejects ambiguous mode arguments', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('query', JSON.stringify({ role: 'button', selector: 'button' }));
        expect(result.ok).toBe(false);
        expect(result.error).toContain('exactly one');
    });
});

describe('webview-tools-shim inspect handler', () => {
    it('returns metadata for a matched element', () => {
        const html = '<!doctype html><html><body><button id="go" aria-label="Run task">Go</button></body></html>';
        const { context } = runShimInDom(html);
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('inspect', JSON.stringify({ selector: '#go' }));
        expect(result.ok).toBe(true);
        expect(result.value.tag).toBe('button');
        expect(result.value.role).toBe('button');
        expect(result.value.accessibleName).toBe('Run task');
        expect(result.value.attributes['aria-label']).toBe('Run task');
        expect(result.value.children).toBeDefined();
    });

    it('reports an error for an unknown selector', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('inspect', JSON.stringify({ selector: '#missing' }));
        expect(result.ok).toBe(false);
        expect(result.error).toContain('no element matches');
    });
});

describe('webview-tools-shim invoke envelope', () => {
    it('returns ok:false for an unknown handler', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        const result = bridge.invoke('does-not-exist', '{}');
        expect(result.ok).toBe(false);
        expect(result.error).toContain('no handler');
    });

    it('catches handler exceptions and reports them', () => {
        const { context } = runShimInDom('<!doctype html><html><body></body></html>');
        const bridge = context[BRIDGE_KEY];

        bridge.registerHandler('boom', () => { throw new Error('expected'); });
        const result = bridge.invoke('boom', '{}');
        expect(result.ok).toBe(false);
        expect(result.error).toContain('expected');
    });
});

// A page holding one frame, with the shim running in the page. The frame's content is written into its
// document directly, since the test DOM does not load pages into frames.
function runShimWithFrame({ markContentFrame = true, frameContent = '<h1>Previewed page</h1>' } = {}) {
    const marker = markContentFrame ? ' data-cel-content-frame' : '';
    const html = `<!doctype html><html><body><h1>Editor shell</h1><iframe id="preview"${marker}></iframe></body></html>`;
    const { context, dom } = runShimInDom(html, '127.0.0.1', { runScripts: 'outside-only' });
    const frame = dom.window.document.getElementById('preview');
    frame.contentDocument.body.innerHTML = frameContent;
    return { bridge: context[BRIDGE_KEY], context, dom, frame };
}

function invoke(bridge, handlerName, args = {}) {
    return bridge.invoke(handlerName, JSON.stringify(args));
}

// The test DOM has no layout, so a frame's box and its elements' rectangles are supplied by the test.
function placeFrame(frame, { left, top, width, height }) {
    frame.getBoundingClientRect = () => ({ left, top, width, height, right: left + width, bottom: top + height });
    Object.defineProperty(frame, 'clientWidth', { value: width });
    Object.defineProperty(frame, 'clientHeight', { value: height });
}

function placeElement(element, { left, top, width, height }) {
    element.getBoundingClientRect = () => ({ left, top, width, height, right: left + width, bottom: top + height });
}

describe('webview-tools-shim frame resolution', () => {
    it('acts on the page itself when the page marks no content frame', () => {
        const { bridge } = runShimWithFrame({ markContentFrame: false });

        const result = invoke(bridge, 'getHtml');

        expect(result.ok).toBe(true);
        expect(result.value.frame).toBe('top');
        expect(result.value.html).toContain('Editor shell');
    });

    it('acts on the content frame by default', () => {
        const { bridge } = runShimWithFrame();

        const result = invoke(bridge, 'getHtml');

        expect(result.ok).toBe(true);
        expect(result.value.frame).toBe('#preview');
        expect(result.value.html).toContain('Previewed page');
        expect(result.value.html).not.toContain('Editor shell');
    });

    it('acts on the page itself when named top', () => {
        const { bridge } = runShimWithFrame();

        const result = invoke(bridge, 'getHtml', { frame: 'top' });

        expect(result.value.frame).toBe('top');
        expect(result.value.html).toContain('Editor shell');
    });

    it('acts on a frame named by a selector, whether or not it is marked', () => {
        const { bridge } = runShimWithFrame({ markContentFrame: false });

        const result = invoke(bridge, 'getHtml', { frame: 'iframe' });

        expect(result.value.frame).toBe('#preview');
        expect(result.value.html).toContain('Previewed page');
    });

    it('acts on the page itself when the marked element is not a frame', () => {
        const { context } = runShimInDom('<!doctype html><html><body><div data-cel-content-frame>Shell</div></body></html>');

        const result = invoke(context[BRIDGE_KEY], 'getHtml');

        expect(result.value.frame).toBe('top');
    });

    it('reports a frame name that matches nothing, matches no frame, or does not parse', () => {
        const { bridge } = runShimWithFrame();

        expect(invoke(bridge, 'getHtml', { frame: '#missing' }).error).toContain("no frame matches '#missing'");
        expect(invoke(bridge, 'getHtml', { frame: 'h1' }).error).toContain('is a <h1>, not a frame');
        expect(invoke(bridge, 'getHtml', { frame: '<<<' }).error).toContain('invalid frame selector');
    });

    it('reports a frame as pending while the page marks it busy', () => {
        const { bridge, frame } = runShimWithFrame();
        frame.setAttribute('aria-busy', 'true');

        const pending = invoke(bridge, 'getHtml');

        expect(pending.ok).toBe(false);
        expect(pending.pending).toBe(true);
        expect(pending.frame).toBe('#preview');

        frame.removeAttribute('aria-busy');

        expect(invoke(bridge, 'getHtml').ok).toBe(true);
    });

    it('names the resolved frame and whether it is the page itself', () => {
        const { bridge } = runShimWithFrame();

        expect(invoke(bridge, 'resolveFrame').value).toEqual({ frame: '#preview', top: false });
        expect(invoke(bridge, 'resolveFrame', { frame: 'top' }).value).toEqual({ frame: 'top', top: true });
    });
});

describe('webview-tools-shim frame handlers', () => {
    it('queries and inspects inside the frame', () => {
        const { bridge } = runShimWithFrame({ frameContent: '<button id="go" aria-label="Go now">Go</button>' });

        const query = invoke(bridge, 'query', { role: 'button' });
        const inspect = invoke(bridge, 'inspect', { selector: '#go' });

        expect(query.value.frame).toBe('#preview');
        expect(query.value.totalMatches).toBe(1);
        expect(query.value.elements[0].selector).toBe('#go');
        expect(inspect.value.frame).toBe('#preview');
        expect(inspect.value.accessibleName).toBe('Go now');
    });

    it('clicks an element inside the frame with events from the frame window', () => {
        const { bridge, frame } = runShimWithFrame({ frameContent: '<button id="go">Go</button>' });
        let clickedFromFrameWindow = false;
        frame.contentDocument.getElementById('go').addEventListener('click', (event) => {
            clickedFromFrameWindow = event instanceof frame.contentWindow.MouseEvent;
        });

        const result = invoke(bridge, 'click', { selector: '#go' });

        expect(result.value.frame).toBe('#preview');
        expect(clickedFromFrameWindow).toBe(true);
    });

    it('fills an input inside the frame', () => {
        const { bridge, frame } = runShimWithFrame({ frameContent: '<input id="name">' });

        const result = invoke(bridge, 'fill', { selector: '#name', value: 'Alice' });

        expect(result.value.frame).toBe('#preview');
        expect(result.value.value).toBe('Alice');
        expect(frame.contentDocument.getElementById('name').value).toBe('Alice');
    });

    it('evaluates an expression in the frame and returns its value as JSON', () => {
        const { bridge, frame } = runShimWithFrame();
        frame.contentDocument.title = 'Framed page';

        const result = invoke(bridge, 'evaluate', { expression: 'document.title' });

        expect(result.value).toEqual({ frame: '#preview', valueJson: '"Framed page"' });
    });

    it('gives null for an expression that throws, does not parse, or has no value', () => {
        const { bridge } = runShimWithFrame();

        expect(invoke(bridge, 'evaluate', { expression: 'this is not valid javascript' }).value.valueJson).toBe('null');
        expect(invoke(bridge, 'evaluate', { expression: 'undefined' }).value.valueJson).toBe('null');
    });

    it('reports an EvalError, which the frame raises when its policy forbids evaluation', () => {
        const { bridge } = runShimWithFrame();

        const result = invoke(bridge, 'evaluate', { expression: 'throw new EvalError("blocked")' });

        expect(result.ok).toBe(false);
        expect(result.error).toContain('does not allow evaluating JavaScript');
    });

    it('reloads a frame and reports it as pending until the page that replaces it', () => {
        const { bridge } = runShimWithFrame();

        const reload = invoke(bridge, 'reload');

        expect(reload.value).toEqual({ frame: '#preview', top: false });
        // The test DOM does not navigate, so the frame keeps the window that the reload marked.
        expect(invoke(bridge, 'getHtml').pending).toBe(true);
    });

    it('leaves a reload of the page itself to the host', () => {
        const { bridge } = runShimWithFrame({ markContentFrame: false });

        expect(invoke(bridge, 'reload').value).toEqual({ frame: 'top', top: true });
        expect(invoke(bridge, 'getHtml').ok).toBe(true);
    });

    it('gives the frame viewport as the capture area, offset into the page', () => {
        const { bridge, frame } = runShimWithFrame();
        placeFrame(frame, { left: 100, top: 40, width: 600, height: 400 });

        const viewport = invoke(bridge, 'getViewport');

        expect(viewport.value).toMatchObject({ frame: '#preview', x: 100, y: 40, width: 600, height: 400 });
    });

    it('reports a frame with no area on screen', () => {
        const { bridge, frame } = runShimWithFrame();
        placeFrame(frame, { left: 0, top: 0, width: 0, height: 0 });

        expect(invoke(bridge, 'getViewport').error).toContain('is not showing');
    });

    it('offsets an element rectangle into the page and keeps only the part inside the frame', () => {
        const { bridge, frame } = runShimWithFrame({ frameContent: '<p id="inside">In view</p><p id="clipped">Half</p><p id="outside">Away</p>' });
        placeFrame(frame, { left: 100, top: 40, width: 600, height: 400 });
        const frameDocument = frame.contentDocument;
        placeElement(frameDocument.getElementById('inside'), { left: 10, top: 20, width: 50, height: 30 });
        placeElement(frameDocument.getElementById('clipped'), { left: 10, top: 380, width: 50, height: 40 });
        placeElement(frameDocument.getElementById('outside'), { left: 10, top: 900, width: 50, height: 40 });

        expect(invoke(bridge, 'getRect', { selector: '#inside' }).value)
            .toEqual({ frame: '#preview', x: 110, y: 60, width: 50, height: 30 });
        expect(invoke(bridge, 'getRect', { selector: '#clipped' }).value)
            .toEqual({ frame: '#preview', x: 110, y: 420, width: 50, height: 20 });
        expect(invoke(bridge, 'getRect', { selector: '#outside' }).error)
            .toContain('outside the visible part of the frame');
    });
});

describe('webview-tools-shim frame buffers', () => {
    it('drains the console of the page and of the shims in its frames, naming the frame of each entry', () => {
        const { bridge, context, frame } = runShimWithFrame();
        frame.contentWindow[BRIDGE_KEY] = {
            drainConsole: () => [{ level: 'log', timestampMs: 2, args: ['from the frame'] }]
        };
        context.console.log('from the page');

        const result = invoke(bridge, 'flushConsole');

        expect(result.ok).toBe(true);
        expect(result.value.map((entry) => [entry.frame, entry.args[0]])).toEqual([
            ['top', 'from the page'],
            ['#preview', 'from the frame']
        ]);
    });

    it('drains the console while a frame is still loading', () => {
        const { bridge, context, frame } = runShimWithFrame();
        frame.setAttribute('aria-busy', 'true');
        context.console.log('logged during the load');

        const result = invoke(bridge, 'flushConsole');

        expect(result.ok).toBe(true);
        expect(result.value).toHaveLength(1);
    });

    it('drains the network buffers of the shims in the frames', () => {
        const { bridge, frame } = runShimWithFrame();
        frame.contentWindow[BRIDGE_KEY] = {
            drainNetwork: () => [{ id: 1, type: 'fetch', url: 'data.json' }]
        };

        const result = invoke(bridge, 'flushNetwork');

        expect(result.value).toEqual([{ id: 1, type: 'fetch', url: 'data.json', frame: '#preview' }]);
    });
});

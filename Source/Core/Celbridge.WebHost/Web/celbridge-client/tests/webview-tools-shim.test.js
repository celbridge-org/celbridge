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
function runShimInDom(html, hostname = 'project.celbridge') {
    const dom = new JSDOM(html, { url: `https://${hostname}/` });
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

    it('installs the bridge on project.celbridge', () => {
        const context = runShim('project.celbridge');
        expect(context[BRIDGE_KEY]).toBeDefined();
    });

    it('installs the bridge on a package virtual host', () => {
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
        const context = runShim('project.celbridge');
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

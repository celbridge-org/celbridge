import { describe, it, expect } from 'vitest';
import { ResourcesAPI } from '../api/resources-api.js';

function createTransport() {
    const listeners = {};

    return {
        requests: [],
        async request(method, params) {
            this.requests.push({ method, params });
            return null;
        },
        addEventListener(method, handler) {
            (listeners[method] ??= []).push(handler);
        },
        removeEventListener(method, handler) {
            listeners[method] = (listeners[method] ?? []).filter(h => h !== handler);
        },
        emit(method, params) {
            for (const handler of listeners[method] ?? []) {
                handler(params);
            }
        }
    };
}

describe('ResourcesAPI', () => {
    it('subscribes with the patterns given', async () => {
        const transport = createTransport();
        const resources = new ResourcesAPI(transport);

        await resources.subscribe(['*.py', 'src/**/*.ts']);

        expect(transport.requests).toEqual([{
            method: 'resources/subscribe',
            params: { patterns: ['*.py', 'src/**/*.ts'] }
        }]);
    });

    it('subscribes to everything when no patterns are given', async () => {
        const transport = createTransport();
        const resources = new ResourcesAPI(transport);

        await resources.subscribe();

        expect(transport.requests[0].params).toEqual({ patterns: [] });
    });

    it('unsubscribes over the bridge', async () => {
        const transport = createTransport();
        const resources = new ResourcesAPI(transport);

        await resources.unsubscribe();

        expect(transport.requests).toEqual([{ method: 'resources/unsubscribe', params: {} }]);
    });

    it('hands each change to the handler, with oldResource only for a rename', () => {
        const transport = createTransport();
        const resources = new ResourcesAPI(transport);
        const changes = [];
        resources.onChanged(change => changes.push(change));

        transport.emit('resources/changed', { kind: 'changed', resource: 'project:main.py' });
        transport.emit('resources/changed', { kind: 'renamed', resource: 'project:b.py', oldResource: 'project:a.py' });

        expect(changes).toEqual([
            { kind: 'changed', resource: 'project:main.py', oldResource: null },
            { kind: 'renamed', resource: 'project:b.py', oldResource: 'project:a.py' }
        ]);
    });

    it('stops calling a handler once it is removed', () => {
        const transport = createTransport();
        const resources = new ResourcesAPI(transport);
        const changes = [];
        const remove = resources.onChanged(change => changes.push(change));

        remove();
        transport.emit('resources/changed', { kind: 'created', resource: 'project:new.js' });

        expect(changes).toEqual([]);
    });
});

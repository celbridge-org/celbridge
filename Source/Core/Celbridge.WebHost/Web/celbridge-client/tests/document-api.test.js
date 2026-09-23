import { describe, it, expect } from 'vitest';
import { DocumentAPI, projectUrl } from '../api/document-api.js';
import { RpcTransport } from '../core/rpc-transport.js';

describe('projectUrl', () => {
    it('strips the project: prefix when present', () => {
        // Regression: a naive concatenation produced URLs like
        // /project/project:packages/foo.png which 404'd because the loopback
        // /project/ route treats the URL path as relative to the project folder.
        const url = projectUrl('project:packages/king-fury/sprites/piece_bishop.png');
        expect(url).toBe('/project/packages/king-fury/sprites/piece_bishop.png');
    });

    it('returns the bare base URL for an empty resource key', () => {
        expect(projectUrl('')).toBe('/project/');
        expect(projectUrl(null)).toBe('/project/');
        expect(projectUrl(undefined)).toBe('/project/');
    });

    it('passes through a key with no project: prefix', () => {
        // The helper is intentionally lenient on its input. A caller that
        // already trimmed the prefix should still get a sane URL.
        expect(projectUrl('packages/foo.png')).toBe('/project/packages/foo.png');
    });
});

describe('DocumentAPI.writeReport', () => {
    it('sends the report as JSON and returns the resource key it opens by', async () => {
        const requests = [];
        const transport = {
            async request(method, params) {
                requests.push({ method, params });
                return { resource: 'logs:reports/acme-tiles-convert.report' };
            }
        };

        const report = {
            id: 'acme-tiles-convert',
            title: 'Convert Tilesets',
            severity: 'warning',
            summary: '1 tileset could not be converted.',
            sections: []
        };

        const resource = await new DocumentAPI(transport).writeReport(report);

        expect(requests[0].method).toBe('document/writeReport');
        expect(JSON.parse(requests[0].params.reportJson)).toEqual(report);
        expect(resource).toBe('logs:reports/acme-tiles-convert.report');
    });
});

describe('DocumentAPI.onRestoreState', () => {
    function createHostedDocument() {
        const sent = [];
        let messageHandler = null;

        const transport = new RpcTransport({
            postMessage: (message) => sent.push(JSON.parse(message)),
            onMessage: (handler) => { messageHandler = handler; }
        });

        const requestRestore = (id, state) => messageHandler(JSON.stringify({
            jsonrpc: '2.0',
            method: 'document/restoreState',
            params: [state],
            id
        }));

        return { document: new DocumentAPI(transport), sent, requestRestore };
    }

    const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

    it('answers the host only once an async handler has finished restoring', async () => {
        const { document, sent, requestRestore } = createHostedDocument();

        const restored = [];
        let finishRestore;
        document.onRestoreState((state) => {
            restored.push(state);
            return new Promise((resolve) => { finishRestore = resolve; });
        });

        requestRestore(7, '{"scroll":120}');
        await settle();

        expect(restored).toEqual(['{"scroll":120}']);
        expect(sent).toHaveLength(0);

        finishRestore();
        await settle();

        expect(sent).toEqual([{ jsonrpc: '2.0', id: 7, result: null }]);
    });
});

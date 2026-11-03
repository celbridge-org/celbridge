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

    it('encodes each segment, so a name holding # or ? or % names the file', () => {
        expect(projectUrl('project:site/chart #1?.html')).toBe('/project/site/chart%20%231%3F.html');
        expect(projectUrl('project:100%/page.html')).toBe('/project/100%25/page.html');
    });

    it('keeps the trailing slash of a folder', () => {
        expect(projectUrl('project:docs/')).toBe('/project/docs/');
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

describe('DocumentAPI pip', () => {
    function createTransport() {
        const requests = [];

        return {
            requests,
            async request(method, params) {
                requests.push({ method, params });
                return {};
            }
        };
    }

    it('sends setPip with the kind and the label, and no pulse by default', async () => {
        const transport = createTransport();

        await new DocumentAPI(transport).setPip('alert', 'Syncing');

        expect(transport.requests).toEqual([
            { method: 'document/setPip', params: { kind: 'alert', label: 'Syncing', pulse: false } }
        ]);
    });

    it('sends the pulse the options ask for', async () => {
        const transport = createTransport();

        await new DocumentAPI(transport).setPip('neutral', 'Syncing', { pulse: true });

        expect(transport.requests).toEqual([
            { method: 'document/setPip', params: { kind: 'neutral', label: 'Syncing', pulse: true } }
        ]);
    });

    it('sends clearPip with no parameters', async () => {
        const transport = createTransport();

        await new DocumentAPI(transport).clearPip();

        expect(transport.requests).toEqual([
            { method: 'document/clearPip', params: {} }
        ]);
    });

    it('rejects when the host refuses the pip', async () => {
        const transport = {
            async request() {
                throw new Error("Unknown pip kind: 'urgent'.");
            }
        };

        await expect(new DocumentAPI(transport).setPip('urgent', 'Syncing')).rejects.toThrow('urgent');
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

describe('DocumentAPI file watches', () => {
    function createHostedDocument({ isInitialized = true } = {}) {
        const sent = [];
        let messageHandler = null;

        const transport = new RpcTransport({
            postMessage: (message) => sent.push(JSON.parse(message)),
            onMessage: (handler) => { messageHandler = handler; }
        });
        if (isInitialized) {
            transport.markInitialized();
        }

        const receive = (message) => messageHandler(JSON.stringify({ jsonrpc: '2.0', ...message }));
        const lastRequestId = () => sent[sent.length - 1].id;

        return {
            document: new DocumentAPI(transport),
            sent,
            answer: () => receive({ id: lastRequestId(), result: {} }),
            refuse: (message) => receive({ id: lastRequestId(), error: { code: -32000, message } }),
            sendChanges: (watchId, changes) => receive({
                method: 'document/filesChanged',
                params: { watchId, changes }
            })
        };
    }

    async function watch(hosted, options, handler) {
        const watching = hosted.document.watchFiles(options, handler);
        hosted.answer();
        return watching;
    }

    it('sends watchFiles with the defaults, and gives each watch a new id', async () => {
        const hosted = createHostedDocument();

        const firstId = await watch(hosted, {}, () => { });
        const secondId = await watch(hosted, { root: 'temp:', include: '*.py', exclude: 'history/' }, () => { });

        expect(firstId).toBe(1);
        expect(secondId).toBe(2);
        expect(hosted.sent.map(({ method, params }) => ({ method, params }))).toEqual([
            {
                method: 'document/watchFiles',
                params: { watchId: 1, root: 'project:', include: '', exclude: '' }
            },
            {
                method: 'document/watchFiles',
                params: { watchId: 2, root: 'temp:', include: '*.py', exclude: 'history/' }
            }
        ]);
    });

    it('calls each handler with only the changes of its own watch', async () => {
        const hosted = createHostedDocument();
        const scripts = [];
        const notes = [];

        const scriptsId = await watch(hosted, { include: '*.py' }, (changes) => scripts.push(changes));
        const notesId = await watch(hosted, { include: '*.md' }, (changes) => notes.push(changes));

        hosted.sendChanges(scriptsId, [{ resource: 'project:main.py', state: 'file' }]);
        hosted.sendChanges(notesId, [{ resource: 'project:notes.md', state: 'absent' }]);

        expect(scripts).toEqual([[{ resource: 'project:main.py', state: 'file' }]]);
        expect(notes).toEqual([[{ resource: 'project:notes.md', state: 'absent' }]]);
    });

    it('sends unwatchFiles with the id, and drops a batch that arrives afterwards', async () => {
        const hosted = createHostedDocument();
        const received = [];

        const watchId = await watch(hosted, {}, (changes) => received.push(changes));
        const unwatching = hosted.document.unwatchFiles(watchId);
        hosted.answer();
        await unwatching;
        hosted.sendChanges(watchId, [{ resource: 'project:main.py', state: 'file' }]);

        expect(hosted.sent[1].method).toBe('document/unwatchFiles');
        expect(hosted.sent[1].params).toEqual({ watchId: 1 });
        expect(received).toEqual([]);
    });

    it('leaves no handler registered when the host rejects the watch', async () => {
        const hosted = createHostedDocument();
        const received = [];

        const watching = hosted.document.watchFiles({ root: 'logs:' }, (changes) => received.push(changes));
        hosted.refuse("The resource root 'logs' is not watched for changes");
        await expect(watching).rejects.toThrow('not watched');
        hosted.sendChanges(1, [{ resource: 'logs:app.log', state: 'file' }]);

        expect(received).toEqual([]);
    });

    it('refuses a watch with no handler', async () => {
        const hosted = createHostedDocument();

        await expect(hosted.document.watchFiles({})).rejects.toThrow(TypeError);
        expect(hosted.sent).toEqual([]);
    });

    it('refuses a watch before the client has initialized', async () => {
        const hosted = createHostedDocument({ isInitialized: false });

        await expect(hosted.document.watchFiles({}, () => { })).rejects.toThrow('initialize()');
        expect(hosted.sent).toEqual([]);
    });
});

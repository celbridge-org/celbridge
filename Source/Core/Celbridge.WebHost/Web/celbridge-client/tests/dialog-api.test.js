import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { DialogAPI } from '../api/dialog-api.js';
import { RpcTransport } from '../core/rpc-transport.js';

function createTransport() {
    const requests = [];

    return {
        requests,
        async request(method, params) {
            requests.push({ method, params });
            return { resource: 'logs:reports/acme-tiles-convert.report' };
        }
    };
}

describe('DialogAPI.pickIcon', () => {
    it('sends the text the field holds, and returns the chosen name', async () => {
        const transport = createTransport();
        transport.request = async (method, params) => {
            transport.requests.push({ method, params });
            return { iconName: 'bs-gear' };
        };
        const dialog = new DialogAPI(transport);

        const iconName = await dialog.pickIcon('bs-floppy');

        expect(iconName).toBe('bs-gear');
        expect(transport.requests).toEqual([{
            method: 'dialog/pickIcon',
            params: { searchText: 'bs-floppy' }
        }]);
    });

    it('returns null when the picker is dismissed', async () => {
        const transport = createTransport();
        transport.request = async () => ({ iconName: null });
        const dialog = new DialogAPI(transport);

        await expect(dialog.pickIcon('')).resolves.toBeNull();
    });
});

describe('DialogAPI.pickResource', () => {
    function createPickerTransport(resource) {
        const transport = createTransport();
        transport.request = async (method, params) => {
            transport.requests.push({ method, params });
            return { resource };
        };
        return transport;
    }

    it('sends the options over one request, and returns the chosen key', async () => {
        const transport = createPickerTransport('project:images/hero.png');
        const dialog = new DialogAPI(transport);

        const resource = await dialog.pickResource({
            mode: 'open',
            extensions: ['.png'],
            defaultResource: 'project:images/hero.png',
            title: 'Choose Artwork',
            preview: true
        });

        expect(resource).toBe('project:images/hero.png');
        expect(transport.requests).toEqual([{
            method: 'dialog/pickResource',
            params: {
                mode: 'open',
                extensions: ['.png'],
                defaultResource: 'project:images/hero.png',
                title: 'Choose Artwork',
                preview: true
            }
        }]);
    });

    it('asks where to save in save mode', async () => {
        const transport = createPickerTransport('project:exports/hero-export.png');
        const dialog = new DialogAPI(transport);

        const resource = await dialog.pickSaveLocation('project:exports/hero-export.png', ['.png']);

        expect(resource).toBe('project:exports/hero-export.png');
        expect(transport.requests[0].params).toEqual({
            mode: 'save',
            extensions: ['.png'],
            defaultResource: 'project:exports/hero-export.png',
            title: undefined,
            preview: false
        });
    });

    it('asks for a folder in folder mode', async () => {
        const transport = createPickerTransport('project:');
        const dialog = new DialogAPI(transport);

        const resource = await dialog.pickFolder();

        expect(resource).toBe('project:');
        expect(transport.requests[0].params).toEqual({
            mode: 'folder',
            extensions: undefined,
            defaultResource: undefined,
            title: undefined,
            preview: false
        });
    });

    it('returns null when the user cancels', async () => {
        const dialog = new DialogAPI(createPickerTransport(null));

        await expect(dialog.pickSaveLocation('project:notes.md')).resolves.toBeNull();
    });
});

describe('DialogAPI.showNotification', () => {
    it('sends the severity and message over the bridge', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.showNotification('warning', '9 of 40 tilesets failed to convert');

        expect(transport.requests).toEqual([{
            method: 'dialog/showNotification',
            params: {
                severity: 'warning',
                message: '9 of 40 tilesets failed to convert',
                resource: undefined,
                label: undefined,
                line: 0,
                column: 0
            }
        }]);
    });

    it('resolves without a value, since nothing is asked of the user', async () => {
        // The promise resolving means the host took the notification, not that the user saw it.
        const dialog = new DialogAPI(createTransport());

        await expect(dialog.showNotification('info', 'Conversion complete')).resolves.toBeUndefined();
    });

    it('flattens an action into the resource, label and position the host takes', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.showNotification('error', 'config.json has a syntax error', {
            resource: 'project:config.json',
            label: 'Open config.json',
            line: 42,
            column: 7
        });

        expect(transport.requests[0].params).toEqual({
            severity: 'error',
            message: 'config.json has a syntax error',
            resource: 'project:config.json',
            label: 'Open config.json',
            line: 42,
            column: 7
        });
    });

    it('defaults an action position to zero, which opens at the top', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.showNotification('info', 'Report written', { resource: 'logs:reports/convert.report' });

        expect(transport.requests[0].params.line).toBe(0);
        expect(transport.requests[0].params.column).toBe(0);
    });
});

// A dialog is answered by the user, who may take minutes, so these calls wait without a timeout.
describe('DialogAPI dialogs that wait for the user', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    function createHostedDialog() {
        const sent = [];
        let messageHandler = null;

        const transport = new RpcTransport({
            postMessage: (message) => sent.push(JSON.parse(message)),
            onMessage: (handler) => { messageHandler = handler; }
        });

        const respond = (id, result) => messageHandler(JSON.stringify({ jsonrpc: '2.0', result, id }));

        return { dialog: new DialogAPI(transport), sent, respond };
    }

    it.each([
        ['pickImage', (dialog) => dialog.pickImage(['.png']), { path: '/tmp/late.png' }, '/tmp/late.png'],
        ['pickFile', (dialog) => dialog.pickFile(['.txt']), { path: '/tmp/late.txt' }, '/tmp/late.txt'],
        ['pickResource', (dialog) => dialog.pickSaveLocation('project:late.txt'), { resource: 'project:late.txt' }, 'project:late.txt'],
        ['pickIcon', (dialog) => dialog.pickIcon(''), { iconName: 'bs-gear' }, 'bs-gear'],
        ['showAlert', (dialog) => dialog.showAlert('Title', 'Message'), {}, undefined]
    ])('resolves %s when the user answers long after the default timeout', async (_, open, result, expected) => {
        const { dialog, sent, respond } = createHostedDialog();

        const pending = open(dialog);

        vi.advanceTimersByTime(300000);

        respond(sent[0].id, result);

        await expect(pending).resolves.toBe(expected);
    });

    it('keeps the default timeout for a notification, which the user never answers', async () => {
        const { dialog } = createHostedDialog();

        const pending = dialog.showNotification('info', 'Report written');
        const rejects = expect(pending).rejects.toThrow('Request timeout: dialog/showNotification (30000ms)');

        vi.advanceTimersByTime(30000);

        await rejects;
    });
});

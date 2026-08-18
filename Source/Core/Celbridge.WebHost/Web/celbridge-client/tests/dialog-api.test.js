import { describe, it, expect } from 'vitest';
import { DialogAPI } from '../api/dialog-api.js';

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

describe('DialogAPI.toast', () => {
    it('sends the severity and message over the bridge', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.toast('warning', '9 of 40 tilesets failed to convert');

        expect(transport.requests).toEqual([{
            method: 'dialog/toast',
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

        await expect(dialog.toast('info', 'Conversion complete')).resolves.toBeUndefined();
    });

    it('flattens an action into the resource, label and position the host takes', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.toast('error', 'config.json has a syntax error', {
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

        await dialog.toast('info', 'Report written', { resource: 'logs:reports/convert.report' });

        expect(transport.requests[0].params.line).toBe(0);
        expect(transport.requests[0].params.column).toBe(0);
    });
});

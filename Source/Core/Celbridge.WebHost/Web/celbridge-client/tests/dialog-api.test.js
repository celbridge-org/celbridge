import { describe, it, expect } from 'vitest';
import { DialogAPI } from '../api/dialog-api.js';

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

describe('DialogAPI.notify', () => {
    it('sends the severity and message over the bridge', async () => {
        const transport = createTransport();
        const dialog = new DialogAPI(transport);

        await dialog.notify('warning', '9 of 40 tilesets failed to convert');

        expect(transport.requests).toEqual([{
            method: 'dialog/notify',
            params: { severity: 'warning', message: '9 of 40 tilesets failed to convert' }
        }]);
    });

    it('resolves without a value, since nothing is asked of the user', async () => {
        // The promise resolving means the host took the notification, not that the user saw it.
        const dialog = new DialogAPI(createTransport());

        await expect(dialog.notify('info', 'Conversion complete')).resolves.toBeUndefined();
    });
});

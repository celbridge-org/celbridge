import { describe, it, expect } from 'vitest';
import { UtilityAPI } from '../api/utility-api.js';

function createTransport() {
    return {
        requests: [],
        async request(method, params) {
            this.requests.push({ method, params });
            return null;
        }
    };
}

describe('UtilityAPI', () => {
    it('sets the indicator tone and label over the bridge', async () => {
        const transport = createTransport();
        const utility = new UtilityAPI(transport);

        await utility.setIndicator('danger', 'Recording', 'bs-record-circle-fill');

        expect(transport.requests).toEqual([{
            method: 'utility/setIndicator',
            params: { tone: 'danger', label: 'Recording', icon: 'bs-record-circle-fill' }
        }]);
    });

    it('sends an empty label when none is given', async () => {
        const transport = createTransport();
        const utility = new UtilityAPI(transport);

        await utility.setIndicator('caution');

        expect(transport.requests[0].params).toEqual({ tone: 'caution', label: '', icon: '' });
    });

    it('clears the indicator with the none tone', async () => {
        const transport = createTransport();
        const utility = new UtilityAPI(transport);

        await utility.clearIndicator();

        expect(transport.requests).toEqual([{
            method: 'utility/setIndicator',
            params: { tone: 'none', label: '', icon: '' }
        }]);
    });
});

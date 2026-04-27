import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
    test: {
        include: ['tests/**/*.test.js'],
        environment: 'jsdom',
        alias: {
            'https://shared.celbridge/celbridge-client/celbridge.js':
                fileURLToPath(new URL('./tests/fixtures/celbridge-stub.js', import.meta.url)),
            'https://shared.celbridge/celbridge-client/api/document-api.js':
                fileURLToPath(new URL('./tests/fixtures/document-api-stub.js', import.meta.url))
        }
    }
});

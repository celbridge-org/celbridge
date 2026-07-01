import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
    test: {
        include: ['tests/**/*.test.js'],
        environment: 'jsdom',
        alias: {
            '/assets/celbridge-client/celbridge.js':
                fileURLToPath(new URL('./tests/fixtures/celbridge-stub.js', import.meta.url)),
            '/assets/celbridge-client/api/document-api.js':
                fileURLToPath(new URL('./tests/fixtures/document-api-stub.js', import.meta.url)),
            '/assets/celbridge-client/localization.js':
                fileURLToPath(new URL('./tests/fixtures/localization-stub.js', import.meta.url))
        }
    }
});

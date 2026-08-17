import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
    test: {
        include: ['tests/**/*.test.js'],
        environment: 'jsdom',
        alias: {
            '/assets/celbridge-client/localization.js':
                fileURLToPath(new URL('./tests/fixtures/localization-stub.js', import.meta.url))
        }
    }
});

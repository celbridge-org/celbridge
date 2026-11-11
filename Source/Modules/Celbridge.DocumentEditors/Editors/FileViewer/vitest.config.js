import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
    test: {
        include: ['tests/**/*.test.js'],
        environment: 'jsdom',
        alias: {
            // The localization module uses only a table of strings and the DOM, so jsdom runs the real module.
            '/assets/celbridge-client/localization.js':
                fileURLToPath(new URL(
                    '../../../../Core/Celbridge.WebHost/Web/celbridge-client/localization.js',
                    import.meta.url))
        }
    }
});
